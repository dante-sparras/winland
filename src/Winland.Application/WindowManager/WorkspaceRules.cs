namespace Winland.Application;

/// <summary>Creating Workspaces, with the options their <c>workspace</c> rules give them.</summary>
public sealed partial class WindowManager
{
    private LiveWorkspace WorkspaceFor(WorkspaceId id, MonitorId? monitor = null)
    {
        if (!id.IsSpecial && monitor is MonitorId place)
            id = new WorkspaceId(id.Name, place.Value);

        var existing = _workspaces.Find(id);
        if (existing is not null)
            return existing;

        var owner = id.IsSpecial ? monitor ?? FocusedId() : new MonitorId(id.Monitor);
        var hinted = WorkspaceRules.For(_config.WorkspaceRules, id, MonitorById(owner)?.Effective);
        var layout = LayoutFactory.Create(_config.Document, hinted?.Layout);
        layout.Configure(_config.Scrolling);
        RankFocus(layout);
        var desk = new LiveWorkspace(id, owner, layout);
        _workspaces.Add(desk);
        Stamp(desk);

        // A rule that matches this number picks the Layout once the Workspace exists.
        MatchLayout(desk);
        layout = desk.Layout;
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

        return desk;
    }

    private bool TryDefault(LiveMonitor screen, out WorkspaceId id)
    {
        id = default;
        var text = WorkspaceRules.DefaultWorkspace(_config.WorkspaceRules, screen.Effective);
        return text is not null && WorkspaceRules.TryConcrete(text, screen.Id, out id);
    }

    private void EnsurePersistent()
    {
        foreach (var rule in _config.WorkspaceRules)
        {
            if (rule.Persistent != true)
                continue;

            foreach (var screen in EnabledMonitors())
            {
                if (rule.Monitor.Length > 0 && !MonitorRules.Matches(screen.Effective, rule.Monitor))
                    continue;

                if (!WorkspaceRules.TryConcrete(rule.WorkspaceString, screen.Id, out var id) || _workspaces.Find(id) is not null)
                    continue;

                WorkspaceFor(id, screen.Id);
            }
        }

        foreach (var desk in _workspaces)
            desk.Persistent = PersistentBy(desk, RuleFor(desk));
    }

    private void Stamp(LiveWorkspace desk)
    {
        var rule = RuleFor(desk);
        desk.Persistent = PersistentBy(desk, rule);
        desk.Options = OptionsFor(desk, rule);
    }

    // A persistent rule keeps a normal Workspace. A special workspace is never kept.
    private static bool PersistentBy(LiveWorkspace desk, WorkspaceRule? rule) =>
        rule?.Persistent == true && !desk.Id.IsSpecial;

    private WorkspaceRule? RuleFor(LiveWorkspace desk) =>
        WorkspaceRules.For(_config.WorkspaceRules, desk.Id, MonitorById(desk.Monitor)?.Effective);

    private LayoutOptions OptionsFor(LiveWorkspace desk, WorkspaceRule? rule)
    {
        var screen = MonitorById(desk.Monitor);
        var scale = screen is { Effective.Enabled: true } ? screen.Effective.Scale : 1;
        var options = _config.Layout with { ClientBorder = _clientBorder };
        if (rule is not null)
        {
            if (rule.GapsIn is EdgeInsets gapsIn)
                options = options with { GapsIn = gapsIn };
            if (rule.GapsOut is EdgeInsets gapsOut)
                options = options with { GapsOut = gapsOut };
        }

        return options.ScaledBy(scale);
    }
}
