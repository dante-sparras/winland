namespace Winland.Application;

/// <summary>Creating Workspaces, with the options their <c>workspace</c> rules give them.</summary>
public sealed partial class WindowManager
{
    private LiveWorkspace WorkspaceFor(WorkspaceId id, MonitorId? monitor = null)
    {
        if (!id.IsSpecial && monitor is MonitorId place)
            id = new WorkspaceId(id.Name, place.Value);

        var existing = FindWorkspace(id);
        if (existing is not null)
            return existing;

        var owner = id.IsSpecial ? monitor ?? FocusedId() : new MonitorId(id.Monitor);
        var hinted = WorkspaceRules.For(_config.WorkspaceRules, id, null, MonitorById(owner)?.Effective);
        var layout = LayoutFactory.Create(_config.Document, hinted?.Layout);
        layout.Configure(_config.Scrolling);
        RankFocus(layout);
        var desk = new LiveWorkspace(id, owner, layout);
        _workspaces.Add(desk);
        Stamp(desk);

        // A rule that matches this number picks the Layout once the Workspace exists.
        MatchLayout(desk);
        layout = desk.Layout;
        layout.Apply(_config.Master);
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

                if (!WorkspaceRules.TryConcrete(rule.WorkspaceString, screen.Id, out var id) || FindWorkspace(id) is not null)
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
        desk.DisplayName = rule?.DefaultName;
        desk.Options = OptionsFor(desk, rule);
    }

    // A persistent rule keeps a normal Workspace. A special workspace is never kept.
    private static bool PersistentBy(LiveWorkspace desk, WorkspaceRule? rule) =>
        rule?.Persistent == true && !desk.Id.IsSpecial;

    private WorkspaceRule? RuleFor(LiveWorkspace desk)
    {
        var screen = MonitorById(desk.Monitor);
        int Count(WindowCount wanted) => desk.CountWindows(wanted, _pinned);
        var fullscreen = desk.CoveringMode();
        var first = WorkspaceRules.For(
            _config.WorkspaceRules,
            desk.Id,
            desk.DisplayName,
            screen?.Effective,
            Count,
            fullscreen);
        var name = first?.DefaultName ?? desk.DisplayName;
        if (name == desk.DisplayName)
            return first;

        return WorkspaceRules.For(
            _config.WorkspaceRules,
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
        // A negative general:float_gaps side takes general:gaps_out, not the rule's.
        var options = _config.Layout with { ClientBorder = _clientBorder, FloatGaps = _config.Layout.EffectiveFloatGaps };
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

    private void NoteCreated(LiveWorkspace desk)
    {
        var command = RuleFor(desk)?.OnCreatedEmpty;
        if (string.IsNullOrEmpty(command))
            return;

        var parsed = LaunchParser.Parse(command, raw: false);
        // Winland starts on-created-empty with the new Workspace as its initial workspace.
        if (parsed.Launch is Launch launch)
            _born.Enqueue(launch with { InitialWorkspace = desk.Id });
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
