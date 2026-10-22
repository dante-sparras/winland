namespace Winland.Application;

/// <summary>Switching Workspaces, moving windows between them, and the history <c>workspace previous</c> reads.</summary>
public sealed partial class WindowManager
{
    // A floating Client keeps its place relative to the Monitor when it moves onto a Workspace on another Monitor,
    //.
    private Taken OntoMonitor(Taken taken, MonitorId from, MonitorId to)
    {
        if (!taken.Floating || from == to || MonitorById(from) is not { Effective.Enabled: true } source || MonitorById(to) is not { Effective.Enabled: true } destination)
            return taken;

        var bounds = taken.Bounds;
        return taken with { Bounds = bounds with { X = bounds.X - source.Bounds.X + destination.Bounds.X, Y = bounds.Y - source.Bounds.Y + destination.Bounds.Y } };
    }

    private Outcome MoveTo(
        string argument,
        bool silent,
        bool consumed,
        string dispatcher,
        WindowId? chosen = null,
        bool windowChosen = false,
        Point? focal = null)
    {
        if (!WorkspaceAddress.TryResolve(argument, Context(), out var resolved))
            return View([], consumed: consumed, dispatcher: dispatcher);

        return MoveTo(resolved, silent, consumed, dispatcher, chosen, windowChosen, focal);
    }

    private Outcome MoveTo(
        WorkspaceId target,
        bool silent,
        bool consumed,
        string dispatcher,
        WindowId? chosen = null,
        bool windowChosen = false,
        Point? focal = null)
    {
        var window = windowChosen ? chosen : ActiveWindow();
        if (window is not WindowId id)
            return View([], consumed: consumed, dispatcher: dispatcher);

        // A pinned Client is never moved onto a special workspace, which still opens that special
        // workspace on a move that is not silent and keeps the Client focused.
        var source = WorkspaceContaining(id);
        if (source is not null && target.IsSpecial && IsPinned(id))
        {
            if (silent)
                return View([], consumed: consumed, dispatcher: dispatcher);

            var opened = OpenSpecial(target, null, dispatcher);
            TakeFocus(source, id);
            return opened with { Focus = id, Consumed = consumed };
        }

        if (source is null || source.Id == target)
            return View([], consumed: consumed, dispatcher: dispatcher);

        var sourceCovered = source.Covered();
        var wasHidden = !_workspaces.Shown.IsShown(source.Id) || sourceCovered.Contains(id);
        if (!source.Take(id, out var taken))
            return View([], consumed: consumed, dispatcher: dispatcher);

        var destination = WorkspaceFor(target, target.IsSpecial ? FocusedId() : null);
        var elsewhere = target.IsSpecial ? null : ShownElsewhere(target);

        var destinationCovered = destination.Covered();
        var solitary = source.GroupOf(id) is null ? SolitaryGroup(destination, id) : null;
        var area = WorkArea(destination, _area);
        destination.Receive(id, OntoMonitor(taken, source.Monitor, destination.Monitor), area, pointer: focal);
        CarryGroup(source, destination, id);
        JoinOnMove(destination, solitary, id);
        Restamp(source, []);
        Restamp(destination, []);

        if (target.IsSpecial)
            return FinishSpecialMove(source, destination, id, silent, consumed, dispatcher, sourceCovered, destinationCovered);

        if (!silent && source.Id.IsSpecial && _workspaces.Shown.SpecialOn(source.Monitor) == source.Id)
        {
            var concealed = ConcealSpecial(source.Monitor);
            return FollowMove(SwitchTo(target, id, consumed, dispatcher), id, concealed, destination);
        }

        if (!silent && elsewhere is { } owner)
        {
            var followed = FocusShown(owner, target, id, consumed, dispatcher);
            var show = new List<WindowId>();
            var conceal = new List<WindowId>();
            if (_workspaces.Shown.IsShown(source.Id))
                source.Collect(sourceCovered, show, conceal);
            if (_workspaces.Shown.IsShown(destination.Id))
                Arrive(show, conceal);
            var moved = new List<Placement>();
            if (_workspaces.Shown.IsShown(source.Id))
                moved.AddRange(source.Layout.Placed());
            if (_workspaces.Shown.IsShown(destination.Id))
                moved.AddRange(destination.Layout.Visible());
            return followed with
            {
                Placements = moved,
                Show = [.. followed.Show, .. show],
                Hide = [.. followed.Hide, .. conceal],
            };
        }

        if (!silent)
        {
            // Switching to the Workspace already shown here changes nothing on screen, so the reflow
            // and the arriving window are added to what the switch returns.
            var here = _workspaces.Shown.ActiveOn(FocusedId()) == target;
            var followedSwitch = SwitchTo(target, id, consumed, dispatcher);
            if (here)
            {
                var show = new List<WindowId>(followedSwitch.Show);
                var conceal = new List<WindowId>(followedSwitch.Hide);
                if (_workspaces.Shown.IsShown(source.Id))
                    source.Collect(sourceCovered, show, conceal);
                Arrive(show, conceal);
                var moved = new List<Placement>();
                if (_workspaces.Shown.IsShown(source.Id))
                    moved.AddRange(source.Layout.Placed());
                moved.AddRange(destination.Layout.Visible());
                followedSwitch = followedSwitch with { Placements = moved, Show = show, Hide = conceal };
            }
            else if (_workspaces.Shown.IsShown(source.Id))
            {
                // The Workspace the window left is still shown on its own Monitor, so it is laid out again too.
                followedSwitch = followedSwitch with { Placements = [.. source.Layout.Placed(), .. followedSwitch.Placements] };
            }

            return followedSwitch.Focus is null && _focus.Focused == id
                ? followedSwitch with { Focus = id }
                : followedSwitch;
        }

        var revealed = new List<WindowId>();
        var hidden = new List<WindowId>();
        if (_workspaces.Shown.IsShown(source.Id))
            source.Collect(sourceCovered, revealed, hidden);
        if (_workspaces.Shown.IsShown(destination.Id))
            Arrive(revealed, hidden);
        else
            hidden.Add(id);

        if (source.Id.IsSpecial
            && source.Layout.Clients.Count == 0
            && _config.Session.CloseSpecialOnEmpty
            && _workspaces.Shown.SpecialOn(source.Monitor) == source.Id)
        {
            foreach (var concealed in ConcealSpecial(source.Monitor))
            {
                if (!hidden.Contains(concealed))
                    hidden.Add(concealed);
            }
        }

        if (_focus.Focused == id)
        {
            _focus.Take(FocusState.AfterLeaving(source, revealed), source);
        }

        var placements = new List<Placement>();
        if (_workspaces.Shown.IsShown(source.Id))
            placements.AddRange(source.Layout.Placed());
        if (_workspaces.Shown.IsShown(destination.Id))
            placements.AddRange(destination.Layout.Visible());

        return View(placements, revealed, hidden, consumed, dispatcher);

        // The moved window was not in the destination's covered set, so Collect alone never shows it. When
        // it was hidden where it was, it is shown here unless the destination's Layout covers it.
        void Arrive(List<WindowId> show, List<WindowId> conceal)
        {
            destination.Collect(destinationCovered, show, conceal);
            if (wasHidden
                && destination.ClientOf(id) is { Minimized: false }
                && !destination.Covered().Contains(id)
                && !show.Contains(id))
                show.Add(id);
        }
    }

    private Outcome FollowMove(Outcome followed, WindowId window, WindowId[] concealed, LiveWorkspace destination)
    {
        var show = new List<WindowId>(followed.Show);
        if (_workspaces.Shown.IsShown(destination.Id) && !show.Contains(window))
            show.Add(window);

        var placements = followed.Placements.Count > 0 ? followed.Placements : destination.Layout.Visible();
        return followed with
        {
            Placements = placements,
            Show = show,
            Hide = Combine(followed.Hide, concealed),
            Focus = followed.Focus ?? window,
        };
    }

    private Outcome SwitchTo(WorkspaceId target, WindowId? focus, bool consumed, string? dispatcher)
    {
        if (target.IsSpecial)
            return OpenSpecial(target, focus, dispatcher);

        if (ShownElsewhere(target) is { } elsewhere)
        {
            var away = SpecialHiddenBy(elsewhere.Id, focus);
            var followed = FocusShown(elsewhere, target, focus, consumed, dispatcher);
            return away.Length == 0 ? followed : followed with { Hide = Combine(followed.Hide, away) };
        }

        // A Workspace that lives on another Monitor, but is not the one that Monitor shows, is shown there.
        if (_workspaces.Find(target) is { } located && MonitorById(located.Monitor) is { Effective.Enabled: true } home && home.Id != FocusedId())
            Focus(home.Id);

        var monitor = FocusedId();
        var concealed = SpecialHiddenBy(monitor, focus);

        var sourceId = _workspaces.Shown.ActiveOn(monitor);
        if (sourceId == target)
        {
            if (focus is WindowId requested && ClientOn(target, requested) is not null
                && (_workspaces.Shown.SpecialOn(monitor) is null || _config.Input.SpecialFallthrough))
            {
                _focus.Take(requested, FocusedWorkspace());
            }

            return View([], hide: concealed, consumed: consumed, dispatcher: dispatcher);
        }

        var keep = _focus.Focused is WindowId current
            && IsPinned(current)
            && ClientOn(sourceId, current) is not null
            ? current
            : (WindowId?)null;
        _workspaces.Shown.Show(monitor, target);

        NoteWorkspaceActive(target);
        Remember(target, monitor);
        var destination = WorkspaceFor(target, monitor);
        CarryPinned(sourceId, destination);
        var hide = VisibleIds(sourceId);
        var keepSpecial = _workspaces.Shown.SpecialOn(monitor) is not null && focus is null;
        if (!keepSpecial)
        {
            _focus.Take(
                focus is WindowId requestedFocus && ClientOn(target, requestedFocus) is not null
                    ? requestedFocus
                    : keep is WindowId pinnedFocus && ClientOn(target, pinnedFocus) is not null
                        ? pinnedFocus
                        : _focus.Remembered(destination),
                destination);
        }

        DropIfEmpty(sourceId);
        return View(
            destination.Layout.Visible(),
            VisibleIds(target),
            Combine(hide, concealed),
            consumed,
            dispatcher);
    }

    private LiveMonitor? ShownElsewhere(WorkspaceId target)
    {
        var desk = _workspaces.Find(target);
        if (desk is null)
            return null;

        var owner = MonitorById(desk.Monitor);
        if (owner is null || !owner.Effective.Enabled || _workspaces.Shown.Shown(owner.Id) != target || owner.Id == FocusedId())
            return null;

        return owner;
    }

    private Outcome FocusShown(LiveMonitor owner, WorkspaceId target, WindowId? focus, bool consumed, string? dispatcher)
    {
        Focus(owner.Id);
        var destination = _workspaces.Find(target);
        WindowId? next = null;
        if (focus is WindowId requested && ClientOn(target, requested) is not null)
            next = requested;
        else if (destination is not null)
            next = _focus.Remembered(destination);

        _focus.Take(next, destination);

        return View([], focus: next, consumed: consumed, dispatcher: dispatcher);
    }

    // A visit to the history workspace previous reads.
    private void Remember(WorkspaceId workspace, MonitorId monitor) =>
        _workspaces.Shown.Visit(workspace, monitor, _config.BindOptions.AllowWorkspaceCycles);

    // A switch to a Workspace another Monitor shows moves the cursor onto that Monitor, to the
    // middle of the Client it focuses or of the Monitor. cursor:no_warps stops it, as it stops Winland's warp.
    // A cursor option that warps after a switch goes first.
    private void NoteCrossMonitorWarp(MonitorId from, Outcome switched)
    {
        _crossMonitorWarp = null;
        var owner = FocusedId();
        if (owner == from || MonitorById(owner) is not LiveMonitor screen)
            return;

        var bounds = screen.Bounds;
        if (_config.BindOptions.WorkspaceCenterOn == 1 && _focus.Focused is WindowId window)
        {
            foreach (var placement in FocusedWorkspace().Layout.Visible())
            {
                if (placement.Client.Value == window.Value)
                {
                    bounds = placement.Bounds;
                    break;
                }
            }

            foreach (var placement in switched.Placements)
            {
                if (placement.Client.Value == window.Value)
                    bounds = placement.Bounds;
            }
        }

        _crossMonitorWarp = new WarpTo(new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2)));
    }

    // binds:workspace_back_and_forth: asking for the Workspace the focused Monitor shows goes to the previous one
    // instead. Without a previous one the Monitor stays where it is.
    private WorkspaceId BackAndForth(WorkspaceId target)
    {
        var monitor = FocusedId();
        if (_config.BindOptions.BackAndForth == 0 || target != _workspaces.Shown.ActiveOn(monitor))
            return target;

        return Previous(monitor, perMonitor: _config.BindOptions.BackAndForth == 2) ?? target;
    }

    private WorkspaceId? Previous(MonitorId monitor, bool perMonitor) => _workspaces.Shown.Previous(monitor, perMonitor);

    private WorkspaceAddress.Context Context() => Context(FocusedId());

    private WorkspaceAddress.Context Context(MonitorId monitor)
    {
        var active = _workspaces.Shown.ActiveOn(monitor);
        if (!active.IsSpecial && active.Monitor != monitor.Value)
            active = new WorkspaceId(active.Name, monitor.Value);

        var listed = _workspaces.Listed(monitor);
        return new WorkspaceAddress.Context(
            active,
            () => Previous(monitor, perMonitor: false),
            () => Previous(monitor, perMonitor: true),
            listed,
            listed,
            number => _workspaces.NumberIsEmpty(number, monitor));
    }

    private void DropIfEmpty(WorkspaceId id) => _workspaces.DropIfEmpty(id, _workspaces.Shown.IsShown(id));

    private void Rebind(LiveWorkspace desk, WorkspaceId to) => Readdressed(_workspaces.Rebind(desk, to));

    // A renamed Workspace keeps its place in the initial-workspace rules and is reported once the step ends.
    private void Readdressed(WorkspaceRename? rename)
    {
        if (rename is not WorkspaceRename (var from, var to))
            return;

        _initialWorkspaces.Readdress(from, to);
        (_readdressed ??= []).Add((from, to));
    }

    private void Readdressed(IReadOnlyList<WorkspaceRename> renames)
    {
        foreach (var rename in renames)
            Readdressed(rename);
    }

    private void DropEmpty()
    {
        foreach (var desk in _workspaces.ToArray())
            DropIfEmpty(desk.Id);
    }

}
