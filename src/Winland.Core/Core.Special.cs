namespace Winland;

public sealed partial class Core
{
    private readonly Dictionary<MonitorId, WorkspaceId> _specialOn = [];
    private LiveWorkspace? _closing;

    private WorkspaceId? SpecialOn(MonitorId monitor)
    {
        if (MonitorById(monitor) is { } screen)
            return screen.Special;

        return _specialOn.TryGetValue(monitor, out var stored) ? stored : null;
    }

    private void SetSpecial(MonitorId monitor, WorkspaceId? workspace)
    {
        if (MonitorById(monitor) is { } screen)
            screen.Special = workspace;

        if (workspace is WorkspaceId id)
            _specialOn[monitor] = id;
        else
            _specialOn.Remove(monitor);
    }

    private MonitorId? MonitorShowing(WorkspaceId workspace)
    {
        if (_monitors.Count == 0)
            return SpecialOn(MonitorId.Primary) == workspace ? MonitorId.Primary : null;

        foreach (var screen in _monitors)
        {
            if (screen.Effective.Enabled && screen.Special == workspace)
                return screen.Id;
        }

        return null;
    }

    private LiveWorkspace CurrentWorkspace()
    {
        var monitor = FocusedId();
        if (SpecialOn(monitor) is WorkspaceId special && FindWorkspace(special) is LiveWorkspace desk)
            return desk;

        return FocusedWorkspace();
    }

    private LiveWorkspace LayoutWorkspace(WindowId window) => WorkspaceContaining(window) ?? CurrentWorkspace();

    private Outcome ToggleSpecial(string arguments, bool allowsElevation)
    {
        const string name = "togglespecialworkspace";
        var target = SpecialName(arguments);
        var monitor = FocusedId();
        if (SpecialOn(monitor) == target)
            return CloseSpecial(monitor, name, allowsElevation);

        return OpenSpecial(target, null, name, allowsElevation);
    }

    private static WorkspaceId SpecialName(string arguments)
    {
        var suffix = arguments.Trim();
        if (suffix.Length == 0 || suffix == "special")
            return WorkspaceId.DefaultSpecial;

        return new WorkspaceId("special:" + suffix);
    }

    private Outcome OpenSpecial(WorkspaceId target, WindowId? focus, string? dispatcher, bool allowsElevation)
    {
        var monitor = FocusedId();
        if (SpecialOn(monitor) == target)
        {
            if (focus is WindowId requested && ClientOn(target, requested) is { Minimized: false })
            {
                _active = requested;
                if (FindWorkspace(target) is { } open)
                    open.Focus = requested;
                return View([], focus: requested, dispatcher: dispatcher, allowsElevation: allowsElevation);
            }

            return View([], dispatcher: dispatcher, allowsElevation: allowsElevation);
        }

        var show = new List<WindowId>();
        var hide = new List<WindowId>();
        if (MonitorShowing(target) is MonitorId other && other != monitor)
            SetSpecial(other, null);

        if (SpecialOn(monitor) is WorkspaceId previous && previous != target)
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

        SetSpecial(monitor, target);
        foreach (var window in VisibleIds(target))
        {
            if (!hide.Contains(window))
                show.Add(window);
        }

        WindowId? next = focus is WindowId wanted && desk.ClientOf(wanted) is { Minimized: false }
            ? wanted
            : desk.Remembered();
        if (next is null && _input.SpecialFallthrough)
            next = FocusedWorkspace().Remembered();

        _active = next;
        desk.Focus = next;
        return View(desk.Layout.Visible(), show, hide, dispatcher: dispatcher, focus: next, allowsElevation: allowsElevation);
    }

    private Outcome CloseSpecial(MonitorId monitor, string dispatcher, bool allowsElevation)
    {
        var hidden = ConcealSpecial(monitor);
        var normal = FocusedWorkspace();
        return View(
            normal.Layout.Visible(),
            hide: hidden,
            focus: _active,
            dispatcher: dispatcher,
            allowsElevation: allowsElevation);
    }

    private WindowId[] SpecialHiddenBy(MonitorId monitor, WindowId? focus)
    {
        if (SpecialOn(monitor) is null)
            return [];

        if (focus is not null && !_input.SpecialFallthrough)
            return ConcealSpecial(monitor);

        if (_session.HideSpecialOnWorkspaceChange)
            return ConcealSpecial(monitor);

        return [];
    }

    private WindowId[] ConcealSpecial(MonitorId monitor)
    {
        if (SpecialOn(monitor) is not WorkspaceId current)
            return [];

        var hidden = VisibleIds(current);
        SetSpecial(monitor, null);
        var normal = FindWorkspace(ActiveOn(monitor));
        var next = normal is null ? null : normal.Remembered();
        _active = next;
        if (normal is not null)
            normal.Focus = next;

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
        bool allowsElevation,
        HashSet<WindowId> sourceCovered,
        HashSet<WindowId> destinationCovered)
    {
        if (!silent)
        {
            var opened = OpenSpecial(destination.Id, window, dispatcher, allowsElevation);
            if (source.Id.IsSpecial && source.Layout.Clients.Count == 0)
                DropIfEmpty(source.Id);

            if (!IsShown(source.Id) || source.Id == destination.Id)
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
        if (IsShown(source.Id))
            source.Collect(sourceCovered, revealed, hidden);
        if (IsShown(destination.Id))
            destination.Collect(destinationCovered, revealed, hidden);
        else
            hidden.Add(window);

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
        else if (source.Layout.Clients.Count == 0)
            DropIfEmpty(source.Id);

        if (_active == window)
        {
            _active = revealed.Count == 1 && source.ClientOf(revealed[0]) is not null
                ? revealed[0]
                : source.LastVisible();
            source.Focus = _active;
        }

        var placed = new List<Placement>();
        if (IsShown(source.Id))
            placed.AddRange(source.Layout.Placed());
        if (IsShown(destination.Id))
            placed.AddRange(destination.Layout.Visible());

        return View(placed, revealed, hidden, consumed, dispatcher, allowsElevation: allowsElevation);
    }

    private void RehomeSpecials(LiveMonitor? home, List<LiveMonitor> enabled)
    {
        if (home is null)
            return;

        foreach (var desk in _workspaces)
        {
            if (!desk.Id.IsSpecial || HeldBy(enabled, desk.Monitor))
                continue;

            var shown = SpecialOn(desk.Monitor) == desk.Id;
            SetSpecial(desk.Monitor, null);
            desk.Monitor = home.Id;
            Refit(desk);
            if (shown && SpecialOn(home.Id) is null)
                SetSpecial(home.Id, desk.Id);
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
