namespace Winland.Application;

public sealed partial class WindowManager
{
    private Outcome Decorate(Outcome outcome, State before)
    {
        var refit = MatchRulesAgain(out var tagsChanged);
        if (refit.Count > 0 || tagsChanged)
            outcome = outcome with { Placements = Union(outcome.Placements, refit, outcome.State), State = Snapshot() };

        var restack = SyncFullscreenHiding();
        outcome = outcome with { State = WithHiding(outcome.State) };
        if (restack.Count > 0)
            outcome = outcome with { ZOrder = [.. outcome.ZOrder, .. restack] };

        var above = FloatingAboveTiles();
        if (above.Count > 0)
            outcome = outcome with { ZOrder = [.. outcome.ZOrder, .. above] };

        var active = ActiveWindow();
        var hidden = new HashSet<WindowId>(outcome.State.Hidden.Select(client => client.Window));
        var decorations = new List<Decoration>(outcome.State.Clients.Count);
        foreach (var client in outcome.State.Clients)
        {
            var borderless = client.Fullscreen == FullscreenMode.Fullscreen;
            var border = BorderOf(client.Window, client.Window == active);
            var corners = CornersFor(client);
            var choice = PropOpacity(client.Window, OpacityOf(client.Window));
            var alpha = DecorationAlpha.Compute(
                _config.Decoration,
                new OpacityInput(client.Window == active, Fullscreen: borderless),
                choice.Rule,
                choice.Opaque);
            var shape = borderless ? CornerChoice.Square : corners;
            var noBlur = OwnProp(client.Window, "no_blur") is true;
            decorations.Add(new Decoration(
                client.Id,
                border,
                shape.Preference,
                alpha,
                borderless)
            {
                Region = shape.Region,
                Opacity = alpha,
                Blur = _config.Decoration.Blur && !noBlur,
                Ring = borderless || hidden.Contains(client.Window) ? null : RingFor(client, client.Window == active, shape, alpha),
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
        outcome = Animate(
            outcome with
            {
                Placements = WithHiddenMembers(BelowBars(outcome.Placements, outcome.State, bars)),
                GroupBars = bars,
                Decorations = decorations,
                ResetDecorations = reset,
            },
            before);
        return outcome with
        {
            Tray = TrayIcon.For(_config.Session.ShowTray, outcome.State.ActiveWorkspace),
            ConfigNotice = _notice,
        };
    }

    // A hidden Group member takes the frame of the shown member whenever that one is placed, as in the pinned
    // release, so it is already in place when the Group shows it. A hidden window stays hidden when it is moved.
    private IReadOnlyList<Placement> WithHiddenMembers(IReadOnlyList<Placement> placements)
    {
        List<Placement>? added = null;
        foreach (var desk in _workspaces)
        {
            foreach (var group in desk.Groups)
            {
                if (group.Members.Count < 2 || !TryPlacement(placements, new ClientId(group.Shown.Value), out var shown))
                    continue;

                foreach (var member in group.Members)
                {
                    if (member != group.Shown && desk.IsWaiting(member) && !TryPlacement(placements, new ClientId(member.Value), out _))
                        (added ??= []).Add(new Placement(new ClientId(member.Value), shown));
                }
            }
        }

        return added is null ? placements : [.. placements, .. added];
    }

    private static bool TryPlacement(IReadOnlyList<Placement> placements, ClientId client, out Rectangle bounds)
    {
        foreach (var placement in placements)
        {
            if (placement.Client == client)
            {
                bounds = placement.Bounds;
                return true;
            }
        }

        bounds = default;
        return false;
    }

    private Outcome View(
        IReadOnlyList<Placement> placements,
        IReadOnlyList<WindowId>? show = null,
        IReadOnlyList<WindowId>? hide = null,
        bool consumed = false,
        string? dispatcher = null,
        WindowId? close = null,
        WindowId? focus = null)
    {
        var snapshot = Snapshot();
        placements = Cover(WithMoved(placements), snapshot);
        var minimized = PlanMinimized(snapshot);
        return new(placements, snapshot, consumed, dispatcher, focus)
        {
            Close = close,
            Show = show ?? [],
            Hide = Combine(hide, minimized.Hide),
            ShowMinimized = minimized.Show,
            Launches = TakeBorn(),
        };
    }

    // A Layout that moved its tiles by itself, as scrolling does when focus scrolls its strip, has its placements sent with
    // the outcome, over any older ones for the same Clients.
    private IReadOnlyList<Placement> WithMoved(IReadOnlyList<Placement> placements)
    {
        List<Placement>? merged = null;
        foreach (var desk in _workspaces)
        {
            if (!desk.Layout.TakeMoved() || !IsShown(desk.Id))
                continue;

            merged ??= [.. placements];
            foreach (var placement in desk.Layout.Placed())
            {
                var index = merged.FindIndex(existing => existing.Client == placement.Client);
                if (index >= 0)
                    merged[index] = placement;
                else
                    merged.Add(placement);
            }
        }

        return merged ?? placements;
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
        foreach (var desk in _workspaces)
        {
            foreach (var client in desk.Layout.Clients)
            {
                var slot = desk.Layout.TryBounds(client.Window, out var found) ? found : client.Bounds;
                var pinned = client.Floating && _pinned.Contains(client.Window);
                clients.Add(client with
                {
                    Bounds = PlacedBounds(client, desk, slot),
                    Pinned = pinned,
                    PinFullscreened = _pinFullscreened.Contains(client.Window),
                    Tags = TagsOf(client.Window),
                    Content = ContentOf(client.Window),
                    AllowedOverFullscreen = OverFullscreenFlag(client.Window),
                    BelowFullscreen = _belowFullscreen.Contains(client.Window),
                });
            }

            foreach (var group in desk.Groups)
            {
                var tile = desk.Layout.TryBounds(group.Shown, out var shownBounds) ? shownBounds : default;
                var floating = desk.ClientOf(group.Shown)?.Floating == true;
                foreach (var member in group.Members)
                {
                    if (desk.IsWaiting(member) && desk.ClientOf(member) is { } waiting)
                        clients.Add(waiting with
                        {
                            Bounds = tile,
                            Floating = floating,
                        });
                }
            }

            workspaces.Add(new Workspace(desk.Id, desk.Monitor)
            {
                LastWindow = desk.Focus,
                Persistent = desk.Persistent,
                DisplayName = desk.DisplayName,
                TiledLayout = LayoutFactory.NameOf(desk.Layout),
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
            Stacking = [.. _stacking],
        };
    }

    // The allowedOverFullscreen flag and whether a covering Client keeps the window under it, as the last sync left them.
    private State WithHiding(State state)
    {
        var clients = new Client[state.Clients.Count];
        for (var i = 0; i < clients.Length; i++)
        {
            var client = state.Clients[i];
            clients[i] = client with
            {
                AllowedOverFullscreen = OverFullscreenFlag(client.Window),
                BelowFullscreen = _belowFullscreen.Contains(client.Window),
            };
        }

        return state with { Clients = clients };
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
        bool focusRevealed = false)
    {
        // A w[] workspace rule counts the Clients, so a Client that opens, closes, floats or moves can match another
        // rule. Winland reads its rules again whenever a window changes its workspace's layout.
        placements = Restamp(desk, placements);
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

        return View(placements, showList, hideList, consumed, dispatcher, close, focus);
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

    // The border Winland draws outside a Client: its own border size, its colors, and corners that follow the
    // window's. A minimized Client, and one with decorate 0, has none.
    private BorderRing? RingFor(Client client, bool active, CornerChoice shape, byte alpha)
    {
        if (client.Minimized || OwnProp(client.Window, "decorate") is false || FindWorkspace(client.Workspace) is not LiveWorkspace desk)
            return null;

        var thickness = desk.Options.BorderFor(client.Id);
        if (thickness <= 0)
            return null;

        var scale = MonitorById(desk.Monitor) is { Effective.Enabled: true } screen ? screen.Effective.Scale : 1;
        var (radius, power) = CornerRadius(shape, scale);
        return new BorderRing(BorderOf(client.Window, active), thickness, radius, power, alpha);
    }

    private static (int Radius, float Power) CornerRadius(CornerChoice shape, double scale) => shape switch
    {
        { Region: CornerRegion region } => (region.Radius, region.Power),
        { Preference: CornerPreference.Round } => ((int)Math.Round(CornerShape.Round * scale), 2f),
        { Preference: CornerPreference.RoundSmall } => ((int)Math.Round(CornerShape.RoundSmall * scale), 2f),
        _ => (0, 2f),
    };

    private CornerChoice CornersFor(Client client)
    {
        var desk = FindWorkspace(client.Workspace);
        var rounding = OwnProp(client.Window, "rounding") is long set ? (int)Math.Min(set, int.MaxValue) : _config.Decoration.Rounding;
        if (OwnProp(client.Window, "rounding") is null && desk is not null && RuleFor(desk)?.Rounding == false)
            return CornerChoice.Square;

        var power = OwnProp(client.Window, "rounding_power") is float setPower ? Math.Clamp(setPower, 1f, 10f) : _config.Decoration.RoundingPower;
        var screen = desk is null ? null : MonitorById(desk.Monitor);
        var scale = screen is { Effective.Enabled: true } ? screen.Effective.Scale : 1;
        return CornerShape.For(rounding, power, scale);
    }
}
