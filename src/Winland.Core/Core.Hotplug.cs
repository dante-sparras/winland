namespace Winland;

public sealed partial class Core
{
    private Outcome SetMonitors(IReadOnlyList<Monitor> reported)
    {
        ArgumentNullException.ThrowIfNull(reported);
        var rules = MonitorRules.From(_config);
        var arrangement = MonitorRules.Arrange(reported, rules);
        var previous = _monitors.ToArray();
        var next = new List<LiveMonitor>(reported.Count);
        for (var i = 0; i < reported.Count; i++)
        {
            var monitor = reported[i];
            ArgumentNullException.ThrowIfNull(monitor);
            var effective = arrangement.Monitors[i];
            var screen = FindNamed(previous, monitor.Name) ?? FindNamed(next, monitor.Name);
            if (screen is null)
                screen = new LiveMonitor(NextMonitorId(previous, next), monitor, effective);
            else
            {
                screen.Reported = monitor;
                screen.Effective = effective;
            }

            next.Add(screen);
        }

        _monitors.Clear();
        _monitors.AddRange(next);

        var enabled = EnabledMonitors();
        LiveMonitor? home = null;
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

    private void Relocate(LiveMonitor? home, List<LiveMonitor> enabled)
    {
        var displaced = new List<LiveWorkspace>();
        foreach (var desk in _workspaces)
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
        foreach (var desk in _workspaces)
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

        var target = WorkspaceFor(home.Active ?? NextFree(home.Id), home.Id);
        home.Active = target.Id;
        foreach (var desk in displaced)
        {
            if (desk.Id == target.Id)
            {
                desk.Monitor = home.Id;
                continue;
            }

            target.Absorb(desk, home.Effective.WorkArea);
            desk.Monitor = home.Id;
        }

        RehomeSpecials(home, enabled);
    }

    private void AssignActives(List<LiveMonitor> enabled)
    {
        var claimed = new HashSet<WorkspaceId>();
        foreach (var screen in enabled)
        {
            if (screen.Active is WorkspaceId active && OnMonitor(screen, active) is not null && claimed.Add(active))
                continue;

            if (TryDefault(screen, out var preferred) && claimed.Add(preferred))
            {
                var chosen = WorkspaceFor(preferred, screen.Id);
                if (chosen.Monitor == screen.Id)
                {
                    screen.Active = chosen.Id;
                    continue;
                }

                claimed.Remove(preferred);
            }

            LiveWorkspace? own = null;
            foreach (var desk in _workspaces)
            {
            if (desk.Monitor != screen.Id || desk.Id.IsSpecial || !claimed.Add(desk.Id))
                continue;

                own = desk;
                break;
            }

            own ??= WorkspaceFor(NextFree(screen.Id), screen.Id);
            screen.Active = own.Id;
            claimed.Add(own.Id);
        }

        foreach (var screen in _monitors)
        {
            if (!screen.Effective.Enabled)
                screen.Active ??= WorkspaceId.Initial;
        }
    }

    private void RefitAll()
    {
        foreach (var desk in _workspaces)
            Refit(desk);
    }

    private void Refit(LiveWorkspace desk)
    {
        Stamp(desk);
        var screen = MonitorById(desk.Monitor);
        if (screen is not { Effective.Enabled: true })
            return;

        desk.Layout.Apply(_master);
        desk.Layout.Apply(desk.Options);
        desk.Layout.Fit(screen.Effective.WorkArea);
    }

    private void EnsureFocus(LiveMonitor? home)
    {
        foreach (var screen in _monitors)
        {
            if (screen.Focused && screen.Effective.Enabled)
                return;
        }

        Focus(home?.Id ?? MonitorId.Primary);
    }

    private void Focus(MonitorId id)
    {
        var found = false;
        foreach (var screen in _monitors)
        {
            var focused = screen.Id == id && screen.Effective.Enabled;
            screen.Focused = focused;
            if (focused)
                found = true;
        }

        if (!found)
        {
            foreach (var screen in _monitors)
            {
                if (!screen.Effective.Enabled)
                    continue;

                screen.Focused = true;
                break;
            }
        }

        Remember(ActiveOn(FocusedId()), FocusedId());
    }

    private void SyncActive()
    {
        foreach (var screen in _monitors)
        {
            if (screen.Active is WorkspaceId active)
                _activeOn[screen.Id] = active;
        }
    }

    private LiveMonitor? MonitorFor(WindowAppeared appeared)
    {
        if (_monitors.Count == 0)
            return null;

        var byArea = Match(screen => screen.Reported.WorkArea == appeared.WorkArea);
        if (byArea is not null)
            return byArea;

        if (appeared.Pointer is { } pointer)
        {
            var byPointer = Match(screen => screen.Covers(pointer));
            if (byPointer is not null)
                return byPointer;
        }

        if (appeared.Bounds is { Width: > 0, Height: > 0 } bounds)
        {
            var center = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
            var byBounds = Match(screen => screen.Covers(center));
            if (byBounds is not null)
                return byBounds;
        }

        return Match(_ => true);
    }

    private LiveMonitor? Match(Func<LiveMonitor, bool> predicate)
    {
        foreach (var screen in _monitors)
        {
            if (screen.Effective.Enabled && predicate(screen))
                return screen;
        }

        return null;
    }

    private static LiveMonitor? FindNamed(IReadOnlyList<LiveMonitor> screens, string name)
    {
        foreach (var screen in screens)
        {
            if (string.Equals(screen.Reported.Name, name, StringComparison.OrdinalIgnoreCase))
                return screen;
        }

        return null;
    }

    private static bool HeldBy(List<LiveMonitor> enabled, MonitorId id)
    {
        foreach (var screen in enabled)
        {
            if (screen.Id == id)
                return true;
        }

        return false;
    }

    private List<LiveMonitor> EnabledMonitors()
    {
        var enabled = new List<LiveMonitor>();
        foreach (var screen in _monitors)
        {
            if (screen.Effective.Enabled)
                enabled.Add(screen);
        }

        return enabled;
    }

    private static MonitorId NextMonitorId(IReadOnlyList<LiveMonitor> previous, List<LiveMonitor> next)
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

    private Monitor[] Reported()
    {
        var monitors = new Monitor[_monitors.Count];
        for (var i = 0; i < _monitors.Count; i++)
            monitors[i] = _monitors[i].Reported;

        return monitors;
    }
}
