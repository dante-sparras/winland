namespace Winland;

public sealed partial class Core
{
    private readonly List<LiveWorkspace> _workspaces = [];
    private readonly List<LiveMonitor> _monitors = [];
    private readonly Dictionary<MonitorId, WorkspaceId> _activeOn = [];
    private readonly List<WorkspaceVisit> _history = [];
    private IReadOnlyList<WindowRule> _rules;
    private IReadOnlyList<WorkspaceRule> _workspaceRules = WorkspaceRule.None;
    private readonly Queue<Launch> _born = new();
    private readonly PidFollowSet _follow = new();
    private readonly InitialWorkspaces _initialWorkspaces = new();
    private readonly BindSession _keyState = new();
    private volatile BindTable _binds = BindTable.Empty;

    // Pipe requests arrive on another thread than binds and window events.
    private readonly Lock _gate = new();
    private ConfigDocument? _config;
    private LayoutOptions _options = LayoutOptions.Unconfigured;

    // One lookup for every Workspace's options, so they still compare equal.
    private readonly Func<ClientId, int?> _clientBorder;
    private ScrollingOptions _scrolling = ScrollingOptions.Defaults;
    private MasterOptions _master = MasterOptions.Documented;
    private DecorationOptions _decoration = DecorationOptions.Documented;
    private Rectangle _area;
    private WindowId? _active;
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
    public bool ReloadsOnSave => !_session.DisableAutoreload;

    /// <summary><c>general:hide_taskbar</c>: the taskbars are hidden and set to auto-hide while Winland runs.</summary>
    public bool HideTaskbar => _session.HideTaskbar;

    public ICursorPosition? Cursor { get; set; }

    public ISessionLock? SessionLock { get; set; }

    /// <summary>The keyboards and pointers <c>devices</c> reports. Without it there is one of each, named <c>keyboard</c> and <c>mouse</c>.</summary>
    public IInputDevices? Devices { get; set; }

    public Action<ConfigDocument>? AfterReload { get; set; }

    public Core(ConfigDocument? config = null)
    {
        _clientBorder = ClientBorderOf;
        TrackWindowState();
        _rules = config?.WindowRules ?? [];
        _workspaceRules = config?.WorkspaceRules ?? WorkspaceRule.None;
        _config = config;
        NoteConfigErrors(config);
        if (config is not null)
        {
            _decoration = DecorationOptions.From(config);
            _session = SessionOptions.From(config);
            _scrolling = ScrollingOptions.From(config);
            _master = MasterOptions.From(config);
            ReadAnimations(config);
            _groupOptions = GroupOptions.From(config);
            ReadCursor(config);
            ReadGestures(config);
            ReadBinds(config);
        }
        _activeOn[MonitorId.Primary] = WorkspaceId.Initial;
        WorkspaceFor(WorkspaceId.Initial);
        EnsurePersistent();
        Remember(WorkspaceId.Initial, MonitorId.Primary);
    }

    public BindTable Binds => _binds;

    /// <summary>Bind state the hook matches against. The active submap is read here with no lock.</summary>
    public BindSession KeyState => _keyState;

    /// <summary>Matches input against the published bind table. Does not take the core lock.</summary>
    public BindDecision Decide(Input input, BindSession session)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(session);
        if (input is WheelTurned wheel)
            input = NaturalScroll.Apply(_input, wheel);
        var decision = BindMatcher.Decide(_binds, session, input);

        // binds:pass_mouse_when_bound: a button that runs a bind is not swallowed, so the window
        // under the pointer gets its press and release. A wheel notch is not a button and stays swallowed.
        if (_passMouseWhenBound && decision.Consumed && input is PointerButtonPressed or PointerButtonReleased)
            decision = decision with { Consumed = false, PassesOnFailure = false, ReleaseOnFailure = [] };

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
            try
            {
                var outcome = Ordered(HandleCore(input));
                Emit(outcome);
                return outcome;
            }
            finally
            {
                _configEvents = null;
                _extraEvents = null;
                _noted = null;
                _modalRules = null;
                _activated = null;
            }
        }
    }

    public Outcome Dispatch(string name, string arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);
        lock (_gate)
        {
            var before = Capture();
            try
            {
                var outcome = Ordered(Decorate(WithEvents(Execute(name, arguments), before, input: null), before.State));
                Emit(outcome);
                return outcome;
            }
            finally
            {
                _configEvents = null;
                _extraEvents = null;
                _noted = null;
                _modalRules = null;
                _activated = null;
            }
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

    public IReadOnlyList<Launch> ShutdownLaunches(ConfigDocument config) =>
        ConfigLaunches(config, LaunchParser.LaunchMoment.Shutdown);

    internal Func<long>? FollowClock { get; set; }

    public void NoteLaunched(int processId, Launch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (processId <= 0)
            return;

        lock (_gate)
        {
            if (launch.Rules.Count > 0)
                _follow.Track(processId, launch.Rules, FollowNow());

            // misc:initial_workspace_tracking 2 keeps the token for good, as Winland gives it a very long life.
            var tracking = _session.InitialWorkspaceTracking;
            if (tracking > 0 && launch.InitialWorkspace is WorkspaceId workspace)
            {
                var expires = tracking == 2 ? long.MaxValue : FollowNow() + (_session.InitialWorkspaceTokenTimeout * 1000L);
                _initialWorkspaces.Track(processId, workspace, expires);
            }
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
            var before = Capture();
            try
            {
                var reply = ExecuteLine(line);
                var outcome = Ordered(Decorate(WithEvents(reply.Effect, before, input: null), before.State));
                Emit(outcome);
                return reply with { Effect = outcome };
            }
            finally
            {
                _configEvents = null;
                _extraEvents = null;
                _noted = null;
                _modalRules = null;
                _activated = null;
            }
        }
    }

    private Outcome HandleCore(Input input)
    {
        var before = Capture();
        var outcome = input switch
        {
            WindowAppeared appeared => AppearMaybeMaximized(appeared),
            ConfigLoaded loaded => Load(loaded),
            InputInhibited inhibited => Inhibit(inhibited),
            KeyPressed or KeyReleased => RunKey(input),
            PointerButtonPressed pressed => PressPointer(pressed),
            PointerButtonReleased released => ReleasePointer(released),
            PointerMoved moved => MovePointer(moved),
            WheelTurned wheel => RunKey(NaturalScroll.Apply(_input, wheel)),
            LongPressElapsed => RunKey(input),
            PointerGesture gesture => ApplyGesture(gesture),
            GroupBarScrolled scrolled => ScrollGroupBar(scrolled),
            WindowMaximizeRequested requested => MaximizeRequest(requested.Window),
            WindowDrifted drifted => PutBack(drifted),
            WindowSizeStarted sizing => BeginNativeSize(sizing),
            WindowSizeEnded sized => EndNativeSize(sized),
            WindowSizing sizing => ContinueNativeSize(sizing),
            WindowMoveStarted started => BeginNativeMove(started),
            WindowMoveEnded ended => EndNativeMove(ended),
            ClickKillChosen chosen => ChooseKill(chosen),
            LayoutCycled => CycleLayout(),
            SwipeBegan => BeginSwipe(),
            SwipeUpdated updated => UpdateSwipe(updated),
            SwipeEnded ended => EndSwipe(ended.Cancelled),
            TouchDown touched => TouchDown(touched),
            TouchMoved moved => TouchMove(moved),
            TouchUp lifted => TouchUp(lifted),
            PinchBegan => BeginPinch(),
            PinchUpdated pinched => UpdatePinch(pinched),
            PinchEnded ended => EndPinch(ended.Cancelled),
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
            Preselect preselect => View(CurrentWorkspace().Layout.Preselect(preselect.Direction)),
            Pseudo pseudo => ChangePseudo(pseudo.Window),
            MonitorsChanged changed => SetMonitors(changed.Monitors),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "No handler for this input."),
        };

        outcome = WakeDisplays(input, outcome);
        Note(input);
        return WithEvents(Decorate(outcome, before.State), before, input);
    }

    private Outcome Inhibit(InputInhibited inhibited)
    {
        _keyState.Inhibit(inhibited.Inhibited);
        return View([]);
    }

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
        return SpecialOn(monitor) ?? ActiveOn(monitor);
    }

    private long FollowNow() => FollowClock?.Invoke() ?? Environment.TickCount64;

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
