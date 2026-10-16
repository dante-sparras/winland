namespace Winland;

public sealed partial class Core
{
    private LiveWorkspace WorkspaceFor(WorkspaceId id, MonitorId? monitor = null)
    {
        var existing = FindWorkspace(id);
        if (existing is not null)
            return existing;

        var hinted = WorkspaceRules.For(_workspaceRules, id, null, null, 0, 0, 0);
        var bound = BoundMonitor(hinted);
        var owner = bound?.Id ?? monitor ?? FocusedId();
        var layout = LayoutFactory.Create(_config);
        layout.Configure(_scrolling);
        var desk = new LiveWorkspace(id, owner, layout);
        _workspaces.Add(desk);
        Stamp(desk);
        var settled = BoundMonitor(RuleFor(desk));
        if (settled is not null && desk.Monitor != settled.Id)
            desk.Monitor = settled.Id;

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

            WorkspaceFor(id, BoundMonitor(rule)?.Id);
        }

        foreach (var desk in _workspaces)
            desk.Persistent = RuleFor(desk)?.Persistent == true;
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
        desk.Persistent = rule?.Persistent == true;
        desk.DisplayName = rule?.DefaultName;
        desk.Options = OptionsFor(desk, rule);
    }

    private WorkspaceRule? RuleFor(LiveWorkspace desk)
    {
        var screen = MonitorById(desk.Monitor);
        var counts = desk.ClientCounts();
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

    private LayoutOptions OptionsFor(LiveWorkspace desk, WorkspaceRule? rule)
    {
        var screen = MonitorById(desk.Monitor);
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

        return options.ScaledBy(scale);
    }

    private LiveMonitor? BoundMonitor(WorkspaceId id)
    {
        var desk = FindWorkspace(id);
        return BoundMonitor(desk is null
            ? WorkspaceRules.For(_workspaceRules, id, null, null, 0, 0, 0)
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
        if (parsed.Launch is Launch launch)
            _born.Enqueue(WithEnvironment(launch) ?? launch);
    }

    private Launch? TakeBorn() => _born.Count == 0 ? null : _born.Dequeue();
}
