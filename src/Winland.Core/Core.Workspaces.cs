namespace Winland;

public sealed partial class Core
{
    // A floating Client keeps its place relative to the Monitor when it moves onto a Workspace on another Monitor,
    // as in the pinned release.
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
        bool allowsElevation = false,
        WindowId? chosen = null,
        bool windowChosen = false)
    {
        var window = windowChosen ? chosen : ActiveWindow();
        if (window is not WindowId id || !WorkspaceAddress.TryResolve(argument, Context(), out var resolved))
            return View([], consumed: consumed, dispatcher: dispatcher, allowsElevation: allowsElevation);

        var target = Alias(resolved);

        // A pinned Client is never moved onto a special workspace, as in the pinned release.
        var source = WorkspaceContaining(id);
        if (source is null || source.Id == target || (target.IsSpecial && _pinned.Contains(id)))
            return View([], consumed: consumed, dispatcher: dispatcher, allowsElevation: allowsElevation);

        var sourceCovered = source.Covered();
        var wasHidden = !IsShown(source.Id) || sourceCovered.Contains(id);
        if (!source.Take(id, out var taken))
            return View([], consumed: consumed, dispatcher: dispatcher, allowsElevation: allowsElevation);

        var destination = WorkspaceFor(target, FocusedId());
        var elsewhere = target.IsSpecial ? null : ShownElsewhere(target);
        if (!silent && !target.IsSpecial && elsewhere is null && MonitorById(FocusedId()) is { } focused)
            BringTo(focused, destination);

        var destinationCovered = destination.Covered();
        var solitary = source.GroupOf(id) is null ? SolitaryGroup(destination, id) : null;
        var area = WorkArea(destination, _area);
        destination.Receive(id, OntoMonitor(taken, source.Monitor, destination.Monitor), area);
        CarryGroup(source, destination, id);
        JoinOnMove(destination, solitary, id);
        Restamp(source, []);
        Restamp(destination, []);

        if (target.IsSpecial)
            return FinishSpecialMove(source, destination, id, silent, consumed, dispatcher, allowsElevation, sourceCovered, destinationCovered);

        if (!silent && source.Id.IsSpecial && SpecialOn(source.Monitor) == source.Id)
        {
            var concealed = ConcealSpecial(source.Monitor);
            return FollowMove(SwitchTo(target, id, consumed, dispatcher, allowsElevation), id, concealed, destination);
        }

        if (!silent && elsewhere is { } owner)
        {
            var followed = FocusShown(owner, target, id, consumed, dispatcher, allowsElevation);
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
            var followedSwitch = SwitchTo(target, id, consumed, dispatcher, allowsElevation);
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

        return View(placements, revealed, hidden, consumed, dispatcher, allowsElevation: allowsElevation);

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

    private Outcome SwitchTo(WorkspaceId target, WindowId? focus, bool consumed, string? dispatcher, bool allowsElevation = false, bool pull = false)
    {
        if (target.IsSpecial)
            return OpenSpecial(target, focus, dispatcher, allowsElevation);

        var bound = BoundMonitor(target);
        if (!pull && bound is null && ShownElsewhere(target) is { } elsewhere)
        {
            var away = SpecialHiddenBy(elsewhere.Id, focus);
            var followed = FocusShown(elsewhere, target, focus, consumed, dispatcher, allowsElevation);
            return away.Length == 0 ? followed : followed with { Hide = Combine(followed.Hide, away) };
        }

        if (bound is not null && bound.Id != FocusedId())
            Focus(bound.Id);

        var monitor = FocusedId();
        var concealed = SpecialHiddenBy(monitor, focus);
        if (bound is not null)
        {
            if (FindWorkspace(target) is { } held && held.Monitor != bound.Id)
            {
                held.Monitor = bound.Id;
                Refit(held);
            }
        }
        else if (MonitorById(monitor) is { } screen && FindWorkspace(target) is { } existing)
            BringTo(screen, existing);

        var sourceId = ActiveOn(monitor);
        if (sourceId == target)
        {
            if (focus is WindowId requested && ClientOn(target, requested) is not null
                && (SpecialOn(monitor) is null || _input.SpecialFallthrough))
            {
                _active = requested;
                FocusedWorkspace().Focus = requested;
            }

            return View([], hide: concealed, consumed: consumed, dispatcher: dispatcher, allowsElevation: allowsElevation);
        }

        var keep = _active is WindowId current
            && _pinned.Contains(current)
            && ClientOn(sourceId, current) is { Floating: true }
            ? current
            : (WindowId?)null;
        _activeOn[monitor] = target;
        if (MonitorById(monitor) is { } owner)
            owner.Active = target;

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
            dispatcher,
            allowsElevation: allowsElevation);
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

    private Outcome FocusShown(LiveMonitor owner, WorkspaceId target, WindowId? focus, bool consumed, string? dispatcher, bool allowsElevation)
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

        return View([], focus: next, consumed: consumed, dispatcher: dispatcher, allowsElevation: allowsElevation);
    }

    private void BringTo(LiveMonitor screen, LiveWorkspace desk)
    {
        if (desk.Monitor == screen.Id)
            return;

        var owner = MonitorById(desk.Monitor);
        var moving = desk.Id;
        var leaving = owner is not null && owner.Active == moving;
        desk.Monitor = screen.Id;
        Refit(desk);
        if (owner is null || !leaving)
            return;

        owner.Active = null;
        AssignActives(EnabledMonitors());
        SyncActive();
        if (owner.Active is not WorkspaceId stay || stay == moving)
            return;

        var pinned = PinnedFloating(desk);
        if (pinned.Count == 0)
            return;

        var home = WorkspaceFor(stay, owner.Id);
        foreach (var window in pinned)
            Transfer(desk, home, window);
    }

    private void Remember(WorkspaceId workspace, MonitorId monitor)
    {
        if (!_allowWorkspaceCycles && _history.Count > 0 && _history[0].Workspace == workspace)
            return;

        _history.RemoveAll(visit => visit.Workspace == workspace);
        _history.Insert(0, new WorkspaceVisit(workspace, monitor));
    }

    // A switch to a Workspace another Monitor shows moves the cursor onto that Monitor, as in the pinned release, to the
    // middle of the Client it focuses or of the Monitor. cursor:no_warps stops it, as it stops the pinned release's warp.
    // A cursor option that warps after a switch goes first.
    private void NoteCrossMonitorWarp(MonitorId from, Outcome switched)
    {
        _crossMonitorWarp = null;
        var owner = FocusedId();
        if (owner == from || MonitorById(owner) is not LiveMonitor screen)
            return;

        var bounds = screen.Bounds;
        if (_workspaceCenterOn == 1 && _active is WindowId window)
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
    // instead, as in the pinned release. Without a previous one the Monitor stays where it is.
    private WorkspaceId BackAndForth(WorkspaceId target)
    {
        var monitor = FocusedId();
        if (_backAndForth == 0 || target != ActiveOn(monitor))
            return target;

        return Previous(monitor, perMonitor: _backAndForth == 2) ?? target;
    }

    private WorkspaceId? Previous(MonitorId monitor, bool perMonitor)
    {
        var active = ActiveOn(monitor);
        var index = _history.FindIndex(visit => visit.Workspace == active);
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

    private WorkspaceAddress.Context Context(MonitorId monitor) =>
        new(
            ActiveOn(monitor),
            Previous(monitor, perMonitor: false),
            Previous(monitor, perMonitor: true),
            Listed(monitor),
            Listed(null),
            NumberIsEmpty,
            number => OwnedByOtherMonitor(number, monitor));

    private IReadOnlyList<WorkspaceId> Listed(MonitorId? only)
    {
        var named = new List<WorkspaceId>();
        var numbered = new List<(int Number, WorkspaceId Id)>();
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

    private bool OwnedByOtherMonitor(int number, MonitorId monitor)
    {
        var desk = FindWorkspace(WorkspaceId.FromNumber(number));
        return desk is not null && desk.Monitor != monitor;
    }

    private bool NumberIsEmpty(int number) =>
        FindWorkspace(WorkspaceId.FromNumber(number)) is not LiveWorkspace desk || desk.Layout.Clients.Count == 0;

    private void DropIfEmpty(WorkspaceId id)
    {
        if (IsShown(id))
            return;

        var desk = FindWorkspace(id);
        if (desk is null || desk.Persistent || desk.Layout.Clients.Count > 0)
            return;

        _workspaces.Remove(desk);
        ForgetReturn(id);
    }

    private WorkspaceId NextFree(MonitorId monitor)
    {
        for (var number = 1; number < int.MaxValue; number++)
        {
            var id = WorkspaceId.FromNumber(number);
            if (FindWorkspace(id) is not null || BoundElsewhere(id, monitor))
                continue;

            return id;
        }

        throw new InvalidOperationException("No workspace id remains.");
    }

    private void DropEmpty()
    {
        foreach (var desk in _workspaces.ToArray())
            DropIfEmpty(desk.Id);
    }

    private WorkspaceId Alias(WorkspaceId id)
    {
        if (FindWorkspace(id) is not null)
            return id;

        foreach (var desk in _workspaces)
        {
            if (!desk.Renamed && desk.DisplayName == id.Name)
                return desk.Id;
        }

        return id;
    }

    private readonly record struct WorkspaceVisit(WorkspaceId Workspace, MonitorId Monitor);
}
