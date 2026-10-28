namespace Winland.Application;

/// <summary>The core of Winland. Every input and command runs as one step under one lock and returns an Outcome for the adapter to apply.</summary>
public sealed partial class WindowManager
{
    private readonly WorkspaceSet _workspaces = new();
    private readonly List<LiveMonitor> _monitors = [];
    private readonly ProcessFamilies<IReadOnlyList<WindowRule>> _follow = new();
    private readonly ProcessFamilies<WorkspaceId> _initialWorkspaces = new();
    private readonly BindSession _keyState = new();

    // Pipe requests arrive on another thread than binds and window events.
    private readonly Lock _gate = new();

    // The config in force. The hook thread reads it outside the lock, so a reload replaces it whole.
    private volatile AppliedConfig _config = AppliedConfig.Unconfigured;

    // One lookup for every Workspace's options, so they still compare equal.
    private readonly Func<ClientId, int?> _clientBorder;
    private BorderColor? _accent;
    private HighContrast? _contrast;
    private double _textScale = 1;
    private Rectangle _area;
    private readonly FocusState _focus;
    private readonly ClientProps _props = new();
    private List<StreamEvent>? _configEvents;
    private List<StreamEvent>? _noted;
    private readonly HashSet<WindowId> _concealedMinimized = [];

    // The last outcome a step returned with something to apply. The next step is empty when it asks for nothing more.
    private Outcome? _applied;

    public event Action<Outcome>? Noticed;

    /// <summary>
    /// Called under the lock with each outcome a step decided that asks for something, so they are handed on in the
    /// order they were decided. The adapter queues them here and applies them in that order.
    /// </summary>
    public Action<Outcome>? Decided { get; set; }

    public Func<ConfigRead>? ReadConfig { get; set; }

    /// <summary>The keyboard layout a config is read in, for a <c>keyword</c> line. The United States layout without it.</summary>
    public Func<KeyLayout>? KeyLayoutSource { get; set; }

    /// <summary>
    /// Whether saving the config reloads it. <c>misc:disable_autoreload</c> turns it off, and turning the option off
    /// again, with <c>keyword</c> or a reload by hand, turns it back on.
    /// </summary>
    public bool ReloadsOnSave => !_config.Session.DisableAutoreload;

    /// <summary><c>general:hide_taskbar</c>: the taskbars are hidden and set to auto-hide while Winland runs.</summary>
    public bool HideTaskbar => _config.Session.HideTaskbar;

    public ICursorPosition? Cursor { get; set; }

    public SessionPhase? SessionLock { get; set; }

    public Action<ConfigDocument>? AfterReload { get; set; }

    public WindowManager(ConfigDocument? config = null)
    {
        _clientBorder = ClientBorderOf;
        _focus = new FocusState(window => !HasNoFocus(window));
        _cover = new FullscreenCover(_pins.IsPinned);
        TrackWindowState();
        if (config is not null)
            Use(AppliedConfig.From(config, accent: null));
        _loaded = config;
        _workspaces.Shown.Show(MonitorId.Primary, WorkspaceId.Initial);
        WorkspaceFor(WorkspaceId.Initial);
        EnsurePersistent();
        Remember(WorkspaceId.Initial, MonitorId.Primary);
    }

    public BindTable Binds => _config.Binds;

    /// <summary>Bind state the hook matches against.</summary>
    public BindSession KeyState => _keyState;

    /// <summary>Matches input against the published bind table. Does not take the core lock.</summary>
    public BindDecision Decide(Input input, BindSession session)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(session);
        var decision = session.Decide(_config.Binds, input);

        return decision;
    }

    internal void WhileLocked(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
            action();
    }

    public Outcome Handle(Input input)
    {
        ArgumentNullException.ThrowIfNull(input);
        lock (_gate)
        {
            // Closing a program destroys many windows that were never Clients, such as its child windows. Each only
            // needs what Winland noted about it forgotten, not a whole step.
            if (input is WindowClosed closed && _applied is { } last && WorkspaceContaining(closed.Window) is null)
            {
                _windowState.Forget(closed.Window);
                return Outcome.Empty(last.State);
            }

            return Step(input, () => HandleInput(input));
        }
    }

    public IReadOnlyList<Launch> StartupLaunches(ConfigDocument config)
    {
        lock (_gate)
            return ConfigLaunches(config, LaunchParser.LaunchMoment.Startup, LaunchWorkspace());
    }

    public IReadOnlyList<Launch> ReloadLaunches(ConfigDocument config)
    {
        lock (_gate)
            return ConfigLaunches(config, LaunchParser.LaunchMoment.Reload, LaunchWorkspace());
    }

    public static IReadOnlyList<Launch> ShutdownLaunches(ConfigDocument config) =>
        ConfigLaunches(config, LaunchParser.LaunchMoment.Shutdown);

    public void NoteLaunched(int processId, Launch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (processId <= 0)
            return;

        lock (_gate)
        {
            // A rule block follows the started program for a minute.
            if (launch.Rules.Count > 0)
                _follow.Track(processId, launch.Rules, Clock() + 60_000);

            // A started program has ten seconds to open its window.
            if (launch.InitialWorkspace is WorkspaceId workspace)
                _initialWorkspaces.Track(processId, workspace, Clock() + 10_000);
        }
    }

    internal void NoteLineage(int processId, IReadOnlyList<int> ancestors)
    {
        lock (_gate)
        {
            _follow.NoteLineage(processId, ancestors);
            _initialWorkspaces.NoteLineage(processId, ancestors);
        }
    }

    public CommandReply Execute(string? line)
    {
        var parsed = CommandProtocol.Parse(line);
        CommandReply result;
        ConfigDocument? reloaded;
        lock (_gate)
        {
            // A query reads the State and changes nothing, so it runs no step and has nothing to apply.
            if (parsed is CommandParsed { Request: var request } && Read(request) is { } text)
                return new CommandReply(text, Outcome.Empty(Snapshot()));

            var reply = default(CommandReply);
            var outcome = Step(null, () => (reply = ExecuteLine(parsed)).Effect);
            result = reply with { Effect = outcome };
            reloaded = _reloaded;
            _reloaded = null;
        }

        if (reloaded is not null)
        {
            try
            {
                AfterReload?.Invoke(reloaded);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.Error.WriteLine(exception);
            }
        }

        return result;
    }

    // The config a reload in this Execute put in force, for AfterReload once the lock is released.
    private ConfigDocument? _reloaded;

    // Every input and every command runs as one step: the change, then the rules matched again, the fullscreen
    // hiding, the decorations and the Motions, and only then the events and the cursor, so they describe the
    // finished State. The scratch state a step gathers is dropped when it ends.
    private Outcome Step(Input? input, Func<Outcome> run)
    {
        var before = Capture();
        var wasClient = input is WindowAppeared appeared && Find(appeared.Window) is not null;
        var config = _config;
        try
        {
            var outcome = run();
            CheckShownWorkspaces();
            if (input is not null)
                Note(input, wasClient);

            outcome = WithEvents(Decorate(outcome, before.State), before, input);

            // A step that loads a config is not remembered: its caller may not apply it, so the next step applies
            // everything again.
            var loaded = !ReferenceEquals(config, _config);
            if (!loaded && outcome.IsEmptyAfter(_applied))
                return Outcome.Empty(outcome.State);

            _applied = loaded ? null : outcome.Lasting();
            Decided?.Invoke(outcome);
            Emit(outcome);
            return outcome;
        }
        finally
        {
            _configEvents = null;
            _extraEvents = null;
            _noted = null;
            _activated = null;
            _readdressed = null;
            _crossMonitorWarp = null;
            _movedItself = null;
            _restoring = null;
        }
    }

    private Outcome HandleInput(Input input) =>
        input switch
        {
            WindowAppeared appeared => AppearMaybeMaximized(appeared),
            ConfigLoaded loaded => Load(loaded),
            KeyPressed or KeyReleased => RunKey(input),
            PointerButtonPressed pressed => PressPointer(pressed),
            PointerButtonReleased released => ReleasePointer(released),
            PointerMoved moved => MovePointer(moved),
            WheelTurned wheel => RunKey(wheel),
            PointerGesture gesture => ApplyGesture(gesture),
            GroupBarScrolled scrolled => ScrollGroupBar(scrolled),
            GroupTabClosed closed => View([], close: closed.Window),
            WindowMaximizeRequested requested => MaximizeRequest(requested.Window),
            WindowFullscreenRequested requested => FullscreenRequest(requested),
            WindowDrifted drifted => PutBack(drifted),
            WindowSizeStarted sizing => BeginNativeSize(sizing),
            WindowSizeEnded sized => EndNativeSize(sized),
            WindowSizing sizing => ContinueNativeSize(sizing),
            WindowMoveStarted started => BeginNativeMove(started),
            WindowMoveEnded ended => EndNativeMove(ended),
            ClickKillChosen chosen => ChooseKill(chosen),
            SwipeBegan => BeginSwipe(),
            SwipeUpdated updated => UpdateSwipe(updated),
            SwipeEnded => EndSwipe(),
            Dispatch dispatched => ExecuteTriggered(dispatched.Name, dispatched.Arguments, dispatched.Trigger),
            WindowFlashed flashed => Flash(flashed.Window),
            WindowFocused focused => Consider(focused),
            WindowClosed closed => Close(closed.Window, closed.Cloaked),
            WindowEnabledChanged changed => ChangeEnabled(changed),
            WindowLimitsChanged limits => ChangeLimits(limits),
            WindowMinimized minimized => Minimize(minimized.Window),
            WindowRestored restored => Restore(restored),
            WindowResized resized => Reapply(resized),
            WindowTitleChanged retitled => Retitle(retitled),
            WindowClassChanged reclassed => Reclass(reclassed),
            Configure configure => Apply(configure.Document),
            AccentChanged accent => NoteAccent(accent),
            RestoredAdopted => ArrangeRestored(),
            Preselect preselect => View(CurrentWorkspace().Layout.Preselect(preselect.Direction)),
            Pseudo pseudo => ChangePseudo(pseudo.Window),
            MonitorsChanged changed => SetMonitors(changed.Monitors),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "No handler for this input."),
        };

    private static IReadOnlyList<Launch> ConfigLaunches(ConfigDocument config, LaunchParser.LaunchMoment moment, WorkspaceId? initialWorkspace = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        return LaunchParser.FromConfig(config, moment, initialWorkspace);
    }

    // The Workspace a program started now is started on: the special workspace the focused Monitor shows, or its
    // Workspace.
    private WorkspaceId LaunchWorkspace()
    {
        var monitor = FocusedId();
        return _workspaces.Shown.SpecialOn(monitor) ?? _workspaces.Shown.ActiveOn(monitor);
    }

    /// <summary>
    /// The time in milliseconds, for rule timers, launch follows and pointer events that carry no time of their own.
    /// Tests set it; the default is the system tick count.
    /// </summary>
    internal Func<long> Clock { get; set; } = static () => Environment.TickCount64;

    private void Emit(Outcome outcome)
    {
        if (outcome.Events.Count == 0)
            return;

        var handlers = Noticed;
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<Outcome>)handler).Invoke(outcome);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.Error.WriteLine(exception);
            }
        }
    }
}
