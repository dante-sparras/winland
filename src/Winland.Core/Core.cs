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
    private IReadOnlyList<EnvironmentAssignment> _environment = EnvironmentAssignment.None;
    private readonly PidFollowSet _follow = new();
    private readonly BindSession _keyState = new();
    private volatile BindTable _binds = BindTable.Empty;

    // Pipe requests arrive on another thread than binds and window events.
    private readonly Lock _gate = new();
    private ConfigDocument? _config;
    private LayoutOptions _options = LayoutOptions.Unconfigured;
    private ScrollingOptions _scrolling = ScrollingOptions.Defaults;
    private MasterOptions _master = MasterOptions.Documented;
    private DecorationOptions _decoration = DecorationOptions.Documented;
    private Rectangle _area;
    private WindowId? _active;
    private List<StreamEvent>? _configEvents;
    private List<StreamEvent>? _noted;
    private readonly HashSet<WindowId> _concealedMinimized = [];

    public event Action<Outcome>? Noticed;

    public Func<ConfigRead>? ReadConfig { get; set; }

    public ICursorPosition? Cursor { get; set; }

    public ISessionLock? SessionLock { get; set; }

    public Action<ConfigDocument>? AfterReload { get; set; }

    public Core(ConfigDocument? config = null)
    {
        TrackWindowState();
        _rules = config?.WindowRules ?? [];
        _workspaceRules = config?.WorkspaceRules ?? WorkspaceRule.None;
        _environment = config?.Environment ?? EnvironmentAssignment.None;
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
        return BindMatcher.Decide(_binds, session, input);
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
                var outcome = HandleCore(input);
                Emit(outcome);
                return outcome;
            }
            finally
            {
                _configEvents = null;
                _extraEvents = null;
                _noted = null;
            }
        }
    }

    public Outcome Dispatch(string name, string arguments, bool allowsElevation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);
        lock (_gate)
        {
            var before = Capture();
            var outcome = Decorate(WithEvents(Execute(name, arguments, allowsElevation), before, input: null), before.State);
            Emit(outcome);
            return outcome;
        }
    }

    public IReadOnlyList<Launch> StartupLaunches(ConfigDocument config) =>
        ConfigLaunches(config, LaunchParser.LaunchMoment.Startup);

    public IReadOnlyList<Launch> ReloadLaunches(ConfigDocument config) =>
        ConfigLaunches(config, LaunchParser.LaunchMoment.Reload);

    public IReadOnlyList<Launch> ShutdownLaunches(ConfigDocument config) =>
        ConfigLaunches(config, LaunchParser.LaunchMoment.Shutdown);

    internal Func<long>? FollowClock { get; set; }

    public void NoteLaunched(int processId, Launch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (processId <= 0 || launch.Rules.Count == 0)
            return;

        lock (_gate)
            _follow.Track(processId, launch.Rules, FollowNow());
    }

    internal void NoteProcess(int processId, int parentProcessId)
    {
        lock (_gate)
            _follow.Note(processId, parentProcessId);
    }

    public CommandReply Execute(string? line)
    {
        lock (_gate)
        {
            var before = Capture();
            try
            {
                var reply = ExecuteLine(line);
                var outcome = Decorate(WithEvents(reply.Effect, before, input: null), before.State);
                Emit(outcome);
                return reply with { Effect = outcome };
            }
            finally
            {
                _configEvents = null;
                _extraEvents = null;
                _noted = null;
            }
        }
    }

    private Outcome HandleCore(Input input)
    {
        var before = Capture();
        var outcome = input switch
        {
            WindowAppeared appeared => Appear(appeared),
            ConfigLoaded loaded => Load(loaded),
            InputInhibited inhibited => Inhibit(inhibited),
            KeyPressed or KeyReleased => RunKey(input),
            PointerButtonPressed pressed => PressPointer(pressed),
            PointerButtonReleased released => ReleasePointer(released),
            PointerMoved moved => MovePointer(moved),
            WheelTurned wheel => RunKey(NaturalScroll.Apply(_input, wheel)),
            LongPressElapsed => RunKey(input),
            PointerGesture gesture => ApplyGesture(gesture),
            WindowMaximizeRequested requested => MaximizeRequest(requested.Window),
            WindowDrifted drifted => PutBack(drifted),
            WindowSizeStarted sizing => BeginNativeSize(sizing),
            WindowSizeEnded sized => EndNativeSize(sized),
            WindowMoveStarted started => BeginNativeMove(started),
            WindowMoveEnded ended => EndNativeMove(ended),
            SwipeBegan => BeginSwipe(),
            SwipeUpdated updated => UpdateSwipe(updated),
            SwipeEnded => EndSwipe(),
            Dispatch dispatched => ExecuteTriggered(dispatched.Name, dispatched.Arguments, dispatched.Trigger),
            WindowFlashed flashed => Flash(flashed.Window),
            WindowFocused focused => Consider(focused),
            WindowClosed closed => Close(closed.Window),
            WindowLimitsChanged limits => ChangeLimits(limits),
            WindowMinimized minimized => Minimize(minimized.Window),
            WindowRestored restored => Restore(restored),
            WindowResized resized => Reapply(resized),
            Configure configure => Apply(configure.Document),
            Preselect preselect => View(CurrentWorkspace().Layout.Preselect(preselect.Direction)),
            Pseudo pseudo => ChangePseudo(pseudo.Window),
            MonitorsChanged changed => SetMonitors(changed.Monitors),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "No handler for this input."),
        };

        Note(input);
        return WithEvents(Decorate(outcome, before.State), before, input);
    }

    private Outcome Inhibit(InputInhibited inhibited)
    {
        _keyState.Inhibit(inhibited.Inhibited);
        return View([]);
    }

    private static IReadOnlyList<Launch> ConfigLaunches(ConfigDocument config, LaunchParser.LaunchMoment moment)
    {
        ArgumentNullException.ThrowIfNull(config);
        return LaunchParser.FromConfig(config, moment);
    }

    private long FollowNow() => FollowClock?.Invoke() ?? Environment.TickCount64;

    private Launch? WithEnvironment(Launch? launch)
    {
        if (launch is null || _environment.Count == 0)
            return launch;

        return launch with { Environment = _environment };
    }

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
