namespace Winland.Application;

/// <summary>Directional focus and moves, <c>movefocus</c> and <c>movewindow</c>, and the dispatchers that focus and move Monitors and their Workspaces.</summary>
public sealed partial class WindowManager
{
    // Where a switch to a Workspace on another Monitor puts the cursor when no cursor option warps it elsewhere.
    private WarpTo? _crossMonitorWarp;

    private void RankFocus(Layout layout) => layout.RankFocus(_config.BindOptions.FocusPreferredMethod == 0 ? Recency : null);

    // How recently a window was focused: the newest is highest. One never focused is 0, below every focused one,
    // as Winland puts a window that opens at the old end of its history.
    private int Recency(WindowId window)
    {
        var index = _focusHistory.IndexOf(window);
        return index < 0 ? 0 : _focusHistory.Count - index;
    }

    private Outcome MoveFocusDirection(string arguments)
    {
        const string name = "movefocus";
        var desk = FocusedWorkspace();
        var covered = desk.Covered();
        var active = ActiveWindow();
        if (!MonitorSelector.TryDirection(FirstToken(arguments), out var direction))
            return View([], dispatcher: name);

        if (active is WindowId grouped && CycleGroupFirst(desk, grouped, arguments, name) is Outcome cycled)
            return cycled;

        if (active is WindowId fullscreen
            && _config.Session.MoveFocusCyclesFullscreen
            && desk.ClientOf(fullscreen) is { Fullscreen: not FullscreenMode.None, LayoutFullscreen: false })
        {
            var previous = direction is MonitorDirection.Left or MonitorDirection.Up;
            return CycleFrom(desk, fullscreen, previous) is WindowId next
                ? FocusClient(next, name)
                : View([], dispatcher: name);
        }

        // A Workspace with a fullscreen or maximized Client offers no window in a direction, as in the pinned
        // release, so focus moves on to the next Monitor. One its Layout holds fullscreen lets focus move on the
        // Workspace when binds:movefocus_cycles_fullscreen is on and misc:on_focus_under_fullscreen is not 0.
        if (active is WindowId current && !HasFullscreen(desk))
        {
            var focus = desk.Layout.MoveFocus(current, arguments);
            if (focus is WindowId chosen)
            {
                if (HasNoFocus(chosen))
                    return View([], dispatcher: name);

                _active = chosen;
                desk.Focus = chosen;
                desk.Layout.NoteFocus(chosen);
                return Present(desk, covered, [], dispatcher: name, focus: chosen);
            }
        }

        if (!_config.BindOptions.MonitorFallback || MonitorHolding(desk) is not LiveMonitor origin || TargetOf(origin) is not MonitorTarget from)
            return NothingToFocus(name, direction);

        if (MonitorSelector.InDirection(Targets(), from, direction) is not MonitorTarget neighbor
            || MonitorOf(neighbor) is not LiveMonitor destination)
            return NothingToFocus(name, direction);

        WindowId? adjacent = null;
        if (active is WindowId fromWindow && desk.Layout.TryBounds(fromWindow, out var box))
            adjacent = AdjacentOn(destination, Ideal(desk, fromWindow, box), direction);

        return FocusOnto(destination, name, adjacent);
    }

    // general:no_focus_fallback makes a movefocus that finds nothing answer an error.
    private Outcome NothingToFocus(string name, MonitorDirection direction) =>
        _config.Session.NoFocusFallback
            ? Rejected(name, "error: Nothing to focus to in direction " + direction.ToString().ToLowerInvariant())
            : View([], dispatcher: name);

    private bool HasFullscreen(LiveWorkspace desk)
    {
        foreach (var client in desk.Layout.Clients)
        {
            if (!desk.IsCovering(client))
                continue;

            if (!client.LayoutFullscreen || !_config.Session.MoveFocusCyclesFullscreen || _config.Session.OnFocusUnderFullscreen == 0)
                return true;
        }

        return false;
    }

    // The window after or before this one on its Workspace in the order they opened, wrapping around, as the
    // Winland cycles. A hidden Group member, a minimized window and one with nofocus are passed over.
    // The next window on the Workspace in the order the windows opened, as Winland's window cycle walks
    // them. floating, when given, takes only floating windows or only tiles.
    private WindowId? CycleFrom(LiveWorkspace desk, WindowId current, bool previous, bool? floating = null)
    {
        var clients = desk.Layout.Clients;
        var start = -1;
        for (var i = 0; i < clients.Count; i++)
        {
            if (clients[i].Window == current)
                start = i;
        }

        if (start < 0)
            return null;

        for (var step = 1; step < clients.Count; step++)
        {
            var index = previous
                ? (start - step + clients.Count) % clients.Count
                : (start + step) % clients.Count;
            var client = clients[index];
            if (client.Minimized || HasNoFocus(client.Window) || desk.GroupOf(client.Window) is WindowGroup group && group.Shown != client.Window)
                continue;

            if (floating is bool wanted && client.Floating != wanted)
                continue;

            return client.Window;
        }

        return null;
    }

    private Outcome MoveWindowDirection(string arguments)
    {
        const string name = "movewindow";
        if (TryMonitorArgument(arguments, out var monitorText, out var silent))
        {
            if (MonitorOf(MonitorSelector.Resolve(monitorText, Targets(), TargetOf(FocusedMonitor()))) is not LiveMonitor chosen)
                return Rejected(name, "error: monitor not found");

            return MoveActiveOnto(chosen, silent, name);
        }

        var active = ActiveWindow();
        var desk = active is WindowId owned && WorkspaceContaining(owned) is LiveWorkspace own ? own : FocusedWorkspace();
        var covered = desk.Covered();
        if (active is WindowId shown && Find(shown) is { Fullscreen: not FullscreenMode.None })
            return Rejected(name, "error: can't move fullscreen window");

        if (active is not WindowId window || !MonitorSelector.TryDirection(FirstToken(arguments), out var direction))
        {
            IReadOnlyList<Placement> stayed = active is WindowId id ? desk.Layout.MoveWindow(id, arguments) : [];
            return Present(desk, covered, stayed, dispatcher: name);
        }

        // A direction onto the neighbouring Monitor follows only for the focused window. Another window is assigned
        // there and the focus stays.
        var stay = !DirectionFollows();
        if (_config.BindOptions.MonitorFallback
            && MonitorHolding(desk) is LiveMonitor origin
            && desk.Layout.TryBounds(window, out var box))
        {
            // the focal point is taken from the window's ideal box, which reaches the
            // Monitor's edge when the tile touches the work area's.
            // The window goes in at that point there, as Winland assigns it to the other Workspace with it.
            var focal = MonitorSelector.Focal(Ideal(desk, window, box), direction);
            var hit = MonitorSelector.At(Targets(), focal);
            if (hit is MonitorTarget other && other.Id != origin.Id && MonitorOf(other) is LiveMonitor across)
                return MoveActiveOnto(across, stay, name, focal);
        }

        // In master, swapping another window with the focused one leaves the focus on the tile the focused window held.
        Rectangle? held = null;
        if (MasterSlotFollows(desk) && _active is WindowId focused && desk.Layout.TryBounds(focused, out var slot))
            held = slot;

        var placements = desk.Layout.MoveWindow(window, arguments);
        if (_config.BindOptions.MonitorFallback
            && placements.Count == 0
            && desk.Layout.FollowsMonitorWhenUnmoved
            && MonitorHolding(desk) is LiveMonitor home
            && TargetOf(home) is MonitorTarget from
            && MonitorSelector.InDirection(Targets(), from, direction) is MonitorTarget neighbor
            && MonitorOf(neighbor) is LiveMonitor next)
            return MoveActiveOnto(next, stay, name);

        var laid = Present(desk, covered, placements, dispatcher: name);
        if (held is Rectangle old && WindowIn(desk, placements, old) == window)
            return laid.Then(FocusClient(window, name));

        return laid;
    }

    // A named window is followed when it is the focused one, or when the dispatcher names no window.
    private bool DirectionFollows() => _actingOn is not WindowId acting || _active == acting;

    private bool MasterSlotFollows(LiveWorkspace desk) =>
        _actingOn is WindowId acting
        && _active is WindowId focused
        && acting != focused
        && LayoutFactory.NameOf(desk.Layout) == "master"
        && WorkspaceContaining(focused) is LiveWorkspace home
        && home.Id == desk.Id;

    private static WindowId? WindowIn(LiveWorkspace desk, IReadOnlyList<Placement> placements, Rectangle slot)
    {
        foreach (var placement in placements)
        {
            if (placement.Bounds != slot)
                continue;

            foreach (var client in desk.Layout.Clients)
            {
                if (client.Id == placement.Client)
                    return client.Window;
            }
        }

        return null;
    }

    private Outcome FocusMonitorDispatch(string arguments)
    {
        const string name = "focusmonitor";
        if (MonitorOf(MonitorSelector.Resolve(arguments, Targets(), TargetOf(FocusedMonitor()))) is not LiveMonitor screen)
            return Rejected(name, "error: monitor not found");

        return FocusOnto(screen, name, preferred: null);
    }

    private Outcome MoveCurrentWorkspace(string arguments)
    {
        const string name = "movecurrentworkspacetomonitor";
        if (MonitorOf(MonitorSelector.Resolve(arguments, Targets(), TargetOf(FocusedMonitor()))) is not LiveMonitor destination)
            return Rejected(name, "error: monitor not found");

        if (FindWorkspace(ActiveId()) is not LiveWorkspace desk)
            return Rejected(name, "error: invalid workspace");

        return MoveWorkspaceTo(desk, destination, name);
    }

    private Outcome MoveWorkspace(string arguments)
    {
        const string name = "moveworkspacetomonitor";
        if (!SplitPair(arguments, out var workspaceText, out var monitorText))
            return Rejected(name, "error: expected workspace and monitor");

        if (!WorkspaceAddress.TryResolve(workspaceText, Context(), out var id))
            return Rejected(name, "error: invalid workspace");

        if (FindWorkspace(id) is not LiveWorkspace desk)
            return Rejected(name, "error: workspace not found");

        if (MonitorOf(MonitorSelector.Resolve(monitorText, Targets(), TargetOf(FocusedMonitor()))) is not LiveMonitor destination)
            return Rejected(name, "error: monitor not found");

        return MoveWorkspaceTo(desk, destination, name);
    }

    private Outcome SwapActiveWorkspaces(string arguments)
    {
        const string name = "swapactiveworkspaces";
        if (!SplitPair(arguments, out var first, out var second))
            return Rejected(name, "error: monitor not found");

        var targets = Targets();
        var current = TargetOf(FocusedMonitor());
        if (MonitorOf(MonitorSelector.Resolve(first, targets, current)) is not LiveMonitor left
            || MonitorOf(MonitorSelector.Resolve(second, targets, current)) is not LiveMonitor right)
            return Rejected(name, "error: monitor not found");

        if (left.Id == right.Id)
            return View([], dispatcher: name);

        var before = ShowingNow();
        var deskLeft = WorkspaceFor(left.Active ?? NextFree(left.Id), left.Id);
        var deskRight = WorkspaceFor(right.Active ?? NextFree(right.Id), right.Id);
        var dx = right.Effective.X - left.Effective.X;
        var dy = right.Effective.Y - left.Effective.Y;
        deskLeft.Layout.NudgeFloating(dx, dy);
        deskRight.Layout.NudgeFloating(-dx, -dy);
        var leftNumber = NumberOn(deskLeft, right.Id, deskRight);
        var rightNumber = NumberOn(deskRight, left.Id, deskLeft);
        deskLeft.HomeIdentity = null;
        deskLeft.HomeNumber = 0;
        deskRight.HomeIdentity = null;
        deskRight.HomeNumber = 0;
        var parked = new WorkspaceId("\0", int.MinValue);
        Rebind(deskLeft, parked);
        Rebind(deskRight, WorkspaceId.FromNumber(rightNumber, left.Id));
        Rebind(deskLeft, WorkspaceId.FromNumber(leftNumber, right.Id));
        left.Active = deskRight.Id;
        right.Active = deskLeft.Id;
        _activeOn[left.Id] = deskRight.Id;
        _activeOn[right.Id] = deskLeft.Id;
        Refit(deskLeft);
        Refit(deskRight);

        WindowId? focus = null;
        var focused = left.Focused ? left : right.Focused ? right : null;
        // A swap reports only the Workspace the focused Monitor shows afterwards.
        if (focused?.Active is WorkspaceId swapped)
            NoteWorkspaceActive(swapped);

        if (focused is LiveMonitor screen && screen.Active is WorkspaceId active && FindWorkspace(active) is LiveWorkspace shown)
        {
            var next = Accept(shown.Remembered());
            _active = next;
            shown.Focus = next;
            if (next is WindowId noted)
                shown.Layout.NoteFocus(noted);
            Remember(active, screen.Id);
            focus = next;
        }

        SyncActive();
        var (show, hide) = VisibilityDelta(before);
        return View(ShownPlacements(), show, hide, dispatcher: name, focus: focus);
    }

    private Outcome MoveWorkspaceTo(LiveWorkspace desk, LiveMonitor destination, string dispatcher)
    {
        if (desk.Monitor == destination.Id)
            return View([], dispatcher: dispatcher);

        var before = ShowingNow();
        var origin = MonitorById(desk.Monitor);
        var wasActive = origin is not null && origin.Active == desk.Id;
        var follow = wasActive && origin is { Focused: true };
        var dx = destination.Effective.X - (origin?.Effective.X ?? destination.Effective.X);
        var dy = destination.Effective.Y - (origin?.Effective.Y ?? destination.Effective.Y);
        if (wasActive && origin is not null)
            Plug(origin, desk);

        desk.Layout.NudgeFloating(dx, dy);
        if (desk.Id.IsSpecial)
            desk.Monitor = destination.Id;
        else
        {
            desk.HomeIdentity = null;
            desk.HomeNumber = 0;
            var number = NumberOn(desk, destination.Id, desk);
            Rebind(desk, WorkspaceId.FromNumber(number, destination.Id));
        }

        Refit(desk);

        WindowId? focus = null;
        if (follow)
        {
            destination.Active = desk.Id;
            _activeOn[destination.Id] = desk.Id;
            var next = Accept(desk.Remembered());
            _active = next;
            desk.Focus = next;
            if (next is WindowId noted)
                desk.Layout.NoteFocus(noted);
            Focus(destination.Id);
            focus = next;
        }

        SyncActive();
        var (show, hide) = VisibilityDelta(before);
        return View(ShownPlacements(), show, hide, dispatcher: dispatcher, focus: focus);
    }

    private Outcome MoveActiveOnto(LiveMonitor screen, bool silent, string dispatcher, Point? focal = null)
    {
        if (screen.Active is not WorkspaceId target)
            return View([], dispatcher: dispatcher);

        return MoveTo(target, silent, consumed: false, dispatcher, focal: focal);
    }

    private Outcome FocusOnto(LiveMonitor screen, string dispatcher, WindowId? preferred)
    {
        if (screen.Focused && preferred is null)
            return View([], dispatcher: dispatcher);

        // A monitor with a special workspace open gives the focus to that workspace's window.
        var desk = SpecialOn(screen.Id) is WorkspaceId special && FindWorkspace(special) is LiveWorkspace open
            ? open
            : screen.Active is WorkspaceId workspace ? FindWorkspace(workspace) : null;
        var next = preferred;
        if (next is WindowId wanted && (desk is null || desk.ClientOf(wanted) is null || HasNoFocus(wanted)))
            next = null;
        if (next is null && desk is not null)
            next = Accept(desk.Remembered());

        Focus(screen.Id);
        _active = next;
        if (desk is not null)
        {
            desk.Focus = next;
            if (next is WindowId noted)
                desk.Layout.NoteFocus(noted);
        }

        return View([], focus: next, dispatcher: dispatcher);
    }

    private void Plug(LiveMonitor origin, LiveWorkspace leaving)
    {
        LiveWorkspace? replacement = null;
        foreach (var other in _workspaces)
        {
            if (other.Id == leaving.Id || other.Monitor != origin.Id)
                continue;

            replacement = other;
            break;
        }

        replacement ??= WorkspaceFor(NextFree(origin.Id), origin.Id);
        origin.Active = replacement.Id;
        _activeOn[origin.Id] = replacement.Id;

        // Pinned Clients stay on the Monitor, on the Workspace it shows next.
        foreach (var window in PinnedFloating(leaving))
            Transfer(leaving, replacement, window);
    }

    private WindowId? AdjacentOn(LiveMonitor screen, Rectangle origin, MonitorDirection direction)
    {
        if (screen.Active is not WorkspaceId workspace || FindWorkspace(workspace) is not LiveWorkspace desk)
            return null;

        WindowId? best = null;
        var bestOverlap = 1.0;
        foreach (var client in desk.Layout.Clients)
        {
            if (client.Minimized || HasNoFocus(client.Window) || !desk.Layout.TryBounds(client.Window, out var box))
                continue;
            // A tie goes to the Client focused most recently, as binds:focus_preferred_method 0 picks.
            if (!MonitorSelector.Overlaps(origin, Ideal(desk, client.Window, box), direction, out var overlap)
                || overlap < bestOverlap
                || (overlap == bestOverlap && (best is not WindowId held || Recency(client.Window) <= Recency(held))))
                continue;

            bestOverlap = overlap;
            best = client.Window;
        }

        return best;
    }

    // A tile's box with its border, and with every side that touches the tiling area pushed out to the
    // Monitor's edge, as Winland compares windows across Monitors. Outer gaps and reserved
    // space then no longer keep two tiles on neighbouring Monitors apart. A floating box is left as it is.
    private Rectangle Ideal(LiveWorkspace desk, WindowId window, Rectangle box)
    {
        if (desk.ClientOf(window) is not Client client || MonitorById(desk.Monitor) is not { Effective.Enabled: true } screen)
            return box;
        // A fullscreen Client covers its Monitor, floating or not.
        if (desk.IsCovering(client))
            return screen.Bounds;
        if (client.Floating)
            return box;

        var border = desk.Options.BorderSize;
        var work = desk.Options.GapsOut.Apply(screen.Effective.WorkArea);
        var monitor = screen.Bounds;
        var left = box.X - border;
        var top = box.Y - border;
        var right = box.X + box.Width + border;
        var bottom = box.Y + box.Height + border;
        if (Math.Abs(left - work.X) <= 1)
            left = monitor.X;
        if (Math.Abs(top - work.Y) <= 1)
            top = monitor.Y;
        if (Math.Abs(right - (work.X + work.Width)) <= 1)
            right = monitor.X + monitor.Width;
        if (Math.Abs(bottom - (work.Y + work.Height)) <= 1)
            bottom = monitor.Y + monitor.Height;

        return new Rectangle(left, top, right - left, bottom - top);
    }

    private WindowId? Accept(WindowId? window) =>
        window is WindowId candidate && !HasNoFocus(candidate) ? candidate : null;

    private List<MonitorTarget> Targets()
    {
        var targets = new List<MonitorTarget>();
        foreach (var screen in _monitors)
        {
            if (screen.Effective.Enabled)
                targets.Add(Target(screen));
        }

        return targets;
    }

    private static MonitorTarget Target(LiveMonitor screen)
    {
        var monitor = screen.Effective;
        return new MonitorTarget(screen.Id, monitor.Name, monitor.Description, monitor.X, monitor.Y, monitor.Width, monitor.Height);
    }

    private static MonitorTarget? TargetOf(LiveMonitor? screen) =>
        screen is { Effective.Enabled: true } enabled ? Target(enabled) : null;

    private LiveMonitor? MonitorOf(MonitorTarget? target)
    {
        if (target is not MonitorTarget found)
            return null;

        var screen = MonitorById(found.Id);
        return screen is { Effective.Enabled: true } ? screen : null;
    }

    private LiveMonitor? MonitorHolding(LiveWorkspace desk)
    {
        var screen = MonitorById(desk.Monitor);
        return screen is { Effective.Enabled: true } ? screen : null;
    }

    private LiveMonitor? FocusedMonitor()
    {
        foreach (var screen in _monitors)
        {
            if (screen.Focused && screen.Effective.Enabled)
                return screen;
        }

        foreach (var screen in _monitors)
        {
            if (screen.Effective.Enabled)
                return screen;
        }

        return null;
    }

    private HashSet<WindowId> ShowingNow()
    {
        var shown = new HashSet<WindowId>();
        foreach (var window in ShownWindows())
            shown.Add(window);

        return shown;
    }

    private (WindowId[] Show, WindowId[] Hide) VisibilityDelta(HashSet<WindowId> before)
    {
        var after = ShowingNow();
        var show = new List<WindowId>();
        var hide = new List<WindowId>();
        foreach (var window in after)
        {
            if (!before.Contains(window))
                show.Add(window);
        }

        foreach (var window in before)
        {
            if (!after.Contains(window))
                hide.Add(window);
        }

        return (show.ToArray(), hide.ToArray());
    }



    private static bool TryMonitorArgument(string arguments, out string monitor, out bool silent)
    {
        silent = false;
        monitor = "";
        var text = arguments.Trim();
        if (text.EndsWith(" silent", StringComparison.OrdinalIgnoreCase))
        {
            silent = true;
            text = text[..^7].TrimEnd();
        }

        if (!text.StartsWith("mon:", StringComparison.OrdinalIgnoreCase))
            return false;

        monitor = text[4..].Trim();
        return true;
    }

    private static bool SplitPair(string arguments, out string first, out string second)
    {
        var text = arguments.Trim();
        var space = text.IndexOf(' ');
        if (space < 0)
        {
            first = text;
            second = "";
            return false;
        }

        first = text[..space].Trim();
        second = text[(space + 1)..].Trim();
        return first.Length > 0 && second.Length > 0;
    }

    private static string FirstToken(string arguments)
    {
        var text = arguments.Trim();
        var space = text.IndexOf(' ');
        return space < 0 ? text : text[..space];
    }
}
