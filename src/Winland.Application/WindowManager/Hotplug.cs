namespace Winland.Application;

/// <summary>Monitors that are plugged in or out: which Workspaces go where, and which come back.</summary>
public sealed partial class WindowManager
{
    private readonly MonitorIdentity _identity = new();

    private Outcome SetMonitors(IReadOnlyList<Monitor> reported)
    {
        ArgumentNullException.ThrowIfNull(reported);
        var identified = _identity.Number(reported);
        var rules = MonitorRules.From(_config.Document);
        var arranged = MonitorRules.Arrange(identified, rules);
        var previous = _monitors.ToArray();
        var wasEnabled = new List<LiveMonitor>(previous.Length);
        foreach (var screen in previous)
        {
            if (screen.Effective.Enabled)
                wasEnabled.Add(screen);
        }

        var next = new List<LiveMonitor>(identified.Length);
        for (var i = 0; i < identified.Length; i++)
        {
            var monitor = identified[i];
            var effective = arranged[i];
            var screen = FindTracked(previous, monitor) ?? FindTracked(next, monitor);
            if (screen is null)
                screen = new LiveMonitor(NextMonitorId(previous, next), monitor, effective);
            else
            {
                screen.Reported = monitor;
                screen.Arranged = effective;
            }

            next.Add(screen);
        }

        // While no Monitor is enabled Winland keeps the Workspaces on a FALLBACK Monitor, and gives them back
        // when one comes back.
        if (next.Count > 0 && !next.Exists(screen => screen.Effective.Enabled))
            next.Add(FindNamed(previous, FallbackName) ?? new LiveMonitor(NextMonitorId(previous, next), Fallback, Fallback));

        _monitors.Clear();
        _monitors.AddRange(next);
        _workspaces.Shown.SetLive(next.Select(screen => screen.Id), next.Where(screen => screen.Effective.Enabled).Select(screen => screen.Id));

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
        Relocate(wasEnabled, enabled, home);
        EnsurePersistent();
        AssignActives(enabled);
        // An empty Workspace a Monitor took from one that left is hidden there, so it goes,
        // and the Monitor that comes back does not show it again.
        DropEmpty();
        RefitAll();
        EnsureFocus(home);
        var refocus = RefocusShown();
        return View(ShownPlacements(), ShownWindows(), HiddenWindows(), focus: refocus);
    }

    // A Monitor that comes back takes its Guests under their old numbers before a Monitor that leaves
    // puts its Workspaces on the one that remains. Empty Workspaces of a Monitor that leaves are removed.
    private void Relocate(List<LiveMonitor> wasEnabled, List<LiveMonitor> enabled, LiveMonitor? host)
    {
        foreach (var screen in enabled)
        {
            if (!HeldBy(wasEnabled, screen.Id))
                Return(screen);
        }

        foreach (var screen in wasEnabled)
        {
            if (!HeldBy(enabled, screen.Id))
                Depart(screen, host);
        }

        RehomeSpecials(host, enabled);
    }

    // A Monitor that goes away or comes back can hide the focused Client with its Workspace. The focus then
    // goes to the Client the focused Monitor shows, so it does not stay on a window out of sight.
    private WindowId? RefocusShown()
    {
        if (_focus.Focused is not WindowId current || IsShownClient(current))
            return null;

        var desk = CurrentWorkspace();
        var next = _focus.Remembered(desk);
        TakeFocus(desk, next);
        return next;
    }

    // A Monitor is known again by its identity, or by its Windows name when it has no model and serial.
    private void Depart(LiveMonitor screen, LiveMonitor? host) =>
        Readdressed(_workspaces.Depart(screen.Id, ReturnKey(screen.Reported), host?.Id));

    private void Return(LiveMonitor screen)
    {
        Readdressed(_workspaces.Return(screen.Id, ReturnKey(screen.Reported), out var shownNumber));
        if (shownNumber is not long number)
            return;

        var shown = WorkspaceFor(WorkspaceId.FromNumber(number, screen.Id), screen.Id);
        _workspaces.Shown.Show(screen.Id, shown.Id);
    }

    private void AssignActives(List<LiveMonitor> enabled)
    {
        var claimed = new HashSet<WorkspaceId>();
        foreach (var screen in enabled)
        {
            if (_workspaces.Shown.Shown(screen.Id) is WorkspaceId active && _workspaces.On(screen.Id, active) is not null && claimed.Add(active))
                continue;

            if (TryDefault(screen, out var preferred) && claimed.Add(preferred))
            {
                var chosen = WorkspaceFor(preferred, screen.Id);
                if (chosen.Monitor == screen.Id)
                {
                    _workspaces.Shown.Show(screen.Id, chosen.Id);
                    continue;
                }

                claimed.Remove(preferred);
            }

            // A Workspace that holds Clients is shown before an empty one. Workspace 1 is shown
            // when it exists and opened when it is free, before a higher empty Workspace. When
            // 1–10 are all taken, an empty one of those is shown before a number above 10 is made.
            LiveWorkspace? populated = null;
            LiveWorkspace? empty = null;
            long emptyNumber = long.MaxValue;
            foreach (var desk in _workspaces)
            {
                if (desk.Monitor != screen.Id || desk.Id.IsSpecial || claimed.Contains(desk.Id))
                    continue;

                if (desk.Layout.Clients.Count > 0)
                {
                    populated = desk;
                    break;
                }

                if (!desk.Id.TryGetNumber(out var number) || number >= emptyNumber)
                    continue;

                empty = desk;
                emptyNumber = number;
            }

            var one = WorkspaceId.FromNumber(1, screen.Id);
            LiveWorkspace own;
            if (populated is not null)
                own = populated;
            else if (_workspaces.Find(one) is LiveWorkspace first && !claimed.Contains(first.Id))
                own = first;
            else if (_workspaces.FirstFree(screen.Id) == 1)
                own = WorkspaceFor(one, screen.Id);
            else
                own = empty ?? WorkspaceFor(_workspaces.NextFree(screen.Id), screen.Id);

            _workspaces.Shown.Show(screen.Id, own.Id);
            claimed.Add(own.Id);
        }

        foreach (var screen in _monitors)
        {
            if (!screen.Effective.Enabled && _workspaces.Shown.Shown(screen.Id) is null)
                _workspaces.Shown.Show(screen.Id, WorkspaceId.Initial);
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

        Remember(_workspaces.Shown.ActiveOn(FocusedId()), FocusedId());
    }

    private LiveMonitor? MonitorFor(WindowAppeared appeared)
    {
        if (_monitors.Count == 0)
            return null;

        // A window that opens while Winland runs opens on the focused Monitor, wherever its program put it, and a
        // window with an owner on its owner's. One that was open when Winland started stays where it is.
        if (appeared.Opened)
        {
            var home = appeared.Owner is WindowId owner && WorkspaceContaining(owner) is LiveWorkspace desk ? desk.Monitor : FocusedId();
            if (Match(screen => screen.Id == home) is LiveMonitor opened)
                return opened;
        }

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

    private static string ReturnKey(Monitor monitor) =>
        MonitorIdentity.Basis(monitor).Length > 0 ? monitor.Description : monitor.Name;

    private static LiveMonitor? FindTracked(IReadOnlyList<LiveMonitor> screens, Monitor monitor)
    {
        if (MonitorIdentity.Basis(monitor).Length > 0)
        {
            foreach (var screen in screens)
            {
                if (MonitorIdentity.Basis(screen.Reported).Length == 0)
                    continue;

                if (string.Equals(screen.Reported.Description, monitor.Description, StringComparison.Ordinal))
                    return screen;
            }

            return null;
        }

        return FindNamed(screens, monitor.Name);
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

    private const string FallbackName = "FALLBACK";

    private static readonly Monitor Fallback = new(FallbackName, "", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1080));

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
