namespace Winland.Application;

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
        if (source is not null && target.IsSpecial && _pinned.Contains(id))
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
        var wasHidden = !IsShown(source.Id) || sourceCovered.Contains(id);
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

        if (!silent && source.Id.IsSpecial && SpecialOn(source.Monitor) == source.Id)
        {
            var concealed = ConcealSpecial(source.Monitor);
            return FollowMove(SwitchTo(target, id, consumed, dispatcher), id, concealed, destination);
        }

        if (!silent && elsewhere is { } owner)
        {
            var followed = FocusShown(owner, target, id, consumed, dispatcher);
            var show = new List<WindowId>();
            var conceal = new List<WindowId>();
            if (IsShown(source.Id))
                source.Collect(sourceCovered, show, conceal);
            if (IsShown(destination.Id))
                Arrive(show, conceal);
            var moved = new List<Placement>();
            if (IsShown(source.Id))
                moved.AddRange(source.Layout.Placed());
            if (IsShown(destination.Id))
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
            var here = ActiveOn(FocusedId()) == target;
            var followedSwitch = SwitchTo(target, id, consumed, dispatcher);
            if (here)
            {
                var show = new List<WindowId>(followedSwitch.Show);
                var conceal = new List<WindowId>(followedSwitch.Hide);
                if (IsShown(source.Id))
                    source.Collect(sourceCovered, show, conceal);
                Arrive(show, conceal);
                var moved = new List<Placement>();
                if (IsShown(source.Id))
                    moved.AddRange(source.Layout.Placed());
                moved.AddRange(destination.Layout.Visible());
                followedSwitch = followedSwitch with { Placements = moved, Show = show, Hide = conceal };
            }
            else if (IsShown(source.Id))
            {
                // The Workspace the window left is still shown on its own Monitor, so it is laid out again too.
                followedSwitch = followedSwitch with { Placements = [.. source.Layout.Placed(), .. followedSwitch.Placements] };
            }

            return followedSwitch.Focus is null && _active == id
                ? followedSwitch with { Focus = id }
                : followedSwitch;
        }

        var revealed = new List<WindowId>();
        var hidden = new List<WindowId>();
        if (IsShown(source.Id))
            source.Collect(sourceCovered, revealed, hidden);
        if (IsShown(destination.Id))
            Arrive(revealed, hidden);
        else
            hidden.Add(id);

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

        if (_active == id)
        {
            _active = revealed.Count == 1 && source.ClientOf(revealed[0]) is not null
                ? revealed[0]
                : source.LastVisible();
            source.Focus = _active;
        }

        var placements = new List<Placement>();
        if (IsShown(source.Id))
            placements.AddRange(source.Layout.Placed());
        if (IsShown(destination.Id))
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
        if (IsShown(destination.Id) && !show.Contains(window))
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
        if (FindWorkspace(target) is { } located && MonitorById(located.Monitor) is { Effective.Enabled: true } home && home.Id != FocusedId())
            Focus(home.Id);

        var monitor = FocusedId();
        var concealed = SpecialHiddenBy(monitor, focus);

        var sourceId = ActiveOn(monitor);
        if (sourceId == target)
        {
            if (focus is WindowId requested && ClientOn(target, requested) is not null
                && (SpecialOn(monitor) is null || _input.SpecialFallthrough))
            {
                _active = requested;
                FocusedWorkspace().Focus = requested;
            }

            return View([], hide: concealed, consumed: consumed, dispatcher: dispatcher);
        }

        var keep = _active is WindowId current
            && _pinned.Contains(current)
            && ClientOn(sourceId, current) is { Floating: true }
            ? current
            : (WindowId?)null;
        _activeOn[monitor] = target;
        if (MonitorById(monitor) is { } owner)
            owner.Active = target;

        NoteWorkspaceActive(target);
        Remember(target, monitor);
        var destination = WorkspaceFor(target, monitor);
        CarryPinned(sourceId, destination);
        var hide = VisibleIds(sourceId);
        var keepSpecial = SpecialOn(monitor) is not null && focus is null;
        if (!keepSpecial)
        {
            _active = focus is WindowId requestedFocus && ClientOn(target, requestedFocus) is not null
                ? requestedFocus
                : keep is WindowId pinnedFocus && ClientOn(target, pinnedFocus) is not null
                    ? pinnedFocus
                    : destination.Remembered();
            destination.Focus = _active;
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
        var desk = FindWorkspace(target);
        if (desk is null)
            return null;

        var owner = MonitorById(desk.Monitor);
        if (owner is null || !owner.Effective.Enabled || owner.Active != target || owner.Id == FocusedId())
            return null;

        return owner;
    }

    private Outcome FocusShown(LiveMonitor owner, WorkspaceId target, WindowId? focus, bool consumed, string? dispatcher)
    {
        Focus(owner.Id);
        var destination = FindWorkspace(target);
        WindowId? next = null;
        if (focus is WindowId requested && ClientOn(target, requested) is not null)
            next = requested;
        else if (destination is not null)
            next = destination.Remembered();

        _active = next;
        if (destination is not null)
            destination.Focus = next;

        return View([], focus: next, consumed: consumed, dispatcher: dispatcher);
    }

    // The visits are Winland's workspace history: newest first, each with the Monitor it was on. A visit to a
    // Workspace that was removed since stays, as a gone one, until the history is collected.
    private void Remember(WorkspaceId workspace, MonitorId monitor)
    {
        if (!_bindOptions.AllowWorkspaceCycles && _history.Count > 0 && _history[0] is { Gone: false } newest && newest.Workspace == workspace)
            return;

        _history.RemoveAll(visit => !visit.Gone && visit.Workspace == workspace);
        _history.Insert(0, new WorkspaceVisit(workspace, monitor));
    }

    private void ForgetVisits(WorkspaceId workspace)
    {
        for (var i = 0; i < _history.Count; i++)
        {
            if (_history[i].Workspace == workspace)
                _history[i] = _history[i] with { Gone = true };
        }
    }

    // Drops the visits to removed Workspaces as Winland collects its history: the newest visit on each
    // Monitor is kept until that Monitor has been seen past the first one in the list, so it can still be gone to.
    private void CollectVisits()
    {
        var seen = new List<MonitorId>();
        _history.RemoveAll(visit =>
        {
            for (var i = 1; i < seen.Count; i++)
            {
                if (seen[i] == visit.Monitor)
                    return visit.Gone;
            }

            seen.Add(visit.Monitor);
            return false;
        });
    }

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
        if (_bindOptions.WorkspaceCenterOn == 1 && _active is WindowId window)
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
        if (_bindOptions.BackAndForth == 0 || target != ActiveOn(monitor))
            return target;

        return Previous(monitor, perMonitor: _bindOptions.BackAndForth == 2) ?? target;
    }

    private WorkspaceId? Previous(MonitorId monitor, bool perMonitor)
    {
        CollectVisits();
        var active = ActiveOn(monitor);
        var index = _history.FindIndex(visit => !visit.Gone && visit.Workspace == active);
        if (!perMonitor)
            return index >= 0 && index + 1 < _history.Count ? _history[index + 1].Workspace : null;

        for (var i = index < 0 ? 0 : index + 1; i < _history.Count; i++)
        {
            if (_history[i].Monitor == monitor)
                return _history[i].Workspace;
        }

        return null;
    }

    private WorkspaceAddress.Context Context() => Context(FocusedId());

    private WorkspaceAddress.Context Context(MonitorId monitor)
    {
        var active = ActiveOn(monitor);
        if (!active.IsSpecial && active.Monitor != monitor.Value)
            active = new WorkspaceId(active.Name, monitor.Value);

        var listed = Listed(monitor);
        return new WorkspaceAddress.Context(
            active,
            () => Previous(monitor, perMonitor: false),
            () => Previous(monitor, perMonitor: true),
            listed,
            listed,
            number => NumberIsEmpty(number, monitor));
    }

    private IReadOnlyList<WorkspaceId> Listed(MonitorId? only)
    {
        var named = new List<WorkspaceId>();
        var numbered = new List<(long Number, WorkspaceId Id)>();
        foreach (var desk in _workspaces)
        {
            if (desk.Id.IsSpecial)
                continue;

            if (only is MonitorId monitor && desk.Monitor != monitor)
                continue;

            if (desk.Id.TryGetNumber(out var number))
                numbered.Add((number, desk.Id));
            else
                named.Add(desk.Id);
        }

        named.Reverse();
        numbered.Sort((left, right) => left.Number.CompareTo(right.Number));
        foreach (var (_, id) in numbered)
            named.Add(id);

        return named;
    }

    private bool NumberIsEmpty(long number, MonitorId monitor) =>
        FindWorkspace(WorkspaceId.FromNumber(number, monitor)) is not LiveWorkspace desk || desk.Layout.Clients.Count == 0;

    private void DropIfEmpty(WorkspaceId id)
    {
        if (IsShown(id))
            return;

        var desk = FindWorkspace(id);
        if (desk is null || desk.Persistent || desk.Layout.Clients.Count > 0)
            return;

        RemoveWorkspace(desk);
    }

    private void RemoveWorkspace(LiveWorkspace desk)
    {
        _workspaces.Remove(desk);
        ForgetVisits(desk.Id);
        foreach (var screen in _monitors)
        {
            if (screen.Active == desk.Id)
                screen.Active = null;
        }

        foreach (var (monitor, active) in _activeOn.ToArray())
        {
            if (active == desk.Id)
                _activeOn.Remove(monitor);
        }
    }

    private WorkspaceId NextFree(MonitorId monitor) => WorkspaceId.FromNumber(FirstFree(monitor), monitor);

    // The number a Workspace keeps on a Monitor it moves to: its own when that number is free there,
    // otherwise the first free one. <paramref name="alsoLeaving"/> does not occupy a number.
    private long NumberOn(LiveWorkspace desk, MonitorId monitor, LiveWorkspace alsoLeaving)
    {
        var preferred = desk.Id.TryGetNumber(out var number) ? number : 1;
        var occupant = FindWorkspace(WorkspaceId.FromNumber(preferred, monitor));
        if (occupant is null || occupant == desk || occupant == alsoLeaving)
            return preferred;

        for (var candidate = 1L; candidate < WorkspaceId.MaxNumber; candidate++)
        {
            var found = FindWorkspace(WorkspaceId.FromNumber(candidate, monitor));
            if (found is null || found == desk || found == alsoLeaving)
                return candidate;
        }

        throw new InvalidOperationException("No workspace id remains.");
    }

    private long FirstFree(MonitorId monitor)
    {
        for (var number = 1L; number < WorkspaceId.MaxNumber; number++)
        {
            if (FindWorkspace(WorkspaceId.FromNumber(number, monitor)) is null)
                return number;
        }

        throw new InvalidOperationException("No workspace id remains.");
    }

    // The next Guest number is one past the highest number already on the Monitor, and at least 11 when
    // 1 to 10 are all in use.
    private long NextGuestNumber(MonitorId monitor)
    {
        long highest = 0;
        var occupied = new HashSet<long>();
        foreach (var desk in _workspaces)
        {
            if (desk.Monitor != monitor || desk.Id.IsSpecial || !desk.Id.TryGetNumber(out var number))
                continue;

            occupied.Add(number);
            if (number > highest)
                highest = number;
        }

        var candidate = highest + 1;
        var full = true;
        for (var number = 1L; number <= 10; number++)
        {
            if (occupied.Contains(number))
                continue;

            full = false;
            break;
        }

        if (full && candidate < 11)
            candidate = 11;

        while (occupied.Contains(candidate))
            candidate++;

        return candidate;
    }

    private void Rebind(LiveWorkspace desk, WorkspaceId to)
    {
        var from = desk.Id;
        if (from == to)
        {
            if (!to.IsSpecial)
                desk.Monitor = new MonitorId(to.Monitor);

            return;
        }

        desk.Readdress(to);
        if (!to.IsSpecial)
            desk.Monitor = new MonitorId(to.Monitor);

        foreach (var screen in _monitors)
        {
            if (screen.Active == from)
                screen.Active = to;
            if (screen.Special == from)
                screen.Special = to;
        }

        foreach (var (monitor, active) in _activeOn.ToArray())
        {
            if (active == from)
                _activeOn[monitor] = to;
        }

        for (var i = 0; i < _history.Count; i++)
        {
            if (!_history[i].Gone && _history[i].Workspace == from)
                _history[i] = _history[i] with { Workspace = to };
        }

        _initialWorkspaces.Readdress(from, to);
        (_readdressed ??= []).Add((from, to));
    }

    private void DropEmpty()
    {
        foreach (var desk in _workspaces.ToArray())
            DropIfEmpty(desk.Id);
    }

    private readonly record struct WorkspaceVisit(WorkspaceId Workspace, MonitorId Monitor, bool Gone = false);
}
