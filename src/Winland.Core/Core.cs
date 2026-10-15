namespace Winland;

public sealed partial class Core
{
    private readonly List<Desk> _desks = [];
    private readonly List<Screen> _screens = [];
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
            ReadCursor(config);
            ReadBinds(config);
        }
        _activeOn[MonitorId.Primary] = WorkspaceId.Initial;
        DeskFor(WorkspaceId.Initial);
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
            Dispatch dispatched => Execute(dispatched.Name, dispatched.Arguments, allowsElevation: true),
            WindowFocused focused => Consider(focused),
            WindowClosed closed => Close(closed.Window),
            WindowLimitsChanged limits => ChangeLimits(limits),
            WindowMinimized minimized => Minimize(minimized.Window),
            WindowRestored restored => Restore(restored),
            Configure configure => Apply(configure.Document),
            Preselect preselect => View(CurrentDesk().Layout.Preselect(preselect.Direction)),
            Pseudo pseudo => ChangePseudo(pseudo.Window),
            MonitorsChanged changed => SetMonitors(changed.Monitors),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "No handler for this input."),
        };

        Note(input);
        return WithEvents(Decorate(outcome, before.State), before, input);
    }

    private Outcome Load(ConfigLoaded loaded)
    {
        InstallBinds(loaded.Config);
        _config = loaded.Config;
        _workspaceRules = loaded.Config.WorkspaceRules;
        NoteConfigErrors(loaded.Config);
        _environment = loaded.Config.Environment;
        _decoration = DecorationOptions.From(loaded.Config);
        _session = SessionOptions.From(loaded.Config);
        ReadCursor(loaded.Config);
        ReadBinds(loaded.Config);
        if (_screens.Count == 0)
            return View([]);

        return SetMonitors(Reported());
    }

    private Outcome Inhibit(InputInhibited inhibited)
    {
        _keyState.Inhibit(inhibited.Inhibited);
        return View([]);
    }

    private void InstallBinds(ConfigDocument document) =>
        _binds = BindTable.From(document);

    private Outcome RunBind(BindDecision match)
    {
        var dispatches = DispatchesOf(match);
        Outcome? outcome = null;
        foreach (var dispatch in dispatches)
        {
            if (dispatch.Dispatcher.Length == 0)
                continue;

            var submapBefore = _keyState.Submap;
            var next = Execute(dispatch.Dispatcher, dispatch.Arguments, allowsElevation: true);
            outcome = outcome is null ? next : Merge(outcome, next);
            if (!Is(dispatch.Dispatcher, "submap")
                && dispatch.SubmapReset is string reset
                && reset.Length > 0
                && _keyState.Submap == submapBefore)
                TryEnterSubmap(reset, out _);
        }

        if (outcome is null)
            return View([], consumed: match.Consumed);

        return outcome with { Consumed = match.Consumed, Dispatcher = match.Dispatcher ?? outcome.Dispatcher };
    }

    private static IReadOnlyList<BindDispatch> DispatchesOf(BindDecision match)
    {
        if (match.Dispatches.Count > 0)
            return match.Dispatches;
        if (string.IsNullOrEmpty(match.Dispatcher))
            return [];

        return [new BindDispatch(match.Dispatcher, match.Arguments)];
    }

    private static Outcome Merge(Outcome first, Outcome second) =>
        second with
        {
            Placements = second.Placements.Count > 0 ? second.Placements : first.Placements,
            Close = second.Close ?? first.Close,
            Focus = second.Focus ?? first.Focus,
            Launch = second.Launch ?? first.Launch,
            Kill = second.Kill ?? first.Kill,
            Exit = second.Exit || first.Exit,
            Notice = second.Notice ?? first.Notice,
            DisplayPower = second.DisplayPower.Count > 0 ? second.DisplayPower : first.DisplayPower,
            Show = [.. first.Show, .. second.Show],
            Hide = [.. first.Hide, .. second.Hide],
            ShowMinimized = [.. first.ShowMinimized, .. second.ShowMinimized],
        };

    private CommandReply ExecuteLine(string? line) =>
        CommandProtocol.Parse(line) switch
        {
            CommandRejected rejected => new CommandReply(rejected.Error, View([])),
            CommandParsed parsed => Reply(parsed.Request),
            _ => throw new InvalidOperationException("No handler for this command."),
        };

    private CommandReply Reply(CommandRequest request)
    {
        var reply = request switch
        {
            DispatchRequest dispatch => DispatchReply(dispatch),
            ClientsRequest clients => Describe(state => CommandProtocol.FormatClients(state, clients.All)),
            WorkspacesRequest => Describe(CommandProtocol.FormatWorkspaces),
            WorkspaceRulesRequest => new CommandReply(WorkspaceRules.Format(_workspaceRules), View([])),
            MonitorsRequest monitors => Describe(state => CommandProtocol.FormatMonitors(state, monitors.All)),
            ActiveWindowRequest => Describe(state => CommandProtocol.FormatActiveWindow(state, ActiveWindow())),
            ActiveWorkspaceRequest => Describe(CommandProtocol.FormatActiveWorkspace),
            EventsRequest => new CommandReply("", View([])) { Stream = true },
            ReloadRequest => ReloadCommand(),
            PluginRequest plugin => PluginCommand(plugin),
            _ => ReplyExtra(request),
        };

        if (request.Flags.HasFlag(CommandFlags.Relayout))
            reply = reply with { Effect = Reflow(reply.Effect) };

        return reply;
    }

    private CommandReply DispatchReply(DispatchRequest dispatch)
    {
        var effect = Execute(dispatch.Name, dispatch.Arguments, dispatch.AllowsElevation);
        return new CommandReply(effect.Error ?? effect.Notice ?? "ok", effect);
    }

    private CommandReply Describe(Func<State, string> format)
    {
        var outcome = View([]);
        return new CommandReply(format(outcome.State), outcome);
    }

    private Outcome Execute(string name, string arguments, bool allowsElevation)
    {
        ArgumentNullException.ThrowIfNull(name);
        arguments ??= "";

        if (Is(name, "togglespecialworkspace"))
            return ToggleSpecial(arguments, allowsElevation);

        if (Is(name, "workspace") || Is(name, "focusworkspaceoncurrentmonitor"))
        {
            if (!WorkspaceAddress.TryResolve(arguments, Context(), out var target))
                return View([], dispatcher: name, allowsElevation: allowsElevation);

            if (target.IsSpecial)
                return OpenSpecial(target, null, name, allowsElevation);

            return SwitchTo(
                Alias(target),
                focus: null,
                consumed: false,
                dispatcher: name,
                allowsElevation: allowsElevation,
                pull: Is(name, "focusworkspaceoncurrentmonitor"));
        }

        if (Is(name, "movetoworkspace"))
            return MoveWindowToWorkspace(arguments, silent: false, allowsElevation: allowsElevation);

        if (Is(name, "movetoworkspacesilent"))
            return MoveWindowToWorkspace(arguments, silent: true, allowsElevation: allowsElevation);

        if (Is(name, "movefocus"))
            return MoveFocusDirection(arguments, allowsElevation);

        if (Is(name, "movewindow"))
            return MoveWindowDirection(arguments, allowsElevation);

        if (Is(name, "focusmonitor"))
            return FocusMonitorDispatch(arguments, allowsElevation);

        if (Is(name, "movecurrentworkspacetomonitor"))
            return MoveCurrentWorkspace(arguments, allowsElevation);

        if (Is(name, "moveworkspacetomonitor"))
            return MoveWorkspace(arguments, allowsElevation);

        if (Is(name, "swapactiveworkspaces"))
            return SwapActiveWorkspaces(arguments, allowsElevation);

        var active = ActiveWindow();
        var desk = CurrentDesk();
        var layout = desk.Layout;
        var covered = Covered(desk);
        IReadOnlyList<Placement> placements = [];
        WindowId? close = null;
        WindowId? focus = null;

        if (Is(name, "killactive"))
            close = active;
        else if (Is(name, "resizeactive"))
            placements = active is WindowId window ? layout.ResizeActive(window, arguments) : [];
        else if (Is(name, "togglefloating"))
            return FloatWindow(arguments, floating: null, allowsElevation);
        else if (Is(name, "setfloating"))
            return FloatWindow(arguments, floating: true, allowsElevation);
        else if (Is(name, "settiled"))
            return FloatWindow(arguments, floating: false, allowsElevation);
        else if (Is(name, "pin"))
            return PinWindow(arguments, allowsElevation);
        else if (Is(name, "pseudo"))
            return PseudoWindow(arguments, allowsElevation);
        else if (Is(name, "focuswindow"))
            return FocusWindow(arguments, allowsElevation);
        else if (Is(name, "closewindow"))
            return CloseWindow(arguments, allowsElevation);
        else if (Is(name, "killwindow"))
            return KillWindow(arguments, allowsElevation);
        else if (Is(name, "swapwindow"))
            return SwapWindow(arguments, allowsElevation);
        else if (Is(name, "movetoroot"))
            return MoveRoot(arguments, allowsElevation);
        else if (Is(name, "exit"))
            return ExitDaemon(allowsElevation);
        else if (Is(name, "event"))
            return EmitCustom(arguments, allowsElevation);
        else if (Is(name, "dpms"))
            return Dpms(arguments, allowsElevation);
        else if (Is(name, "fullscreen") || Is(name, "fullscreenstate") || Is(name, "maximize"))
            return FullscreenDispatcher(name, arguments, allowsElevation);
        else if (Is(name, "layoutmsg") || Is(name, "layout"))
            placements = layout.LayoutMessage(active, arguments);
        else if (IsDwindleMessage(name))
            placements = layout.LayoutMessage(active, Join(name, arguments));
        else if (Is(name, "exec") || Is(name, "execr"))
        {
            var parsed = LaunchParser.Parse(arguments, allowsElevation, raw: Is(name, "execr"));
            if (parsed.Warnings.Count > 0)
            {
                var warnings = new List<StreamEvent>(parsed.Warnings.Count);
                foreach (var warning in parsed.Warnings)
                    warnings.Add(new ConfigWarned(0, warning, null));
                _configEvents = warnings;
            }

            return View([], dispatcher: name, allowsElevation: allowsElevation) with
            {
                Launch = WithEnvironment(parsed.Launch),
            };
        }
        else if (Is(name, "submap"))
        {
            if (!TryEnterSubmap(arguments, out var error))
                return View([], dispatcher: name, allowsElevation: allowsElevation) with { Error = error };

            return View([], dispatcher: name, allowsElevation: allowsElevation);
        }
        else if (Dispatchers.TryInvoke(name, arguments, out var pluginResult))
            return View([], dispatcher: name, allowsElevation: allowsElevation) with
            {
                Error = pluginResult.Error,
                Notice = pluginResult.Notice,
            };
        else if (!Dispatchers.IsKnown(name))
            return View([], dispatcher: name, allowsElevation: allowsElevation) with
            {
                Error = "error: unknown dispatcher '" + name + "'",
            };
        else
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        if (layout.LayoutMessageError is string layoutError
            && (Is(name, "layoutmsg") || Is(name, "layout") || IsDwindleMessage(name)))
            return View(placements, dispatcher: name, allowsElevation: allowsElevation) with { Error = layoutError };

        if (focus is null && layout.LayoutMessageFocus is WindowId requested)
            focus = requested;

        if (focus is WindowId next && HasNoFocus(next))
            focus = null;
        else if (focus is WindowId accepted)
        {
            _active = accepted;
            desk.Focus = accepted;
            desk.Layout.NoteFocus(accepted);
        }

        var message = Is(name, "layoutmsg") || Is(name, "layout") || IsDwindleMessage(name);
        return Present(
            desk,
            covered,
            placements,
            dispatcher: name,
            close: close,
            focus: focus,
            allowsElevation: allowsElevation,
            focusRevealed: message && focus is null);
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

    private static IReadOnlyList<WindowRule> JoinRules(IReadOnlyList<WindowRule> rules, IReadOnlyList<WindowRule> followed)
    {
        if (followed.Count == 0)
            return rules;
        if (rules.Count == 0)
            return followed;

        var combined = new WindowRule[rules.Count + followed.Count];
        for (var i = 0; i < rules.Count; i++)
            combined[i] = rules[i];
        for (var i = 0; i < followed.Count; i++)
            combined[rules.Count + i] = followed[i];
        return combined;
    }

    private static string Join(string name, string arguments) =>
        arguments.Length == 0 ? name : string.Concat(name, " ", arguments);

    private static bool IsDwindleMessage(string name) =>
        Is(name, "togglesplit")
        || Is(name, "swapsplit")
        || Is(name, "rotatesplit")
        || Is(name, "splitratio");

    private static bool Is(string name, string expected) =>
        name.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private WindowId? ActiveWindow()
    {
        if (_active is not WindowId active)
            return null;

        if (ClientOn(ActiveId(), active) is not null)
            return active;

        var desk = DeskContaining(active);
        if (desk is null || !IsShown(desk.Id))
            return null;

        return ClientOn(desk, active) is { Minimized: false } ? active : null;
    }

    private void Note(Input input)
    {
        _closing = null;
        switch (input)
        {
            case WindowAppeared appeared when DeskContaining(appeared.Window) is { } appearedDesk
                && IsShown(appearedDesk.Id)
                && !_awaitingMapFocus.Contains(appeared.Window):
                var current = SpecialOn(appearedDesk.Monitor) ?? ActiveOn(appearedDesk.Monitor);
                if (appearedDesk.Id == current)
                {
                    appearedDesk.Focus = appeared.Window;
                    if (_screens.Count == 0 || appearedDesk.Monitor == FocusedId())
                        _active = appeared.Window;
                    TouchHistory(appeared.Window);
                }

                break;
        }
    }

    private void NoteFocus(WindowId window)
    {
        var owner = DeskContaining(window);
        if (owner is not null && IsShown(owner.Id))
        {
            if (_screens.Count > 0)
                Focus(owner.Monitor);

            var special = SpecialOn(owner.Monitor);
            var onSpecial = special == owner.Id;
            var onNormal = owner.Id == ActiveOn(owner.Monitor);
            if (onSpecial || (onNormal && (special is null || _input.SpecialFallthrough)))
            {
                _active = window;
                owner.Focus = window;
                TouchHistory(window);
            }

            return;
        }

        if (Find(window) is null)
            _active = null;
    }

    private Outcome Appear(WindowAppeared appeared)
    {
        var processId = appeared.ProcessId is > 0 and <= int.MaxValue ? (int)appeared.ProcessId : 0;
        if (processId > 0)
            _follow.NoteLineage(processId, appeared.AncestorProcessIds);
        var followed = processId > 0 ? _follow.Take(processId, FollowNow()) : [];
        var decision = WindowRuleParser.Decide(appeared.Traits, JoinRules(_rules, followed));
        if (!decision.Adopt)
            return View([]);

        if (Find(appeared.Window) is { } existing)
            return IsShown(existing.Workspace) ? View([]) : View([], hide: [appeared.Window]);

        var restored = FindRestored(appeared.Window);
        var screen = ScreenFor(appeared);
        if (_screens.Count > 0 && screen is null && !RestoredScreenExists(restored))
            return View([]);

        var monitor = RestoredMonitor(restored, screen);
        var active = screen is null ? ActiveOn(monitor) : screen.Active ?? ActiveOn(monitor);
        var current = SpecialOn(monitor) ?? active;
        var rule = WorkspaceAddress.ParseRule(decision.Workspace, Context(monitor));
        if (rule.Id is WorkspaceId addressed)
            rule = rule with { Id = Alias(addressed) };
        var target = restored?.Workspace ?? rule.Id ?? current;
        ForgetRestored(appeared.Window);
        var desk = DeskFor(target, monitor);
        var area = WorkArea(desk, appeared.WorkArea);
        _area = area;
        var covered = Covered(desk);
        if (decision.ScrollingWidth is double scrollingWidth)
            desk.Layout.NoteScrollingWidth(appeared.Window, scrollingWidth);
        var placements = desk.Layout.Add(
            appeared.Window,
            area,
            appeared.Pointer,
            decision.Floating,
            appeared.Bounds,
            target,
            appeared.Traits.Class,
            appeared.Traits.Title,
            IsShown(target),
            FocusedMonitor(),
            appeared.ProcessId,
            minSize: decision.MinSize,
            maxSize: decision.MaxSize);
        if (decision.FullscreenRequest is { } fullscreen)
            placements = desk.Layout.SetFullscreen(appeared.Window, fullscreen.Internal, fullscreen.Client);

        RememberFocus(appeared.Window, decision);
        RememberOpacity(appeared.Window, decision);
        if (decision.Pin && decision.Floating)
            _pinned.Add(appeared.Window);
        var restore = _awaitingMapFocus.Contains(appeared.Window) ? Kept() : null;
        if (restore == appeared.Window)
            restore = null;

        if (restored is null && rule.Id is not null && !rule.Silent && !IsShown(target) && !_awaitingMapFocus.Contains(appeared.Window))
        {
            if (ScreenById(desk.Monitor) is { } owner)
                Focus(owner.Id);

            if (target.IsSpecial)
                return OpenSpecial(target, appeared.Window, null, allowsElevation: false);

            return SwitchTo(target, appeared.Window, consumed: false, dispatcher: null);
        }

        if (!IsShown(target))
        {
            if (_pinned.Contains(appeared.Window) && MonitorShowing(desk.Monitor))
                return Offering(View(placements, show: [appeared.Window]), restore);

            return Offering(View([], hide: [appeared.Window]), restore);
        }

        return Offering(Present(desk, covered, placements), restore);
    }

    private Outcome ChangeLimits(WindowLimitsChanged changed)
    {
        var desk = DeskContaining(changed.Window);
        if (desk is null)
            return View([]);

        var placements = desk.Layout.UpdateLimits(changed.Window, changed.MinSize, changed.MaxSize);
        return IsShown(desk.Id) ? View(placements) : View([]);
    }

    private Outcome Close(WindowId window)
    {
        ForgetFocus(window);
        ForgetOpacity(window);
        ForgetRestored(window);
        _pinned.Remove(window);
        var desk = DeskContaining(window);
        if (desk is null)
            return View([]);

        _closing = desk;
        var covered = Covered(desk);
        var wasActive = _active == window;
        var monitor = desk.Monitor;
        var special = desk.Id.IsSpecial && SpecialOn(monitor) == desk.Id;
        var floating = ClientOn(desk, window)?.Floating == true;
        var hadBounds = desk.Layout.TryBounds(window, out var bounds);
        var placements = desk.Layout.Remove(window);
        if (!IsShown(desk.Id))
            placements = [];

        if (special && desk.Layout.Clients.Count == 0)
        {
            if (_session.CloseSpecialOnEmpty)
            {
                SetSpecial(monitor, null);
                DropIfEmpty(desk.Id);
                var normal = FindDesk(ActiveOn(monitor));
                var specialFocus = normal is null ? null : Remembered(normal);
                _active = specialFocus;
                if (normal is not null)
                    normal.Focus = specialFocus;
                _closing = null;
                return View(normal?.Layout.Visible() ?? [], focus: specialFocus);
            }

            _active = _input.SpecialFallthrough ? Remembered(FindDesk(ActiveOn(monitor)) ?? ActiveDesk()) : null;
            desk.Focus = null;
            _closing = null;
            return View([], focus: _active);
        }

        DropIfEmpty(desk.Id);
        if (!IsShown(desk.Id))
        {
            if (wasActive)
                _active = null;

            return View(placements);
        }

        WindowId? next = null;
        var reveal = false;
        if (wasActive)
        {
            next = FocusAfter(desk, floating, hadBounds ? bounds : default, hadBounds);
            if (next is WindowId chosen)
                TakeFocus(desk, chosen);
            else if (_input.FocusOnClose == 0)
                reveal = true;
            else
                TakeFocus(desk, null);
        }

        var outcome = Present(desk, covered, placements, focus: next, focusRevealed: reveal);
        if (wasActive && reveal && _active == window)
        {
            var fallback = LastVisible(desk);
            TakeFocus(desk, fallback);
            outcome = outcome with { Focus = fallback };
        }

        return outcome;
    }

    private Outcome Minimize(WindowId window)
    {
        var desk = DeskContaining(window);
        if (desk is null)
            return View([]);

        var covered = Covered(desk);
        var wasActive = _active == window;
        var placements = desk.Layout.Minimize(window);
        return IsShown(desk.Id)
            ? Present(desk, covered, placements, focusRevealed: wasActive)
            : View([]);
    }

    private Outcome Restore(WindowRestored restored)
    {
        var desk = DeskContaining(restored.Window);
        if (desk is null)
            return View([]);

        var covered = Covered(desk);
        var placements = desk.Layout.Restore(restored.Window);
        if (!IsShown(desk.Id))
            return View([], hide: [restored.Window]);

        return Present(desk, covered, placements);
    }

    private CommandReply ReloadCommand()
    {
        if (ReadConfig is null)
            return new CommandReply("error: config is not available", View([]));

        ConfigRead read;
        try
        {
            read = ReadConfig();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            read = new ConfigRead(null, new ConfigWarning(0, exception.Message, null));
        }

        if (read.Document is not ConfigDocument document)
        {
            var warning = read.Failure ?? new ConfigWarning(0, "Could not read config.", null);
            var message = warning.Message.StartsWith("error:", StringComparison.Ordinal)
                ? warning.Message
                : "error: " + warning.Message;
            _configEvents = [new ConfigWarned(warning.Line, warning.Message, warning.Source)];
            return new CommandReply(message, View([]));
        }

        var outcome = Apply(document);
        try
        {
            AfterReload?.Invoke(document);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _ = exception;
        }

        return new CommandReply("ok", outcome);
    }

    private Outcome Apply(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _config = document;
        _workspaceRules = document.WorkspaceRules;
        NoteConfigErrors(document);
        _rules = document.WindowRules;
        _session = SessionOptions.From(document);
        _environment = document.Environment;
        InstallBinds(document);
        _options = LayoutOptions.From(document);
        _scrolling = ScrollingOptions.From(document);
        _master = MasterOptions.From(document);
        _decoration = DecorationOptions.From(document);
        ReadCursor(document);
        ReadBinds(document);
        var active = ActiveDesk();
        var covered = Covered(active);
        foreach (var desk in _desks)
            MatchLayout(desk);
        Outcome outcome;
        if (_screens.Count == 0)
        {
            EnsurePersistent();
            IReadOnlyList<Placement> placements = [];
            foreach (var desk in _desks)
            {
                Stamp(desk);
                var placed = desk.Layout.Apply(desk.Options);
                if (desk.Id == ActiveId())
                    placements = placed;
            }

            DropEmpty();
            outcome = Present(active, covered, placements);
        }
        else
        {
            outcome = SetMonitors(Reported());
            DropEmpty();
        }

        var events = new List<StreamEvent>(document.Warnings.Count + 1);
        foreach (var warning in document.Warnings)
            events.Add(new ConfigWarned(warning.Line, warning.Message, warning.Source));

        events.Add(new ConfigReloaded());
        _configEvents = events;
        return outcome;
    }

    private void MatchLayout(Desk desk)
    {
        var created = LayoutFactory.Create(_config);
        if (created.GetType() == desk.Layout.GetType())
        {
            desk.Layout.Apply(_master);
            desk.Layout.Configure(_scrolling);
            return;
        }

        if (desk.Layout.Clients.Count == 0)
        {
            desk.Replace(created);
            created.Apply(_master);
            created.Configure(_scrolling);
            return;
        }

        var held = new List<(WindowId Window, Taken Taken)>();
        foreach (var client in desk.Layout.Clients.ToArray())
        {
            if (desk.Layout.TryTake(client.Window, out var taken))
                held.Add((client.Window, taken));
        }

        var focus = desk.Focus;
        desk.Replace(created);
        created.Apply(_master);
        created.Configure(_scrolling);
        Stamp(desk);
        var screen = ScreenById(desk.Monitor);
        var area = WorkArea(desk, _area);
        created.Apply(desk.Options);
        if (area.Width > 0 && area.Height > 0)
            created.Fit(area);

        var monitor = screen is { Effective.Enabled: true }
            ? new Rectangle(screen.Effective.X, screen.Effective.Y, screen.Effective.Width, screen.Effective.Height)
            : default;
        foreach (var (window, taken) in held)
        {
            NoteScrollingWidth(created, window, taken);
            created.Add(
                window,
                area,
                null,
                taken.Floating,
                taken.Bounds,
                desk.Id,
                taken.Class,
                taken.Title,
                monitor: monitor,
                processId: taken.ProcessId,
                initialClass: taken.InitialClass,
                initialTitle: taken.InitialTitle,
                minSize: taken.MinSize,
                maxSize: taken.MaxSize);
            if (taken.Minimized)
                created.Minimize(window);
        }

        if (focus is WindowId focused)
            created.NoteFocus(focused);
    }

    private Outcome ChangePseudo(WindowId window)
    {
        var desk = DeskContaining(window);
        if (desk is null)
            return View([]);

        var covered = Covered(desk);
        var placements = desk.Layout.TogglePseudo(window);
        return IsShown(desk.Id) ? Present(desk, covered, placements) : View([]);
    }

    private Outcome MoveTo(
        string argument,
        bool silent,
        bool consumed,
        string dispatcher,
        bool allowsElevation = false,
        WindowId? chosen = null,
        bool windowChosen = false)
    {
        var window = windowChosen ? chosen : ActiveWindow();
        if (window is not WindowId id || !WorkspaceAddress.TryResolve(argument, Context(), out var resolved))
            return View([], consumed: consumed, dispatcher: dispatcher, allowsElevation: allowsElevation);

        var target = Alias(resolved);

        var source = DeskContaining(id);
        if (source is null || source.Id == target)
            return View([], consumed: consumed, dispatcher: dispatcher, allowsElevation: allowsElevation);

        var sourceCovered = Covered(source);
        if (!source.Layout.TryTake(id, out var taken))
            return View([], consumed: consumed, dispatcher: dispatcher, allowsElevation: allowsElevation);

        var destination = DeskFor(target, FocusedId());
        var elsewhere = target.IsSpecial ? null : ShownElsewhere(target);
        if (!silent && !target.IsSpecial && elsewhere is null && ScreenById(FocusedId()) is { } focused)
            BringTo(focused, destination);

        var destinationCovered = Covered(destination);
        var area = WorkArea(destination, _area);
        NoteScrollingWidth(destination.Layout, id, taken);
        destination.Layout.Add(
            id,
            area,
            null,
            taken.Floating,
            taken.Bounds,
            target,
            taken.Class,
            taken.Title,
            processId: taken.ProcessId,
            initialClass: taken.InitialClass,
            initialTitle: taken.InitialTitle,
            minSize: taken.MinSize,
            maxSize: taken.MaxSize);
        if (taken.Minimized)
            destination.Layout.Minimize(id);

        if (target.IsSpecial)
            return FinishSpecialMove(source, destination, id, silent, consumed, dispatcher, allowsElevation, sourceCovered, destinationCovered);

        if (!silent && source.Id.IsSpecial && SpecialOn(source.Monitor) == source.Id)
        {
            var concealed = ConcealSpecial(source.Monitor);
            return FollowMove(SwitchTo(target, id, consumed, dispatcher, allowsElevation), id, concealed, destination);
        }

        if (!silent && elsewhere is { } owner)
        {
            var followed = FocusShown(owner, target, id, consumed, dispatcher, allowsElevation);
            var show = new List<WindowId>();
            var conceal = new List<WindowId>();
            if (IsShown(source.Id))
                Collect(source, sourceCovered, show, conceal);
            if (IsShown(destination.Id))
                Collect(destination, destinationCovered, show, conceal);
            var moved = new List<Placement>();
            if (IsShown(source.Id))
                moved.AddRange(source.Layout.Placed());
            if (IsShown(destination.Id))
                moved.AddRange(destination.Layout.Visible());
            return followed with
            {
                Placements = moved,
                Show = [.. followed.Show, .. show],
                Hide = [.. followed.Hide, .. conceal],
            };
        }

        if (!silent)
        {
            var followedSwitch = SwitchTo(target, id, consumed, dispatcher, allowsElevation);
            return followedSwitch.Focus is null && _active == id
                ? followedSwitch with { Focus = id }
                : followedSwitch;
        }

        var revealed = new List<WindowId>();
        var hidden = new List<WindowId>();
        if (IsShown(source.Id))
            Collect(source, sourceCovered, revealed, hidden);
        if (IsShown(destination.Id))
            Collect(destination, destinationCovered, revealed, hidden);
        else
            hidden.Add(id);

        if (source.Id.IsSpecial
            && source.Layout.Clients.Count == 0
            && _session.CloseSpecialOnEmpty
            && SpecialOn(source.Monitor) == source.Id)
        {
            foreach (var concealed in ConcealSpecial(source.Monitor))
            {
                if (!hidden.Contains(concealed))
                    hidden.Add(concealed);
            }
        }

        if (_active == id)
        {
            _active = revealed.Count == 1 && ClientOn(source, revealed[0]) is not null
                ? revealed[0]
                : LastVisible(source);
            source.Focus = _active;
        }

        var placements = new List<Placement>();
        if (IsShown(source.Id))
            placements.AddRange(source.Layout.Placed());
        if (IsShown(destination.Id))
            placements.AddRange(destination.Layout.Visible());

        return View(placements, revealed, hidden, consumed, dispatcher, allowsElevation: allowsElevation);
    }

    private Outcome FollowMove(Outcome followed, WindowId window, WindowId[] concealed, Desk destination)
    {
        var show = new List<WindowId>(followed.Show);
        if (IsShown(destination.Id) && !show.Contains(window))
            show.Add(window);

        var placements = followed.Placements.Count > 0 ? followed.Placements : destination.Layout.Visible();
        return followed with
        {
            Placements = placements,
            Show = show,
            Hide = Combine(followed.Hide, concealed),
            Focus = followed.Focus ?? window,
        };
    }

    private Outcome SwitchTo(WorkspaceId target, WindowId? focus, bool consumed, string? dispatcher, bool allowsElevation = false, bool pull = false)
    {
        if (target.IsSpecial)
            return OpenSpecial(target, focus, dispatcher, allowsElevation);

        var bound = BoundScreen(target);
        if (!pull && bound is null && ShownElsewhere(target) is { } elsewhere)
        {
            var away = SpecialHiddenBy(elsewhere.Id, focus);
            var followed = FocusShown(elsewhere, target, focus, consumed, dispatcher, allowsElevation);
            return away.Length == 0 ? followed : followed with { Hide = Combine(followed.Hide, away) };
        }

        if (bound is not null && bound.Id != FocusedId())
            Focus(bound.Id);

        var monitor = FocusedId();
        var concealed = SpecialHiddenBy(monitor, focus);
        if (bound is not null)
        {
            if (FindDesk(target) is { } held && held.Monitor != bound.Id)
            {
                held.Monitor = bound.Id;
                Refit(held);
            }
        }
        else if (ScreenById(monitor) is { } screen && FindDesk(target) is { } existing)
            BringTo(screen, existing);

        var sourceId = ActiveOn(monitor);
        if (sourceId == target)
        {
            if (focus is WindowId requested && ClientOn(target, requested) is not null
                && (SpecialOn(monitor) is null || _input.SpecialFallthrough))
            {
                _active = requested;
                ActiveDesk().Focus = requested;
            }

            return View([], hide: concealed, consumed: consumed, dispatcher: dispatcher, allowsElevation: allowsElevation);
        }

        var keep = _active is WindowId current
            && _pinned.Contains(current)
            && ClientOn(sourceId, current) is { Floating: true }
            ? current
            : (WindowId?)null;
        _activeOn[monitor] = target;
        if (ScreenById(monitor) is { } owner)
            owner.Active = target;

        Remember(target, monitor);
        var destination = DeskFor(target, monitor);
        CarryPinned(sourceId, destination);
        var hide = VisibleIds(sourceId);
        var keepSpecial = SpecialOn(monitor) is not null && focus is null;
        if (!keepSpecial)
        {
            _active = focus is WindowId requestedFocus && ClientOn(target, requestedFocus) is not null
                ? requestedFocus
                : keep is WindowId pinnedFocus && ClientOn(target, pinnedFocus) is not null
                    ? pinnedFocus
                    : Remembered(destination);
            destination.Focus = _active;
        }

        DropIfEmpty(sourceId);
        return View(
            destination.Layout.Visible(),
            VisibleIds(target),
            Combine(hide, concealed),
            consumed,
            dispatcher,
            allowsElevation: allowsElevation);
    }

    private Outcome SetMonitors(IReadOnlyList<Monitor> reported)
    {
        ArgumentNullException.ThrowIfNull(reported);
        var rules = MonitorRules.From(_config);
        var arrangement = MonitorRules.Arrange(reported, rules);
        var previous = _screens.ToArray();
        var next = new List<Screen>(reported.Count);
        for (var i = 0; i < reported.Count; i++)
        {
            var monitor = reported[i];
            ArgumentNullException.ThrowIfNull(monitor);
            var effective = arrangement.Monitors[i];
            var screen = FindNamed(previous, monitor.Name) ?? FindNamed(next, monitor.Name);
            if (screen is null)
                screen = new Screen(NextMonitorId(previous, next), monitor, effective);
            else
            {
                screen.Reported = monitor;
                screen.Effective = effective;
            }

            next.Add(screen);
        }

        _screens.Clear();
        _screens.AddRange(next);

        var enabled = EnabledScreens();
        Screen? home = null;
        foreach (var screen in enabled)
        {
            if (!screen.Focused)
                continue;

            home = screen;
            break;
        }

        home ??= enabled.Count > 0 ? enabled[0] : null;
        Relocate(home, enabled);
        EnsurePersistent();
        PlaceBound();
        AssignActives(enabled);
        RefitAll();
        EnsureFocus(home);
        SyncActive();
        var outcome = View(ShownPlacements(), ShownWindows(), HiddenWindows());
        return arrangement.Orientations.Length == 0
            ? outcome
            : outcome with { Orientations = arrangement.Orientations };
    }

    private void Relocate(Screen? home, List<Screen> enabled)
    {
        var displaced = new List<Desk>();
        foreach (var desk in _desks)
        {
            if (desk.Id.IsSpecial || HeldBy(enabled, desk.Monitor))
                continue;

            displaced.Add(desk);
        }

        if (home is null || displaced.Count == 0)
        {
            RehomeSpecials(home, enabled);
            return;
        }

        var occupied = false;
        foreach (var desk in _desks)
        {
            if (desk.Monitor != home.Id)
                continue;

            occupied = true;
            break;
        }

        if (!occupied)
        {
            foreach (var desk in displaced)
                desk.Monitor = home.Id;

            home.Active ??= displaced[0].Id;
            RehomeSpecials(home, enabled);
            return;
        }

        var target = DeskFor(home.Active ?? NextFree(home.Id), home.Id);
        home.Active = target.Id;
        foreach (var desk in displaced)
        {
            if (desk.Id == target.Id)
            {
                desk.Monitor = home.Id;
                continue;
            }

            Absorb(desk, target, home.Effective.WorkArea);
            desk.Monitor = home.Id;
        }

        RehomeSpecials(home, enabled);
    }

    private void AssignActives(List<Screen> enabled)
    {
        var claimed = new HashSet<WorkspaceId>();
        foreach (var screen in enabled)
        {
            if (screen.Active is WorkspaceId active && OnScreen(screen, active) is not null && claimed.Add(active))
                continue;

            if (TryDefault(screen, out var preferred) && claimed.Add(preferred))
            {
                var chosen = DeskFor(preferred, screen.Id);
                if (chosen.Monitor == screen.Id)
                {
                    screen.Active = chosen.Id;
                    continue;
                }

                claimed.Remove(preferred);
            }

            Desk? own = null;
            foreach (var desk in _desks)
            {
            if (desk.Monitor != screen.Id || desk.Id.IsSpecial || !claimed.Add(desk.Id))
                continue;

                own = desk;
                break;
            }

            own ??= DeskFor(NextFree(screen.Id), screen.Id);
            screen.Active = own.Id;
            claimed.Add(own.Id);
        }

        foreach (var screen in _screens)
        {
            if (!screen.Effective.Enabled)
                screen.Active ??= WorkspaceId.Initial;
        }
    }

    private Screen? ShownElsewhere(WorkspaceId target)
    {
        var desk = FindDesk(target);
        if (desk is null)
            return null;

        var owner = ScreenById(desk.Monitor);
        if (owner is null || !owner.Effective.Enabled || owner.Active != target || owner.Id == FocusedId())
            return null;

        return owner;
    }

    private Outcome FocusShown(Screen owner, WorkspaceId target, WindowId? focus, bool consumed, string? dispatcher, bool allowsElevation)
    {
        Focus(owner.Id);
        var destination = FindDesk(target);
        WindowId? next = null;
        if (focus is WindowId requested && ClientOn(target, requested) is not null)
            next = requested;
        else if (destination is not null)
            next = Remembered(destination);

        _active = next;
        if (destination is not null)
            destination.Focus = next;

        return View([], focus: next, consumed: consumed, dispatcher: dispatcher, allowsElevation: allowsElevation);
    }

    private void BringTo(Screen screen, Desk desk)
    {
        if (desk.Monitor == screen.Id)
            return;

        var owner = ScreenById(desk.Monitor);
        var moving = desk.Id;
        var leaving = owner is not null && owner.Active == moving;
        desk.Monitor = screen.Id;
        Refit(desk);
        if (owner is null || !leaving)
            return;

        owner.Active = null;
        AssignActives(EnabledScreens());
        SyncActive();
        if (owner.Active is not WorkspaceId stay || stay == moving)
            return;

        var pinned = PinnedFloating(desk);
        if (pinned.Count == 0)
            return;

        var home = DeskFor(stay, owner.Id);
        foreach (var window in pinned)
            Transfer(desk, home, window);
    }

    private static void Absorb(Desk source, Desk destination, Rectangle area)
    {
        var windows = new List<WindowId>(source.Layout.Clients.Count);
        foreach (var client in source.Layout.Clients)
            windows.Add(client.Window);

        foreach (var window in windows)
        {
            if (!source.Layout.TryTake(window, out var taken))
                continue;

            NoteScrollingWidth(destination.Layout, window, taken);
            destination.Layout.Add(
                window,
                area,
                null,
                taken.Floating,
                taken.Bounds,
                destination.Id,
                taken.Class,
                taken.Title,
                processId: taken.ProcessId,
                initialClass: taken.InitialClass,
                initialTitle: taken.InitialTitle,
                minSize: taken.MinSize,
                maxSize: taken.MaxSize);
            if (taken.Minimized)
                destination.Layout.Minimize(window);
        }
    }

    private static void NoteScrollingWidth(Layout layout, WindowId window, Taken taken)
    {
        if (taken.ScrollingWidth is double width)
            layout.NoteScrollingWidth(window, width);
    }

    private void RefitAll()
    {
        foreach (var desk in _desks)
            Refit(desk);
    }

    private void Refit(Desk desk)
    {
        Stamp(desk);
        var screen = ScreenById(desk.Monitor);
        if (screen is not { Effective.Enabled: true })
            return;

        desk.Layout.Apply(_master);
        desk.Layout.Apply(desk.Options);
        desk.Layout.Fit(screen.Effective.WorkArea);
    }

    private void EnsureFocus(Screen? home)
    {
        foreach (var screen in _screens)
        {
            if (screen.Focused && screen.Effective.Enabled)
                return;
        }

        Focus(home?.Id ?? MonitorId.Primary);
    }

    private void Focus(MonitorId id)
    {
        var found = false;
        foreach (var screen in _screens)
        {
            var focused = screen.Id == id && screen.Effective.Enabled;
            screen.Focused = focused;
            if (focused)
                found = true;
        }

        if (!found)
        {
            foreach (var screen in _screens)
            {
                if (!screen.Effective.Enabled)
                    continue;

                screen.Focused = true;
                break;
            }
        }

        Remember(ActiveOn(FocusedId()), FocusedId());
    }

    private void Remember(WorkspaceId workspace, MonitorId monitor)
    {
        _history.RemoveAll(visit => visit.Workspace == workspace);
        _history.Insert(0, new WorkspaceVisit(workspace, monitor));
    }

    private WorkspaceId? Previous(MonitorId monitor, bool perMonitor)
    {
        var active = ActiveOn(monitor);
        var index = _history.FindIndex(visit => visit.Workspace == active);
        if (!perMonitor)
            return index >= 0 && index + 1 < _history.Count ? _history[index + 1].Workspace : null;

        for (var i = index < 0 ? 0 : index + 1; i < _history.Count; i++)
        {
            if (_history[i].Monitor == monitor)
                return _history[i].Workspace;
        }

        return null;
    }

    private void SyncActive()
    {
        foreach (var screen in _screens)
        {
            if (screen.Active is WorkspaceId active)
                _activeOn[screen.Id] = active;
        }
    }

    private WorkspaceAddress.Context Context() => Context(FocusedId());

    private WorkspaceAddress.Context Context(MonitorId monitor) =>
        new(
            ActiveOn(monitor),
            Previous(monitor, perMonitor: false),
            Previous(monitor, perMonitor: true),
            Listed(monitor),
            Listed(null),
            NumberIsEmpty,
            number => OwnedByOtherMonitor(number, monitor));

    private IReadOnlyList<WorkspaceId> Listed(MonitorId? only)
    {
        var named = new List<WorkspaceId>();
        var numbered = new List<(int Number, WorkspaceId Id)>();
        foreach (var desk in _desks)
        {
            if (desk.Id.IsSpecial)
                continue;

            if (only is MonitorId monitor && desk.Monitor != monitor)
                continue;

            if (desk.Id.TryGetNumber(out var number))
                numbered.Add((number, desk.Id));
            else
                named.Add(desk.Id);
        }

        named.Reverse();
        numbered.Sort((left, right) => left.Number.CompareTo(right.Number));
        foreach (var (_, id) in numbered)
            named.Add(id);

        return named;
    }

    private bool OwnedByOtherMonitor(int number, MonitorId monitor)
    {
        var desk = FindDesk(WorkspaceId.FromNumber(number));
        return desk is not null && desk.Monitor != monitor;
    }

    private bool NumberIsEmpty(int number) =>
        FindDesk(WorkspaceId.FromNumber(number)) is not Desk desk || desk.Layout.Clients.Count == 0;

    private readonly record struct Captured(State State, WindowId? Active);

    private Captured Capture() => new(Snapshot(), ActiveWindow());

    private Outcome WithEvents(Outcome outcome, Captured before, Input? input)
    {
        outcome = FinishCursor(before, outcome, input);
        var events = Diff(before.State, outcome.State, before.Active, ActiveWindow());
        if (input is ConfigLoaded loaded)
        {
            foreach (var warning in loaded.Config.Warnings)
                events.Add(new ConfigWarned(warning.Line, warning.Message, warning.Source));
        }

        if (_configEvents is not null)
        {
            events.AddRange(_configEvents);
            _configEvents = null;
        }

        if (_extraEvents is { Count: > 0 })
        {
            events.AddRange(_extraEvents);
            _extraEvents = null;
        }

        if (_noted is not null)
        {
            events.AddRange(_noted);
            _noted = null;
        }

        return events.Count == 0 ? outcome : outcome with { Events = events };
    }

    private bool TryEnterSubmap(string arguments, out string? error)
    {
        var name = arguments.Trim();
        if (name.Length == 0 || name == "reset")
        {
            EnterSubmap("");
            error = null;
            return true;
        }

        if (!_binds.HasSubmap(name))
        {
            error = "error: submap '" + name + "' does not exist";
            return false;
        }

        EnterSubmap(name);
        error = null;
        return true;
    }

    private void EnterSubmap(string name)
    {
        _keyState.Submap = name;
        _noted ??= [];
        _noted.Add(new SubmapChanged(name));
    }

    private static List<StreamEvent> Diff(State before, State after, WindowId? activeBefore, WindowId? activeAfter)
    {
        var events = new List<StreamEvent>();
        AddWorkspaceEvents(events, before, after);
        var previous = ByWindow(before);
        var next = ByWindow(after);
        foreach (var pair in previous)
        {
            if (!next.ContainsKey(pair.Key))
                events.Add(new ClientClosed(pair.Key));
        }

        foreach (var pair in next)
        {
            if (!previous.TryGetValue(pair.Key, out var prior))
            {
                events.Add(new ClientOpened(pair.Key, pair.Value.Workspace, pair.Value.Class, pair.Value.Title));
                if (pair.Value.Fullscreen != FullscreenMode.None)
                    events.Add(new FullscreenChanged(true));
                continue;
            }

            if (prior.Workspace != pair.Value.Workspace)
                events.Add(new ClientMoved(pair.Key, pair.Value.Workspace));

            if (prior.Floating != pair.Value.Floating)
                events.Add(new FloatingChanged(pair.Key, pair.Value.Floating));

            if (prior.Minimized != pair.Value.Minimized)
                events.Add(new ClientMinimized(pair.Key, pair.Value.Minimized));

            if (prior.Fullscreen != pair.Value.Fullscreen)
                events.Add(new FullscreenChanged(pair.Value.Fullscreen != FullscreenMode.None));
        }

        if (after.ActiveMonitor != before.ActiveMonitor)
        {
            var name = ScreenName(after, after.ActiveMonitor);
            if (name.Length > 0)
                events.Add(new MonitorFocused(name, WorkspaceOn(after, after.ActiveMonitor)));
        }

        if (after.ActiveWorkspace != before.ActiveWorkspace)
            events.Add(new WorkspaceActivated(after.ActiveWorkspace));

        if (activeAfter != activeBefore)
            events.Add(new FocusChanged(activeAfter));

        AddSpecialEvents(events, before, after);
        AddMonitorEvents(events, before, after);
        return events;
    }

    private static void AddWorkspaceEvents(List<StreamEvent> events, State before, State after)
    {
        var previous = new Dictionary<WorkspaceId, Workspace>(before.Workspaces.Count);
        foreach (var workspace in before.Workspaces)
            previous[workspace.Id] = workspace;

        var next = new HashSet<WorkspaceId>();
        foreach (var workspace in after.Workspaces)
            next.Add(workspace.Id);

        foreach (var workspace in after.Workspaces)
        {
            if (!previous.ContainsKey(workspace.Id))
                events.Add(new WorkspaceCreated(workspace.Id));
        }

        foreach (var workspace in before.Workspaces)
        {
            if (next.Contains(workspace.Id))
                continue;

            events.Add(new WorkspaceDestroyed(workspace.Id, CommandProtocol.WorkspaceNumber(before, workspace.Id)));
        }

        foreach (var workspace in after.Workspaces)
        {
            if (!previous.TryGetValue(workspace.Id, out var prior) || prior.Monitor == workspace.Monitor)
                continue;

            var name = ScreenName(after, workspace.Monitor);
            if (name.Length == 0)
                continue;

            events.Add(new WorkspaceMoved(workspace.Id, name));
        }
    }

    private static string ScreenName(State state, MonitorId id)
    {
        foreach (var screen in state.Screens)
        {
            if (screen.Id == id)
                return screen.Monitor.Name;
        }

        return "";
    }

    private static WorkspaceId WorkspaceOn(State state, MonitorId id)
    {
        foreach (var screen in state.Screens)
        {
            if (screen.Id == id)
                return screen.ActiveWorkspace;
        }

        return state.ActiveWorkspace;
    }

    private static void AddMonitorEvents(List<StreamEvent> events, State before, State after)
    {
        var nextNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var screen in after.Screens)
            nextNames.Add(screen.Monitor.Name);

        foreach (var screen in before.Screens)
        {
            if (nextNames.Contains(screen.Monitor.Name))
                continue;

            events.Add(new MonitorRemoved(screen.Monitor.Name));
            events.Add(new MonitorRemovedV2(screen.Id.Value, screen.Monitor.Name, screen.Monitor.Description));
        }

        var previous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var screen in before.Screens)
            previous.Add(screen.Monitor.Name);

        foreach (var screen in after.Screens)
        {
            if (previous.Contains(screen.Monitor.Name))
                continue;

            events.Add(new MonitorAdded(screen.Monitor.Name));
            events.Add(new MonitorAddedV2(screen.Id.Value, screen.Monitor.Name, screen.Monitor.Description));
        }
    }

    private static Dictionary<WindowId, Client> ByWindow(State state)
    {
        var map = new Dictionary<WindowId, Client>(state.Clients.Count);
        foreach (var client in state.Clients)
            map[client.Window] = client;

        return map;
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

    private Outcome Decorate(Outcome outcome, State before)
    {
        var active = ActiveWindow();
        var decorations = new List<Decoration>(outcome.State.Clients.Count);
        foreach (var client in outcome.State.Clients)
        {
            var borderless = client.Fullscreen == FullscreenMode.Fullscreen;
            var border = client.Window == active ? _decoration.ActiveBorder : _decoration.InactiveBorder;
            var corners = CornersFor(client);
            var choice = OpacityOf(client.Window);
            var special = FindDesk(client.Workspace) is Desk home ? SpecialOn(home.Monitor) : null;
            var alpha = DecorationAlpha.Compute(
                _decoration,
                new OpacityInput(
                    client.Window == active,
                    Fullscreen: borderless,
                    SpecialOpen: special is not null,
                    OnSpecial: special == client.Workspace),
                choice.Rule,
                choice.Opaque);
            decorations.Add(new Decoration(
                client.Id,
                border,
                borderless ? CornerPreference.DoNotRound : corners,
                alpha,
                borderless));
        }

        var present = new HashSet<WindowId>(outcome.State.Clients.Count);
        foreach (var client in outcome.State.Clients)
            present.Add(client.Window);

        var reset = new List<WindowId>();
        foreach (var client in before.Clients)
        {
            if (!present.Contains(client.Window))
                reset.Add(client.Window);
        }

        return outcome with
        {
            Decorations = decorations,
            ResetDecorations = reset,
        };
    }

    private Outcome View(
        IReadOnlyList<Placement> placements,
        IReadOnlyList<WindowId>? show = null,
        IReadOnlyList<WindowId>? hide = null,
        bool consumed = false,
        string? dispatcher = null,
        WindowId? close = null,
        WindowId? focus = null,
        bool allowsElevation = false)
    {
        var snapshot = Snapshot();
        placements = Cover(placements, snapshot);
        var minimized = PlanMinimized(snapshot);
        return new(placements, snapshot, consumed, dispatcher, close, focus)
        {
            Show = show ?? [],
            Hide = Combine(hide, minimized.Hide),
            ShowMinimized = minimized.Show,
            AllowsElevation = allowsElevation,
            Launch = TakeBorn(),
        };
    }

    private MinimizedPlan PlanMinimized(State snapshot)
    {
        var hidden = new HashSet<WindowId>(snapshot.Hidden.Count);
        foreach (var client in snapshot.Hidden)
            hidden.Add(client.Window);

        var presence = new MinimizedPresence[snapshot.Clients.Count];
        for (var i = 0; i < snapshot.Clients.Count; i++)
        {
            var client = snapshot.Clients[i];
            presence[i] = new MinimizedPresence(client.Window, !hidden.Contains(client.Window), client.Minimized);
        }

        var plan = MinimizedConcealment.Plan(presence, _concealedMinimized);
        _concealedMinimized.Clear();
        foreach (var window in plan.Concealed)
            _concealedMinimized.Add(window);
        return plan;
    }

    private static WindowId[] Combine(IReadOnlyList<WindowId>? primary, IReadOnlyList<WindowId> extra)
    {
        var first = primary ?? [];
        if (extra.Count == 0)
            return first as WindowId[] ?? [.. first];

        var combined = new List<WindowId>(first.Count + extra.Count);
        combined.AddRange(first);
        foreach (var window in extra)
        {
            if (!combined.Contains(window))
                combined.Add(window);
        }

        return [.. combined];
    }

    private State Snapshot()
    {
        var clients = new List<Client>();
        var workspaces = new List<Workspace>(_desks.Count);
        foreach (var desk in _desks)
        {
            foreach (var client in desk.Layout.Clients)
            {
                var slot = desk.Layout.TryBounds(client.Window, out var found) ? found : client.Bounds;
                var pinned = client.Floating && _pinned.Contains(client.Window);
                clients.Add(client with { Bounds = PlacedBounds(client, desk, slot), Pinned = pinned });
            }

            workspaces.Add(new Workspace(desk.Id, desk.Monitor)
            {
                LastWindow = desk.Focus,
                Persistent = desk.Persistent,
                DisplayName = desk.DisplayName,
            });
        }

        var hidden = new List<Client>();
        foreach (var client in clients)
        {
            if (!IsShown(client.Workspace) && !StaysVisible(client))
                hidden.Add(client);
        }

        var monitors = new List<Monitor>(_screens.Count);
        var screens = new List<MonitorSnapshot>(_screens.Count);
        var focused = FocusedId();
        foreach (var screen in _screens)
        {
            monitors.Add(screen.Effective);
            screens.Add(new MonitorSnapshot(
                screen.Id,
                screen.Effective,
                screen.Active ?? WorkspaceId.Initial,
                screen.Id == focused && screen.Effective.Enabled)
            {
                SpecialWorkspace = screen.Special,
            });
        }

        return new State(clients)
        {
            Workspaces = workspaces,
            ActiveWorkspace = ActiveId(),
            SpecialWorkspace = SpecialOn(focused),
            ActiveMonitor = focused,
            Hidden = hidden,
            Monitors = monitors,
            Screens = screens,
        };
    }

    private WorkspaceId ActiveId() => ActiveOn(FocusedId());

    private WorkspaceId ActiveOn(MonitorId monitor)
    {
        if (ScreenById(monitor) is { Active: WorkspaceId active })
            return active;

        return _activeOn.TryGetValue(monitor, out var stored) ? stored : WorkspaceId.Initial;
    }

    private MonitorId FocusedId()
    {
        foreach (var screen in _screens)
        {
            if (screen.Focused && screen.Effective.Enabled)
                return screen.Id;
        }

        if (_screens.Count == 0)
            return MonitorId.Primary;

        foreach (var screen in _screens)
        {
            if (screen.Effective.Enabled)
                return screen.Id;
        }

        return _screens[0].Id;
    }

    private bool IsShown(WorkspaceId id)
    {
        if (id.IsSpecial)
            return MonitorShowing(id) is not null;

        if (_screens.Count == 0)
            return id == ActiveId();

        foreach (var screen in _screens)
        {
            if (screen.Effective.Enabled && screen.Active == id)
                return true;
        }

        return false;
    }

    private Desk ActiveDesk() => DeskFor(ActiveId(), FocusedId());

    private Desk DeskFor(WorkspaceId id, MonitorId? monitor = null)
    {
        var existing = FindDesk(id);
        if (existing is not null)
            return existing;

        var hinted = WorkspaceRules.For(_workspaceRules, id, null, null, 0, 0, 0);
        var bound = BoundScreen(hinted);
        var owner = bound?.Id ?? monitor ?? FocusedId();
        var layout = LayoutFactory.Create(_config);
        layout.Configure(_scrolling);
        var desk = new Desk(id, owner, layout);
        _desks.Add(desk);
        Stamp(desk);
        var settled = BoundScreen(RuleFor(desk));
        if (settled is not null && desk.Monitor != settled.Id)
            desk.Monitor = settled.Id;

        layout.Apply(_master);
        var screen = ScreenById(desk.Monitor);
        if (screen is { Effective.Enabled: true })
        {
            Stamp(desk);
            layout.Apply(desk.Options);
            layout.Fit(screen.Effective.WorkArea);
        }
        else
        {
            layout.Apply(desk.Options);
        }

        NoteCreated(desk);
        return desk;
    }

    private Desk? FindDesk(WorkspaceId id)
    {
        foreach (var desk in _desks)
        {
            if (desk.Id == id)
                return desk;
        }

        return null;
    }

    private Desk? OnScreen(Screen screen, WorkspaceId id)
    {
        var desk = FindDesk(id);
        return desk is not null && desk.Monitor == screen.Id ? desk : null;
    }

    private Desk? DeskContaining(WindowId window)
    {
        foreach (var desk in _desks)
        {
            if (ClientOn(desk, window) is not null)
                return desk;
        }

        return null;
    }

    private Client? Find(WindowId window)
    {
        foreach (var desk in _desks)
        {
            if (ClientOn(desk, window) is { } client)
                return client;
        }

        return null;
    }

    private Client? ClientOn(WorkspaceId id, WindowId window)
    {
        var desk = FindDesk(id);
        return desk is null ? null : ClientOn(desk, window);
    }

    private static Client? ClientOn(Desk desk, WindowId window)
    {
        foreach (var client in desk.Layout.Clients)
        {
            if (client.Window == window)
                return client;
        }

        return null;
    }

    private WindowId[] VisibleIds(WorkspaceId id)
    {
        var desk = FindDesk(id);
        if (desk is null)
            return [];

        var showing = Showing(desk);
        var visible = new List<WindowId>();
        foreach (var client in desk.Layout.Clients)
        {
            if (!client.Minimized && showing.Contains(client.Window))
                visible.Add(client.Window);
        }

        return visible.ToArray();
    }

    // A Layout may omit tiled Clients from Visible when it is only showing one of them.
    private static HashSet<WindowId> Showing(Desk desk)
    {
        var ids = new Dictionary<ClientId, WindowId>(desk.Layout.Clients.Count);
        foreach (var client in desk.Layout.Clients)
            ids[client.Id] = client.Window;

        var showing = new HashSet<WindowId>();
        foreach (var placement in desk.Layout.Visible())
        {
            if (ids.TryGetValue(placement.Client, out var window))
                showing.Add(window);
        }

        return showing;
    }

    private static HashSet<WindowId> Covered(Desk desk)
    {
        var showing = Showing(desk);
        var covered = new HashSet<WindowId>();
        foreach (var client in desk.Layout.Clients)
        {
            if (client.Minimized || client.Floating || showing.Contains(client.Window))
                continue;

            covered.Add(client.Window);
        }

        return covered;
    }

    private static void Collect(Desk desk, HashSet<WindowId> covered, List<WindowId> show, List<WindowId> hide)
    {
        var after = Covered(desk);
        foreach (var window in after)
        {
            if (!covered.Contains(window))
                hide.Add(window);
        }

        foreach (var window in covered)
        {
            if (!after.Contains(window) && ClientOn(desk, window) is { Minimized: false })
                show.Add(window);
        }
    }

    private Outcome Present(
        Desk desk,
        HashSet<WindowId> covered,
        IReadOnlyList<Placement> placements,
        IReadOnlyList<WindowId>? show = null,
        IReadOnlyList<WindowId>? hide = null,
        bool consumed = false,
        string? dispatcher = null,
        WindowId? close = null,
        WindowId? focus = null,
        bool allowsElevation = false,
        bool focusRevealed = false)
    {
        var showList = new List<WindowId>(show ?? []);
        var hideList = new List<WindowId>(hide ?? []);
        Collect(desk, covered, showList, hideList);
        if (placements.Count == 0 && (showList.Count > 0 || hideList.Count > 0))
            placements = desk.Layout.Placed();

        if (focusRevealed && focus is null && showList.Count == 1)
        {
            focus = showList[0];
            _active = focus;
            desk.Focus = focus;
            desk.Layout.NoteFocus(showList[0]);
            TouchHistory(showList[0]);
        }

        return View(placements, showList, hideList, consumed, dispatcher, close, focus, allowsElevation);
    }

    private static WindowId? LastVisible(Desk desk)
    {
        for (var i = desk.Layout.Clients.Count - 1; i >= 0; i--)
        {
            if (!desk.Layout.Clients[i].Minimized)
                return desk.Layout.Clients[i].Window;
        }

        return null;
    }

    private static WindowId? Remembered(Desk desk)
    {
        if (desk.Focus is WindowId focus && ClientOn(desk, focus) is { Minimized: false })
            return focus;

        return LastVisible(desk);
    }

    private void DropIfEmpty(WorkspaceId id)
    {
        if (IsShown(id))
            return;

        var desk = FindDesk(id);
        if (desk is null || desk.Persistent || desk.Layout.Clients.Count > 0)
            return;

        _desks.Remove(desk);
    }

    private List<Placement> ShownPlacements()
    {
        var placements = new List<Placement>();
        if (_screens.Count == 0)
        {
            placements.AddRange(ActiveDesk().Layout.Visible());
            WorkspaceId? overlayId = null;
            if (SpecialOn(MonitorId.Primary) is WorkspaceId primary && FindDesk(primary) is Desk overlay)
            {
                overlayId = overlay.Id;
                placements.AddRange(overlay.Layout.Visible());
            }

            foreach (var desk in _desks)
            {
                if (desk.Id != ActiveId() && desk.Id != overlayId)
                    AddPinnedPlacements(desk, placements);
            }

            return placements;
        }

        foreach (var screen in _screens)
        {
            if (!screen.Effective.Enabled || screen.Active is not WorkspaceId active)
                continue;

            var desk = FindDesk(active);
            if (desk is not null)
                placements.AddRange(desk.Layout.Visible());

            if (screen.Special is WorkspaceId special && FindDesk(special) is Desk overlay)
                placements.AddRange(overlay.Layout.Visible());
        }

        foreach (var desk in _desks)
        {
            if (!IsShown(desk.Id))
                AddPinnedPlacements(desk, placements);
        }

        return placements;
    }

    private void AddPinnedPlacements(Desk desk, List<Placement> placements)
    {
        if (!MonitorShowing(desk.Monitor))
            return;

        foreach (var client in desk.Layout.Clients)
        {
            if (!client.Floating || client.Minimized || !_pinned.Contains(client.Window))
                continue;

            if (desk.Layout.TryBounds(client.Window, out var bounds))
                placements.Add(new Placement(client.Id, bounds));
        }
    }

    private WindowId[] ShownWindows()
    {
        var windows = new List<WindowId>();
        foreach (var desk in _desks)
        {
            if (IsShown(desk.Id))
            {
                var showing = Showing(desk);
                foreach (var client in desk.Layout.Clients)
                {
                    if (!client.Minimized && showing.Contains(client.Window))
                        windows.Add(client.Window);
                }

                continue;
            }

            if (!MonitorShowing(desk.Monitor))
                continue;

            foreach (var client in desk.Layout.Clients)
            {
                if (client.Floating && !client.Minimized && _pinned.Contains(client.Window))
                    windows.Add(client.Window);
            }
        }

        return windows.ToArray();
    }

    private WindowId[] HiddenWindows()
    {
        var windows = new List<WindowId>();
        foreach (var desk in _desks)
        {
            if (IsShown(desk.Id))
            {
                var covered = Covered(desk);
                foreach (var client in desk.Layout.Clients)
                {
                    if (covered.Contains(client.Window))
                        windows.Add(client.Window);
                }

                continue;
            }

            foreach (var client in desk.Layout.Clients)
            {
                if (client.Minimized)
                    continue;

                if (client.Floating && _pinned.Contains(client.Window) && MonitorShowing(desk.Monitor))
                    continue;

                windows.Add(client.Window);
            }
        }

        return windows.ToArray();
    }

    private Rectangle FocusedMonitor()
    {
        if (_screens.Count == 0 || ScreenById(FocusedId()) is not { Effective: { Enabled: true } monitor })
            return default;

        return new Rectangle(monitor.X, monitor.Y, monitor.Width, monitor.Height);
    }

    private Rectangle WorkArea(Desk desk, Rectangle fallback)
    {
        var screen = ScreenById(desk.Monitor);
        if (screen is { Effective.Enabled: true })
            return screen.Effective.WorkArea;

        return fallback;
    }

    private Screen? ScreenFor(WindowAppeared appeared)
    {
        if (_screens.Count == 0)
            return null;

        var byArea = Match(screen => screen.Reported.WorkArea == appeared.WorkArea);
        if (byArea is not null)
            return byArea;

        if (appeared.Pointer is { } pointer)
        {
            var byPointer = Match(screen => Contains(screen.Reported, pointer));
            if (byPointer is not null)
                return byPointer;
        }

        if (appeared.Bounds is { Width: > 0, Height: > 0 } bounds)
        {
            var center = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
            var byBounds = Match(screen => Contains(screen.Reported, center));
            if (byBounds is not null)
                return byBounds;
        }

        return Match(_ => true);
    }

    private Screen? Match(Func<Screen, bool> predicate)
    {
        foreach (var screen in _screens)
        {
            if (screen.Effective.Enabled && predicate(screen))
                return screen;
        }

        return null;
    }

    private static bool Contains(Monitor monitor, Point point) =>
        point.X >= monitor.X
        && point.Y >= monitor.Y
        && point.X < monitor.X + monitor.Width
        && point.Y < monitor.Y + monitor.Height;

    private Screen? ScreenById(MonitorId id)
    {
        foreach (var screen in _screens)
        {
            if (screen.Id == id)
                return screen;
        }

        return null;
    }

    private static Screen? FindNamed(IReadOnlyList<Screen> screens, string name)
    {
        foreach (var screen in screens)
        {
            if (string.Equals(screen.Reported.Name, name, StringComparison.OrdinalIgnoreCase))
                return screen;
        }

        return null;
    }

    private static bool HeldBy(List<Screen> enabled, MonitorId id)
    {
        foreach (var screen in enabled)
        {
            if (screen.Id == id)
                return true;
        }

        return false;
    }

    private List<Screen> EnabledScreens()
    {
        var enabled = new List<Screen>();
        foreach (var screen in _screens)
        {
            if (screen.Effective.Enabled)
                enabled.Add(screen);
        }

        return enabled;
    }

    private static MonitorId NextMonitorId(IReadOnlyList<Screen> previous, List<Screen> next)
    {
        var used = new HashSet<int>();
        foreach (var screen in previous)
            used.Add(screen.Id.Value);

        foreach (var screen in next)
            used.Add(screen.Id.Value);

        var id = 0;
        while (used.Contains(id))
            id++;

        return new MonitorId(id);
    }

    private WorkspaceId NextFree(MonitorId monitor)
    {
        for (var number = 1; number < int.MaxValue; number++)
        {
            var id = WorkspaceId.FromNumber(number);
            if (FindDesk(id) is not null || BoundElsewhere(id, monitor))
                continue;

            return id;
        }

        throw new InvalidOperationException("No workspace id remains.");
    }

    private bool BoundElsewhere(WorkspaceId id, MonitorId monitor)
    {
        var bound = WorkspaceRules.BoundMonitor(_workspaceRules, id);
        if (bound.Length == 0)
            return false;

        var screen = ScreenById(monitor);
        return screen is null || !MonitorRules.Matches(screen.Effective, bound);
    }

    private bool TryDefault(Screen screen, out WorkspaceId id)
    {
        id = default;
        var text = WorkspaceRules.DefaultWorkspace(_workspaceRules, screen.Effective);
        return text is not null && WorkspaceRules.TryConcrete(text, out id);
    }

    private void EnsurePersistent()
    {
        foreach (var rule in _workspaceRules)
        {
            if (rule.Persistent != true || !WorkspaceRules.TryConcrete(rule.WorkspaceString, out var id) || FindDesk(id) is not null)
                continue;

            DeskFor(id, BoundScreen(rule)?.Id);
        }

        foreach (var desk in _desks)
            desk.Persistent = RuleFor(desk)?.Persistent == true;
    }

    private void PlaceBound()
    {
        foreach (var desk in _desks)
        {
            var screen = BoundScreen(RuleFor(desk));
            if (screen is null || desk.Monitor == screen.Id)
                continue;

            var previous = ScreenById(desk.Monitor);
            desk.Monitor = screen.Id;
            if (previous is not null && previous.Active == desk.Id)
                previous.Active = null;
        }
    }

    private void DropEmpty()
    {
        foreach (var desk in _desks.ToArray())
            DropIfEmpty(desk.Id);
    }

    private void Stamp(Desk desk)
    {
        var rule = RuleFor(desk);
        desk.Persistent = rule?.Persistent == true;
        desk.DisplayName = rule?.DefaultName;
        desk.Options = OptionsFor(desk, rule);
    }

    private WorkspaceRule? RuleFor(Desk desk)
    {
        var screen = ScreenById(desk.Monitor);
        var counts = ClientCounts(desk);
        var first = WorkspaceRules.For(
            _workspaceRules,
            desk.Id,
            desk.DisplayName,
            screen?.Effective,
            counts.Windows,
            counts.Tiled,
            counts.Floating);
        var name = first?.DefaultName ?? desk.DisplayName;
        if (name == desk.DisplayName)
            return first;

        return WorkspaceRules.For(
            _workspaceRules,
            desk.Id,
            name,
            screen?.Effective,
            counts.Windows,
            counts.Tiled,
            counts.Floating);
    }

    private LayoutOptions OptionsFor(Desk desk, WorkspaceRule? rule)
    {
        var screen = ScreenById(desk.Monitor);
        var scale = screen is { Effective.Enabled: true } ? screen.Effective.Scale : 1;
        var options = _options;
        if (rule is not null)
        {
            if (rule.GapsIn is EdgeInsets gapsIn)
                options = options with { GapsIn = gapsIn };
            if (rule.GapsOut is EdgeInsets gapsOut)
                options = options with { GapsOut = gapsOut };

            var border = options.BorderSize;
            if (rule.BorderSize is int size)
                border = Math.Clamp(size, 0, 20);
            if (rule.Border == false || rule.Decorate == false)
                border = 0;
            options = options with { BorderSize = border };
            foreach (var pair in rule.LayoutOpts)
                LayoutOptions.TryOverrideDwindle(options, pair.Key, pair.Value, out options);
        }

        return Scaled(options, scale);
    }

    private Screen? BoundScreen(WorkspaceId id)
    {
        var desk = FindDesk(id);
        return BoundScreen(desk is null
            ? WorkspaceRules.For(_workspaceRules, id, null, null, 0, 0, 0)
            : RuleFor(desk));
    }

    private Screen? BoundScreen(WorkspaceRule? rule)
    {
        if (rule is null || rule.Monitor.Length == 0)
            return null;

        foreach (var screen in _screens)
        {
            if (screen.Effective.Enabled && MonitorRules.Matches(screen.Effective, rule.Monitor))
                return screen;
        }

        return null;
    }

    private void NoteCreated(Desk desk)
    {
        var command = RuleFor(desk)?.OnCreatedEmpty;
        if (string.IsNullOrEmpty(command))
            return;

        var parsed = LaunchParser.Parse(command, allowsElevation: true, raw: false);
        if (parsed.Launch is Launch launch)
            _born.Enqueue(WithEnvironment(launch) ?? launch);
    }

    private Launch? TakeBorn() => _born.Count == 0 ? null : _born.Dequeue();

    private WorkspaceId Alias(WorkspaceId id)
    {
        if (FindDesk(id) is not null)
            return id;

        foreach (var desk in _desks)
        {
            if (desk.DisplayName == id.Name)
                return desk.Id;
        }

        return id;
    }

    private CornerPreference CornersFor(Client client)
    {
        var desk = FindDesk(client.Workspace);
        if (desk is null || RuleFor(desk)?.Rounding != false)
            return _decoration.Corners;

        return CornerPreference.DoNotRound;
    }

    private static (int Windows, int Tiled, int Floating) ClientCounts(Desk desk)
    {
        var tiled = 0;
        var floating = 0;
        foreach (var client in desk.Layout.Clients)
        {
            if (client.Floating)
                floating++;
            else
                tiled++;
        }

        return (desk.Layout.Clients.Count, tiled, floating);
    }

    private Monitor[] Reported()
    {
        var monitors = new Monitor[_screens.Count];
        for (var i = 0; i < _screens.Count; i++)
            monitors[i] = _screens[i].Reported;

        return monitors;
    }

    private static LayoutOptions Scaled(LayoutOptions options, double scale)
    {
        if (scale is <= 0 or >= 1000 || !double.IsFinite(scale) || scale == 1)
            return options;

        return options with
        {
            GapsIn = Scale(options.GapsIn, scale),
            GapsOut = Scale(options.GapsOut, scale),
            FloatGaps = Scale(options.FloatGaps, scale),
            BorderSize = Scale(options.BorderSize, scale),
            ExtendBorderGrabArea = Scale(options.ExtendBorderGrabArea, scale),
            SnapWindowGap = Scale(options.SnapWindowGap, scale),
            SnapMonitorGap = Scale(options.SnapMonitorGap, scale),
        };
    }

    private static EdgeInsets Scale(EdgeInsets insets, double scale) =>
        new(Scale(insets.Top, scale), Scale(insets.Right, scale), Scale(insets.Bottom, scale), Scale(insets.Left, scale));

    private static int Scale(int value, double scale) =>
        (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);

    private readonly record struct WorkspaceVisit(WorkspaceId Workspace, MonitorId Monitor);

    // A Workspace keeps its own id. The Monitor it belongs to can change when a display is unplugged.
    private sealed class Desk(WorkspaceId id, MonitorId monitor, Layout layout)
    {
        public WorkspaceId Id { get; } = id;

        public MonitorId Monitor { get; set; } = monitor;

        public Layout Layout { get; private set; } = layout;

        public void Replace(Layout layout) => Layout = layout;

        public WindowId? Focus { get; set; }

        public bool Persistent { get; set; }

        public string? DisplayName { get; set; }

        public LayoutOptions Options { get; set; } = LayoutOptions.Unconfigured;
    }

    private sealed class Screen(MonitorId id, Monitor reported, Monitor effective)
    {
        public MonitorId Id { get; } = id;

        public Monitor Reported { get; set; } = reported;

        public Monitor Effective { get; set; } = effective;

        public WorkspaceId? Active { get; set; }

        public WorkspaceId? Special { get; set; }

        public bool Focused { get; set; }
    }
}
