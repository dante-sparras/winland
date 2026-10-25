namespace Winland.Application;

/// <summary>Special Workspaces: <c>togglespecialworkspace</c> and how they show over a Monitor.</summary>
public sealed partial class WindowManager
{
    private LiveWorkspace CurrentWorkspace()
    {
        var monitor = FocusedId();
        if (_workspaces.Shown.SpecialOn(monitor) is WorkspaceId special && _workspaces.Find(special) is LiveWorkspace desk)
            return desk;

        return FocusedWorkspace();
    }

    private LiveWorkspace LayoutWorkspace(WindowId window) => WorkspaceContaining(window) ?? CurrentWorkspace();

    private Outcome ToggleSpecial(string arguments)
    {
        const string name = "togglespecialworkspace";
        var target = SpecialName(arguments);
        var monitor = FocusedId();
        if (_workspaces.Shown.SpecialOn(monitor) == target)
            return CloseSpecial(monitor, name);

        return OpenSpecial(target, null, name);
    }

    private static WorkspaceId SpecialName(string arguments)
    {
        var suffix = arguments.Trim();
        if (suffix.Length == 0 || suffix == "special")
            return WorkspaceId.DefaultSpecial;

        return new WorkspaceId("special:" + suffix);
    }

    // A floating Client whose middle is off the Monitor a special workspace opens on is brought onto it, as in the
    // Winland: by the offset between the Monitors when its middle is on another one, else to the middle.
    private void BringFloatingOnto(LiveWorkspace desk, MonitorId monitor)
    {
        if (MonitorById(monitor) is not { Effective.Enabled: true } screen)
            return;

        var here = screen.Bounds;
        foreach (var client in desk.Layout.Clients)
        {
            if (!client.Floating || client.Minimized || !desk.Layout.TryBounds(client.Window, out var box))
                continue;

            var middle = new Point(box.X + (box.Width / 2), box.Y + (box.Height / 2));
            if (Holds(here, middle))
                continue;

            Rectangle? holder = null;
            foreach (var other in _monitors)
            {
                if (other.Effective.Enabled && Holds(other.Bounds, middle))
                    holder = other.Bounds;
            }

            var moved = holder is Rectangle from
                ? box with { X = box.X - from.X + here.X, Y = box.Y - from.Y + here.Y }
                : box with { X = here.X + (here.Width / 2) - (box.Width / 2), Y = here.Y + (here.Height / 2) - (box.Height / 2) };
            _ = desk.Layout.PlaceFloating(client.Window, moved);
        }
    }

    // Edges count as inside, as Winland tests a point against a Monitor.
    private static bool Holds(Rectangle bounds, Point point) =>
        point.X >= bounds.X && point.X <= bounds.X + bounds.Width && point.Y >= bounds.Y && point.Y <= bounds.Y + bounds.Height;

    private Outcome OpenSpecial(WorkspaceId target, WindowId? focus, string? dispatcher)
    {
        var monitor = FocusedId();
        if (_workspaces.Shown.SpecialOn(monitor) == target)
        {
            if (focus is WindowId requested && ClientOn(target, requested) is { Minimized: false })
            {
                _focus.Take(requested, _workspaces.Find(target));
                return View([], focus: requested, dispatcher: dispatcher);
            }

            return View([], dispatcher: dispatcher);
        }

        var show = new List<WindowId>();
        var hide = new List<WindowId>();
        if (_workspaces.Shown.ShowingSpecial(target) is MonitorId other && other != monitor)
            _workspaces.Shown.ShowSpecial(other, null);

        if (_workspaces.Shown.SpecialOn(monitor) is WorkspaceId previous && previous != target)
        {
            foreach (var window in VisibleIds(previous))
                hide.Add(window);
        }

        var desk = WorkspaceFor(target, monitor);
        if (desk.Monitor != monitor)
        {
            desk.Monitor = monitor;
            Refit(desk);
        }

        _workspaces.Shown.ShowSpecial(monitor, target);
        BringFloatingOnto(desk, monitor);
        foreach (var window in VisibleIds(target))
        {
            if (!hide.Contains(window))
                show.Add(window);
        }

        WindowId? next = focus is WindowId wanted && desk.ClientOf(wanted) is { Minimized: false }
            ? wanted
            : _focus.Remembered(desk);

        _focus.Take(next, desk);
        return View(desk.Layout.Visible(), show, hide, dispatcher: dispatcher, focus: next);
    }

    private Outcome CloseSpecial(MonitorId monitor, string? dispatcher)
    {
        var hidden = ConcealSpecial(monitor);
        var normal = FocusedWorkspace();
        return View(
            normal.Layout.Visible(),
            hide: hidden,
            focus: _focus.Focused,
            dispatcher: dispatcher);
    }

    private WindowId[] ConcealSpecial(MonitorId monitor)
    {
        if (_workspaces.Shown.SpecialOn(monitor) is not WorkspaceId current)
            return [];

        var hidden = VisibleIds(current);
        _workspaces.Shown.ShowSpecial(monitor, null);
        var normal = _workspaces.Find(_workspaces.Shown.ActiveOn(monitor));
        var next = normal is null ? null : _focus.Remembered(normal);
        _focus.Take(next, normal);

        DropIfEmpty(current);
        return hidden;
    }

    private Outcome FinishSpecialMove(
        LiveWorkspace source,
        LiveWorkspace destination,
        WindowId window,
        bool silent,
        bool consumed,
        string dispatcher,
        HashSet<WindowId> sourceCovered,
        HashSet<WindowId> destinationCovered)
    {
        if (!silent)
        {
            var opened = OpenSpecial(destination.Id, window, dispatcher);
            if (source.Id.IsSpecial && source.Layout.Clients.Count == 0)
                DropIfEmpty(source.Id);

            if (!_workspaces.Shown.IsShown(source.Id) || source.Id == destination.Id)
                return opened.Focus is null ? opened with { Focus = window } : opened;

            var show = new List<WindowId>(opened.Show);
            var hide = new List<WindowId>(opened.Hide);
            source.Collect(sourceCovered, show, hide);
            var placements = new List<Placement>(opened.Placements);
            placements.AddRange(source.Layout.Visible());
            return opened with
            {
                Placements = placements,
                Show = show,
                Hide = hide,
                Focus = opened.Focus ?? window,
            };
        }

        var revealed = new List<WindowId>();
        var hidden = new List<WindowId>();
        if (_workspaces.Shown.IsShown(source.Id))
            source.Collect(sourceCovered, revealed, hidden);
        if (_workspaces.Shown.IsShown(destination.Id))
            destination.Collect(destinationCovered, revealed, hidden);
        else
            hidden.Add(window);

        if (source.Id.IsSpecial
            && source.Layout.Clients.Count == 0
            && _workspaces.Shown.SpecialOn(source.Monitor) == source.Id)
        {
            foreach (var concealed in ConcealSpecial(source.Monitor))
            {
                if (!hidden.Contains(concealed))
                    hidden.Add(concealed);
            }
        }
        else if (source.Layout.Clients.Count == 0)
            DropIfEmpty(source.Id);

        if (_focus.Focused == window)
        {
            _focus.Take(FocusState.AfterLeaving(source, revealed), source);
        }

        var placed = new List<Placement>();
        if (_workspaces.Shown.IsShown(source.Id))
            placed.AddRange(source.Layout.Placed());
        if (_workspaces.Shown.IsShown(destination.Id))
            placed.AddRange(destination.Layout.Visible());

        return View(placed, revealed, hidden, consumed, dispatcher);
    }

    private void RehomeSpecials(LiveMonitor? home, List<LiveMonitor> enabled)
    {
        if (home is null)
            return;

        foreach (var desk in _workspaces)
        {
            if (!desk.Id.IsSpecial || HeldBy(enabled, desk.Monitor))
                continue;

            var shown = _workspaces.Shown.SpecialOn(desk.Monitor) == desk.Id;
            _workspaces.Shown.ShowSpecial(desk.Monitor, null);
            desk.Monitor = home.Id;
            Refit(desk);

            // A special workspace that was shown is shown on the Monitor that takes it, in place of the one
            // shown there.
            if (shown)
                _workspaces.Shown.ShowSpecial(home.Id, desk.Id);
        }
    }

    private static void AddSpecialEvents(List<StreamEvent> events, State before, State after)
    {
        if (before.Screens.Count == 0 && after.Screens.Count == 0)
        {
            if (before.SpecialWorkspace != after.SpecialWorkspace)
                EmitSpecial(events, after.SpecialWorkspace, "");

            return;
        }

        var ids = new List<MonitorId>();
        foreach (var screen in before.Screens)
        {
            if (!ids.Contains(screen.Id))
                ids.Add(screen.Id);
        }

        foreach (var screen in after.Screens)
        {
            if (!ids.Contains(screen.Id))
                ids.Add(screen.Id);
        }

        foreach (var id in ids)
        {
            var previous = FindMonitorSnapshot(before, id);
            var next = FindMonitorSnapshot(after, id);
            if (previous?.SpecialWorkspace == next?.SpecialWorkspace)
                continue;

            var name = next?.Monitor.Name ?? previous?.Monitor.Name ?? "";
            EmitSpecial(events, next?.SpecialWorkspace, name);
        }
    }

    private static void EmitSpecial(List<StreamEvent> events, WorkspaceId? workspace, string monitor)
    {
        events.Add(new ActiveSpecial(workspace, monitor));
    }

    private static MonitorSnapshot? FindMonitorSnapshot(State state, MonitorId id)
    {
        foreach (var screen in state.Screens)
        {
            if (screen.Id == id)
                return screen;
        }

        return null;
    }
}
