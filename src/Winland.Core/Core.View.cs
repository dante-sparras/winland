namespace Winland;

public sealed partial class Core
{
    private Outcome Decorate(Outcome outcome, State before)
    {
        var active = ActiveWindow();
        var decorations = new List<Decoration>(outcome.State.Clients.Count);
        foreach (var client in outcome.State.Clients)
        {
            var borderless = client.Fullscreen == FullscreenMode.Fullscreen;
            var border = BorderOf(client.Window, client.Window == active);
            var corners = CornersFor(client);
            var choice = OpacityOf(client.Window);
            var special = FindWorkspace(client.Workspace) is LiveWorkspace home ? SpecialOn(home.Monitor) : null;
            var alpha = DecorationAlpha.Compute(
                _decoration,
                new OpacityInput(
                    client.Window == active,
                    Fullscreen: borderless,
                    SpecialOpen: special is not null,
                    OnSpecial: special == client.Workspace),
                choice.Rule,
                choice.Opaque);
            var shape = borderless ? CornerChoice.Square : corners;
            decorations.Add(new Decoration(
                client.Id,
                border,
                shape.Preference,
                alpha,
                borderless)
            {
                Region = shape.Region,
                Blur = _decoration.Blur,
                Shadow = _decoration.Shadow,
            });
        }

        var present = new HashSet<WindowId>(outcome.State.Clients.Count);
        foreach (var client in outcome.State.Clients)
            present.Add(client.Window);

        var reset = new List<WindowId>();
        foreach (var client in before.Clients)
        {
            if (!present.Contains(client.Window))
                reset.Add(client.Window);
        }

        // Bars are added here, once per finished Outcome, so every placement passes below them exactly once.
        var bars = GroupBars(outcome.State);
        return Animate(
            outcome with
            {
                Placements = BelowBars(outcome.Placements, outcome.State, bars),
                GroupBars = bars,
                Decorations = decorations,
                ResetDecorations = reset,
            },
            before);
    }

    private Outcome View(
        IReadOnlyList<Placement> placements,
        IReadOnlyList<WindowId>? show = null,
        IReadOnlyList<WindowId>? hide = null,
        bool consumed = false,
        string? dispatcher = null,
        WindowId? close = null,
        WindowId? focus = null,
        bool allowsElevation = false)
    {
        var snapshot = Snapshot();
        placements = Cover(placements, snapshot);
        var minimized = PlanMinimized(snapshot);
        var swallowed = TakeSwallowChanges();
        return new(placements, snapshot, consumed, dispatcher, close, focus)
        {
            Show = Combine(show, swallowed.Show),
            Hide = Combine(Combine(hide, minimized.Hide), swallowed.Hide),
            ShowMinimized = minimized.Show,
            AllowsElevation = allowsElevation,
            Launch = TakeBorn(),
        };
    }

    private MinimizedPlan PlanMinimized(State snapshot)
    {
        var hidden = new HashSet<WindowId>(snapshot.Hidden.Count);
        foreach (var client in snapshot.Hidden)
            hidden.Add(client.Window);

        var presence = new MinimizedPresence[snapshot.Clients.Count];
        for (var i = 0; i < snapshot.Clients.Count; i++)
        {
            var client = snapshot.Clients[i];
            presence[i] = new MinimizedPresence(client.Window, !hidden.Contains(client.Window), client.Minimized);
        }

        var plan = MinimizedConcealment.Plan(presence, _concealedMinimized);
        _concealedMinimized.Clear();
        foreach (var window in plan.Concealed)
            _concealedMinimized.Add(window);
        return plan;
    }

    private static WindowId[] Combine(IReadOnlyList<WindowId>? primary, IReadOnlyList<WindowId> extra)
    {
        var first = primary ?? [];
        if (extra.Count == 0)
            return first as WindowId[] ?? [.. first];

        var combined = new List<WindowId>(first.Count + extra.Count);
        combined.AddRange(first);
        foreach (var window in extra)
        {
            if (!combined.Contains(window))
                combined.Add(window);
        }

        return [.. combined];
    }

    private State Snapshot()
    {
        var clients = new List<Client>();
        var workspaces = new List<Workspace>(_workspaces.Count);
        var active = ActiveWindow();
        foreach (var desk in _workspaces)
        {
            var shown = IsShown(desk.Id);
            foreach (var client in desk.Layout.Clients)
            {
                var slot = desk.Layout.TryBounds(client.Window, out var found) ? found : client.Bounds;
                var pinned = client.Floating && _pinned.Contains(client.Window);
                clients.Add(client with
                {
                    Bounds = PlacedBounds(client, desk, slot),
                    Pinned = pinned,
                    Swallowing = SwallowedBy(client.Window),
                    InhibitingIdle = InhibitsIdle(client, shown, active),
                });
            }

            foreach (var group in desk.Groups)
            {
                var tile = desk.Layout.TryBounds(group.Shown, out var shownBounds) ? shownBounds : default;
                var floating = desk.ClientOf(group.Shown)?.Floating == true;
                foreach (var member in group.Members)
                {
                    if (desk.IsWaiting(member) && desk.ClientOf(member) is { } waiting)
                        clients.Add(waiting with { Bounds = tile, Floating = floating });
                }
            }

            workspaces.Add(new Workspace(desk.Id, desk.Monitor)
            {
                LastWindow = desk.Focus,
                Persistent = desk.Persistent,
                DisplayName = desk.DisplayName,
            });
        }

        var groups = new List<IReadOnlyList<WindowId>>();
        foreach (var desk in _workspaces)
        {
            foreach (var group in desk.Groups)
                groups.Add(group.Members.ToArray());
        }

        var hidden = new List<Client>();
        foreach (var client in clients)
        {
            if ((!IsShown(client.Workspace) && !StaysVisible(client)) || IsWaiting(client.Window))
                hidden.Add(client);
        }

        AddSwallowed(clients, hidden);

        var monitors = new List<Monitor>(_monitors.Count);
        var screens = new List<MonitorSnapshot>(_monitors.Count);
        var focused = FocusedId();
        foreach (var screen in _monitors)
        {
            monitors.Add(screen.Effective);
            screens.Add(new MonitorSnapshot(
                screen.Id,
                screen.Effective,
                screen.Active ?? WorkspaceId.Initial,
                screen.Id == focused && screen.Effective.Enabled)
            {
                SpecialWorkspace = screen.Special,
            });
        }

        return new State(clients)
        {
            Workspaces = workspaces,
            ActiveWorkspace = ActiveId(),
            SpecialWorkspace = SpecialOn(focused),
            ActiveMonitor = focused,
            Hidden = hidden,
            Monitors = monitors,
            Screens = screens,
            Groups = groups,
        };
    }

    private WindowId[] VisibleIds(WorkspaceId id) => FindWorkspace(id)?.VisibleWindows() ?? [];

    private Outcome Present(
        LiveWorkspace desk,
        HashSet<WindowId> covered,
        IReadOnlyList<Placement> placements,
        IReadOnlyList<WindowId>? show = null,
        IReadOnlyList<WindowId>? hide = null,
        bool consumed = false,
        string? dispatcher = null,
        WindowId? close = null,
        WindowId? focus = null,
        bool allowsElevation = false,
        bool focusRevealed = false)
    {
        var showList = new List<WindowId>(show ?? []);
        var hideList = new List<WindowId>(hide ?? []);
        desk.Collect(covered, showList, hideList);
        if (placements.Count == 0 && (showList.Count > 0 || hideList.Count > 0))
            placements = desk.Layout.Placed();

        if (focusRevealed && focus is null && showList.Count == 1)
        {
            focus = showList[0];
            _active = focus;
            desk.Focus = focus;
            desk.Layout.NoteFocus(showList[0]);
            TouchHistory(showList[0]);
        }

        return View(placements, showList, hideList, consumed, dispatcher, close, focus, allowsElevation);
    }

    private List<Placement> ShownPlacements()
    {
        var placements = new List<Placement>();
        if (_monitors.Count == 0)
        {
            placements.AddRange(FocusedWorkspace().Layout.Visible());
            WorkspaceId? overlayId = null;
            if (SpecialOn(MonitorId.Primary) is WorkspaceId primary && FindWorkspace(primary) is LiveWorkspace overlay)
            {
                overlayId = overlay.Id;
                placements.AddRange(overlay.Layout.Visible());
            }

            foreach (var desk in _workspaces)
            {
                if (desk.Id != ActiveId() && desk.Id != overlayId)
                    AddPinnedPlacements(desk, placements);
            }

            return placements;
        }

        foreach (var screen in _monitors)
        {
            if (!screen.Effective.Enabled || screen.Active is not WorkspaceId active)
                continue;

            var desk = FindWorkspace(active);
            if (desk is not null)
                placements.AddRange(desk.Layout.Visible());

            if (screen.Special is WorkspaceId special && FindWorkspace(special) is LiveWorkspace overlay)
                placements.AddRange(overlay.Layout.Visible());
        }

        foreach (var desk in _workspaces)
        {
            if (!IsShown(desk.Id))
                AddPinnedPlacements(desk, placements);
        }

        return placements;
    }

    private void AddPinnedPlacements(LiveWorkspace desk, List<Placement> placements)
    {
        if (!MonitorShowing(desk.Monitor))
            return;

        foreach (var client in desk.Layout.Clients)
        {
            if (!client.Floating || client.Minimized || !_pinned.Contains(client.Window))
                continue;

            if (desk.Layout.TryBounds(client.Window, out var bounds))
                placements.Add(new Placement(client.Id, bounds));
        }
    }

    private WindowId[] ShownWindows()
    {
        var windows = new List<WindowId>();
        foreach (var desk in _workspaces)
        {
            if (IsShown(desk.Id))
            {
                windows.AddRange(desk.VisibleWindows());
                continue;
            }

            if (!MonitorShowing(desk.Monitor))
                continue;

            foreach (var client in desk.Layout.Clients)
            {
                if (client.Floating && !client.Minimized && _pinned.Contains(client.Window))
                    windows.Add(client.Window);
            }
        }

        return windows.ToArray();
    }

    private WindowId[] HiddenWindows()
    {
        var windows = new List<WindowId>();
        foreach (var desk in _workspaces)
        {
            if (IsShown(desk.Id))
            {
                var covered = desk.Covered();
                foreach (var client in desk.Layout.Clients)
                {
                    if (covered.Contains(client.Window))
                        windows.Add(client.Window);
                }

                continue;
            }

            foreach (var client in desk.Layout.Clients)
            {
                if (client.Minimized)
                    continue;

                if (client.Floating && _pinned.Contains(client.Window) && MonitorShowing(desk.Monitor))
                    continue;

                windows.Add(client.Window);
            }

            foreach (var waiting in desk.WaitingClients)
                windows.Add(waiting.Window);
        }

        return windows.ToArray();
    }

    private CornerChoice CornersFor(Client client)
    {
        var desk = FindWorkspace(client.Workspace);
        if (desk is not null && RuleFor(desk)?.Rounding == false)
            return CornerChoice.Square;

        var screen = desk is null ? null : MonitorById(desk.Monitor);
        var scale = screen is { Effective.Enabled: true } ? screen.Effective.Scale : 1;
        return CornerShape.For(_decoration.Rounding, _decoration.RoundingPower, scale);
    }
}
