namespace Winland;

public sealed partial class Core
{
    private LiveWorkspace WorkspaceFor(WorkspaceId id, MonitorId? monitor = null)
    {
        var existing = FindWorkspace(id);
        if (existing is not null)
            return existing;

        var hinted = WorkspaceRules.For(_workspaceRules, id, null, null);
        var bound = BoundMonitor(hinted);
        var owner = bound?.Id ?? monitor ?? FocusedId();
        var layout = LayoutFactory.Create(_config, hinted?.Layout, LayoutScripts);
        layout.Configure(_scrolling);
        RankFocus(layout);
        var desk = new LiveWorkspace(id, owner, layout);
        _workspaces.Add(desk);
        Stamp(desk);
        var settled = BoundMonitor(RuleFor(desk));
        if (settled is not null && desk.Monitor != settled.Id)
            desk.Monitor = settled.Id;

        // A rule that matches by name picks the Layout once the Workspace has its name.
        MatchLayout(desk);
        layout = desk.Layout;
        layout.Apply(_master);
        var screen = MonitorById(desk.Monitor);
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

    private bool BoundElsewhere(WorkspaceId id, MonitorId monitor)
    {
        var bound = WorkspaceRules.BoundMonitor(_workspaceRules, id);
        if (bound.Length == 0)
            return false;

        var screen = MonitorById(monitor);
        return screen is null || !MonitorRules.Matches(screen.Effective, bound);
    }

    private bool TryDefault(LiveMonitor screen, out WorkspaceId id)
    {
        id = default;
        var text = WorkspaceRules.DefaultWorkspace(_workspaceRules, screen.Effective);
        return text is not null && WorkspaceRules.TryConcrete(text, out id);
    }

    private void EnsurePersistent()
    {
        foreach (var rule in _workspaceRules)
        {
            if (rule.Persistent != true || !WorkspaceRules.TryConcrete(rule.WorkspaceString, out var id) || FindWorkspace(id) is not null)
                continue;

            // A Workspace bound to a Monitor that is not there yet waits for it, as in the pinned release.
            if (rule.Monitor.Length > 0 && BoundMonitor(rule) is null)
                continue;

            WorkspaceFor(id, BoundMonitor(rule)?.Id);
        }

        foreach (var desk in _workspaces)
            desk.Persistent = PersistentBy(desk, RuleFor(desk));
    }

    private void PlaceBound()
    {
        foreach (var desk in _workspaces)
        {
            var screen = BoundMonitor(RuleFor(desk));
            if (screen is null || desk.Monitor == screen.Id)
                continue;

            var previous = MonitorById(desk.Monitor);
            desk.Monitor = screen.Id;
            if (previous is not null && previous.Active == desk.Id)
                previous.Active = null;
        }
    }

    private void Stamp(LiveWorkspace desk)
    {
        var rule = RuleFor(desk);
        desk.Persistent = PersistentBy(desk, rule);
        if (!desk.Renamed)
            desk.DisplayName = rule?.DefaultName;
        desk.Options = OptionsFor(desk, rule);
    }

    // A persistent rule keeps a normal Workspace whose Monitor is there, as the pinned release marks it. A special
    // workspace is never kept.
    private bool PersistentBy(LiveWorkspace desk, WorkspaceRule? rule) =>
        rule?.Persistent == true && !desk.Id.IsSpecial && (rule.Monitor.Length == 0 || BoundMonitor(rule) is not null);

    private WorkspaceRule? RuleFor(LiveWorkspace desk)
    {
        var screen = MonitorById(desk.Monitor);
        int Count(WindowCount wanted) => desk.CountWindows(wanted, _pinned);
        var fullscreen = desk.CoveringMode();
        var first = WorkspaceRules.For(
            _workspaceRules,
            desk.Id,
            desk.DisplayName,
            screen?.Effective,
            Count,
            fullscreen);
        var name = first?.DefaultName ?? desk.DisplayName;
        if (name == desk.DisplayName)
            return first;

        return WorkspaceRules.For(
            _workspaceRules,
            desk.Id,
            name,
            screen?.Effective,
            Count,
            fullscreen);
    }

    private LayoutOptions OptionsFor(LiveWorkspace desk, WorkspaceRule? rule)
    {
        var screen = MonitorById(desk.Monitor);
        var scale = screen is { Effective.Enabled: true } ? screen.Effective.Scale : 1;
        // A negative general:float_gaps side takes general:gaps_out, not the rule's, as in the pinned release.
        var options = _options with { ClientBorder = _clientBorder, FloatGaps = _options.EffectiveFloatGaps };
        if (rule is not null)
        {
            if (rule.GapsIn is EdgeInsets gapsIn)
                options = options with { GapsIn = gapsIn };
            if (rule.GapsOut is EdgeInsets gapsOut)
                options = options with { GapsOut = gapsOut };
            if (rule.FloatGaps is EdgeInsets floatGaps)
                options = options with { FloatGaps = floatGaps };

            var border = options.BorderSize;
            if (rule.BorderSize is int size)
                border = Math.Clamp(size, 0, 20);
            if (rule.Border == false || rule.Decorate == false)
                border = 0;
            options = options with { BorderSize = border };
            foreach (var pair in rule.LayoutOpts)
                LayoutOptions.TryOverrideDwindle(options, pair.Key, pair.Value, out options);
        }

        var scaled = options.ScaledBy(scale);
        if (screen is not { Effective: { Enabled: true } monitor })
            return scaled;

        return scaled with
        {
            ReservedLeft = Math.Max(0, monitor.WorkArea.X - monitor.X),
            ReservedRight = Math.Max(0, monitor.X + monitor.Width - (monitor.WorkArea.X + monitor.WorkArea.Width)),
        };
    }

    private LiveMonitor? BoundMonitor(WorkspaceId id)
    {
        var desk = FindWorkspace(id);
        return BoundMonitor(desk is null
            ? WorkspaceRules.For(_workspaceRules, id, null, null)
            : RuleFor(desk));
    }

    private LiveMonitor? BoundMonitor(WorkspaceRule? rule)
    {
        if (rule is null || rule.Monitor.Length == 0)
            return null;

        foreach (var screen in _monitors)
        {
            if (screen.Effective.Enabled && MonitorRules.Matches(screen.Effective, rule.Monitor))
                return screen;
        }

        return null;
    }

    private void NoteCreated(LiveWorkspace desk)
    {
        var command = RuleFor(desk)?.OnCreatedEmpty;
        if (string.IsNullOrEmpty(command))
            return;

        var parsed = LaunchParser.Parse(command, allowsElevation: ElevationTrusted, raw: false);
        // The pinned release starts on-created-empty with the new Workspace as its initial workspace.
        if (parsed.Launch is Launch launch)
            _born.Enqueue((WithEnvironment(launch) ?? launch) with { InitialWorkspace = desk.Id });
    }

    // Every workspace created since the last outcome runs its command, so all of them leave together.
    private Launch[] TakeBorn()
    {
        if (_born.Count == 0)
            return [];

        var all = _born.ToArray();
        _born.Clear();
        return all;
    }
}
