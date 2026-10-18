namespace Winland;

public sealed partial class Core
{
    // The Monitor each Workspace left when its Monitor went away, and the Workspace each Monitor
    // showed then, by Monitor name.
    private readonly Dictionary<WorkspaceId, string> _returnMonitors = [];
    private readonly Dictionary<string, WorkspaceId> _rememberedActive = new(StringComparer.OrdinalIgnoreCase);

    private Outcome SetMonitors(IReadOnlyList<Monitor> reported)
    {
        ArgumentNullException.ThrowIfNull(reported);
        var rules = MonitorRules.From(_config);
        var arrangement = MonitorRules.Arrange(reported, rules);
        var previous = _monitors.ToArray();
        var wasEnabled = new List<LiveMonitor>(previous.Length);
        foreach (var screen in previous)
        {
            if (screen.Effective.Enabled)
                wasEnabled.Add(screen);
        }

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
        Relocate(wasEnabled, enabled);
        EnsurePersistent();
        PlaceBound();
        AssignActives(enabled);
        // An empty Workspace a Monitor took from one that left is hidden there, so it goes, as in the pinned release,
        // and the Monitor that comes back does not show it again.
        DropEmpty();
        RefitAll();
        EnsureFocus(home);
        SyncActive();
        var refocus = RefocusShown();
        var outcome = View(ShownPlacements(), ShownWindows(), HiddenWindows(), focus: refocus);
        return arrangement.Orientations.Length == 0
            ? outcome
            : outcome with { Orientations = arrangement.Orientations };
    }

    // As in the pinned release, each Workspace of a Monitor that goes away moves intact onto the first
    // Monitor that remains, where it is hidden unless that Monitor shows nothing. When the Monitor
    // comes back, its Workspaces move back and it shows the Workspace it showed when it left.
    private void Relocate(List<LiveMonitor> wasEnabled, List<LiveMonitor> enabled)
    {
        foreach (var screen in wasEnabled)
        {
            if (!HeldBy(enabled, screen.Id))
                NoteLeaving(screen);
        }

        var backup = enabled.Count > 0 ? enabled[0] : null;
        if (backup is not null)
        {
            foreach (var desk in _workspaces)
            {
                if (!desk.Id.IsSpecial && !HeldBy(enabled, desk.Monitor))
                    desk.Monitor = backup.Id;
            }
        }

        RehomeSpecials(backup, enabled);
        foreach (var screen in enabled)
        {
            if (!HeldBy(wasEnabled, screen.Id))
                Return(screen);
        }
    }

    // A Monitor that goes away or comes back can hide the focused Client with its Workspace. The focus then
    // goes to the Client the focused Monitor shows, so it does not stay on a window out of sight.
    private WindowId? RefocusShown()
    {
        if (_active is not WindowId current || IsShownClient(current))
            return null;

        var desk = CurrentWorkspace();
        var next = desk.Remembered();
        TakeFocus(desk, next);
        return next;
    }

    private void NoteLeaving(LiveMonitor screen)
    {
        var name = screen.Reported.Name;
        if (screen.Active is WorkspaceId active)
            _rememberedActive[name] = active;

        foreach (var desk in _workspaces)
        {
            if (desk.Monitor == screen.Id)
                _returnMonitors.TryAdd(desk.Id, name);
        }
    }

    private void Return(LiveMonitor screen)
    {
        var name = screen.Reported.Name;
        foreach (var desk in _workspaces.ToArray())
        {
            if (!_returnMonitors.TryGetValue(desk.Id, out var owner) || !string.Equals(owner, name, StringComparison.OrdinalIgnoreCase))
                continue;

            _returnMonitors.Remove(desk.Id);
            MoveBack(desk, screen);
        }

        if (!_rememberedActive.Remove(name, out var remembered) || FindWorkspace(remembered) is not LiveWorkspace shown || shown.Id.IsSpecial)
            return;

        MoveBack(shown, screen);
        screen.Active = shown.Id;
    }

    private void MoveBack(LiveWorkspace desk, LiveMonitor screen)
    {
        if (desk.Monitor == screen.Id)
            return;

        var owner = MonitorById(desk.Monitor);
        if (desk.Id.IsSpecial)
        {
            var shown = owner is not null && SpecialOn(owner.Id) == desk.Id;
            if (shown)
                SetSpecial(owner!.Id, null);

            desk.Monitor = screen.Id;
            if (shown && SpecialOn(screen.Id) is null)
                SetSpecial(screen.Id, desk.Id);

            return;
        }

        if (owner is { Effective.Enabled: true } && owner.Active == desk.Id)
            Plug(owner, desk);

        desk.Monitor = screen.Id;
    }

    private void ForgetReturn(WorkspaceId workspace)
    {
        _returnMonitors.Remove(workspace);
        foreach (var (name, active) in _rememberedActive.ToArray())
        {
            if (active == workspace)
                _rememberedActive.Remove(name);
        }
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

            // An empty persistent Workspace is not what a Monitor shows when it comes, as in the pinned release, which
            // makes those after the Monitor picks one.
            LiveWorkspace? own = null;
            foreach (var desk in _workspaces)
            {
            if (desk.Monitor != screen.Id || desk.Id.IsSpecial || (desk.Persistent && desk.Layout.Clients.Count == 0) || !claimed.Add(desk.Id))
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
