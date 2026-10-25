namespace Winland.Application;

/// <summary>The core of Winland. Every input and command runs as one step under one lock and returns an Outcome for the adapter to apply.</summary>
public sealed partial class WindowManager
{
    private readonly WorkspaceSet _workspaces = new();
    private readonly List<LiveMonitor> _monitors = [];
    private readonly PidFollowSet _follow = new();
    private readonly InitialWorkspaces _initialWorkspaces = new();
    private readonly BindSession _keyState = new();

    // Pipe requests arrive on another thread than binds and window events.
    private readonly Lock _gate = new();

    // The config in force. The hook thread reads it outside the lock, so a reload replaces it whole.
    private volatile AppliedConfig _config = AppliedConfig.Unconfigured;

    // One lookup for every Workspace's options, so they still compare equal.
    private readonly Func<ClientId, int?> _clientBorder;
    private BorderColor? _accent;
    private Rectangle _area;
    private readonly FocusState _focus;
    private readonly ClientProps _props = new();
    private List<StreamEvent>? _configEvents;
    private List<StreamEvent>? _noted;
    private readonly HashSet<WindowId> _concealedMinimized = [];

    private static long Cores;
    private readonly long _id = Interlocked.Increment(ref Cores);
    private long _sequence;

    public event Action<Outcome>? Noticed;

    public Func<ConfigRead>? ReadConfig { get; set; }

    /// <summary>
    /// Whether saving the config reloads it. <c>misc:disable_autoreload</c> turns it off, and turning the option off
    /// again, with <c>keyword</c> or a reload by hand, turns it back on.
    /// </summary>
    public bool ReloadsOnSave => !_config.Session.DisableAutoreload;

    /// <summary><c>general:hide_taskbar</c>: the taskbars are hidden and set to auto-hide while Winland runs.</summary>
    public bool HideTaskbar => _config.Session.HideTaskbar;

    public ICursorPosition? Cursor { get; set; }

    public ISessionLock? SessionLock { get; set; }

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
            return Step(input, () => HandleInput(input));
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
            if (launch.Rules.Count > 0)
                _follow.Track(processId, launch.Rules, Clock());

            // A started program has ten seconds to open its window.
            if (launch.InitialWorkspace is WorkspaceId workspace)
                _initialWorkspaces.Track(processId, workspace, Clock() + 10_000);
        }
    }

    internal void NoteProcess(int processId, int parentProcessId)
    {
        lock (_gate)
        {
            _follow.Note(processId, parentProcessId);
            _initialWorkspaces.Note(processId, parentProcessId);
        }
    }

    public CommandReply Execute(string? line)
    {
        lock (_gate)
        {
            var reply = default(CommandReply);
            var outcome = Step(null, () => (reply = ExecuteLine(line)).Effect);
            return reply with { Effect = outcome };
        }
    }

    // Every input and every command runs as one step: the change, then the rules matched again, the fullscreen
    // hiding, the decorations and the Motions, and only then the events and the cursor, so they describe the
    // finished State. The scratch state a step gathers is dropped when it ends.
    private Outcome Step(Input? input, Func<Outcome> run)
    {
        var before = Capture();
        var wasClient = input is WindowAppeared appeared && Find(appeared.Window) is not null;
        try
        {
            var outcome = run();
            CheckShownWorkspaces();
            if (input is not null)
                Note(input, wasClient);

            outcome = Ordered(WithEvents(Decorate(outcome, before.State), before, input));
            Emit(outcome);
            return outcome;
        }
        finally
        {
            _configEvents = null;
            _extraEvents = null;
            _noted = null;
            _activated = null;
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
            WindowClosed closed => Close(closed.Window),
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

    // Called under the lock, so the sequence is the order in which the outcomes were decided.
    private Outcome Ordered(Outcome outcome) => outcome with { Order = new OutcomeOrder(_id, ++_sequence) };

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
                _ = exception;
            }
        }
    }
}
