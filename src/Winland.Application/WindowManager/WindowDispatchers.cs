namespace Winland.Application;

/// <summary>Dispatchers that act on one window and the daemon: focus, float, swap, move to a Workspace, <c>closeall</c> and <c>exit</c>.</summary>
public sealed partial class WindowManager
{
    private Outcome FocusWindow(string arguments)
    {
        if (Select(arguments, emptyMeansActive: false) is not WindowId id)
            return Rejected("error: no such window");

        return FocusClient(id);
    }

    private Outcome FocusClient(WindowId id)
    {
        if (WorkspaceContaining(id) is not LiveWorkspace desk)
            return Rejected("error: no such window");

        // A window shadowed by a modal refuses the focus, though its Workspace is still shown.
        if (ShadowedByModal(id))
        {
            return _workspaces.Shown.IsShown(desk.Id)
                ? View([])
                : SwitchTo(desk.Id, null, consumed: false);
        }

        if (_pins.IsPinned(id))
            return FocusPinned(desk, id);

        var covered = desk.Covered();
        id = FocusUnderFullscreen(desk, id, out var refit);
        var raised = RaiseMember(desk, id);
        desk.Layout.NoteFocus(id);
        if (desk.Id.IsSpecial)
        {
            var special = OpenSpecial(desk.Id, id);
            return raised is null ? special : raised.Then(special);
        }

        var outcome = SwitchTo(desk.Id, id, consumed: false);
        outcome = outcome.Focus is null ? outcome with { Focus = id } : outcome;
        if (refit is not null && _workspaces.Shown.IsShown(desk.Id))
            outcome = Present(desk, covered, refit).Then(outcome);
        return raised is null ? outcome : raised.Then(outcome);
    }

    private Outcome FloatWindow(string arguments)
    {
        var window = Select(arguments, emptyMeansActive: true);
        if (window is not WindowId id || Find(id) is not Client client || WorkspaceContaining(id) is not LiveWorkspace desk)
            return View([]);

        var covered = desk.Covered();
        var placements = desk.Layout.ToggleFloating(id);
        // Only a floating Client is pinned, including one whose pin a fullscreen mode holds.
        _pins.Forget(id);

        // A window that floats is raised, and so allowed over a fullscreen one.
        if (!client.Floating)
            _cover.SetFlag(id, true);

        return _workspaces.Shown.IsShown(desk.Id)
            ? Present(desk, covered, placements)
            : View([]);
    }

    private Outcome PseudoWindow(string arguments)
    {
        var window = Select(arguments, emptyMeansActive: true);
        if (window is not WindowId id)
            return View([]);

        return ChangePseudo(id);
    }

    private Outcome MoveWindowToWorkspace(string arguments, bool silent)
    {
        var comma = arguments.LastIndexOf(',');
        if (comma < 0)
            return MoveTo(arguments, silent, consumed: false);

        var workspace = arguments[..comma];
        var window = Select(arguments[(comma + 1)..], emptyMeansActive: false);
        return MoveTo(workspace, silent, consumed: false, window, windowChosen: true);
    }

    private Outcome SwapWindow(string arguments)
    {
        var text = arguments.Trim();
        if (ActiveWindow() is not WindowId current || WorkspaceContaining(current) is not LiveWorkspace home)
            return Rejected("error: No window to swap with");

        // Winland's answers, in its order.
        if (Find(current) is { Fullscreen: not FullscreenMode.None })
            return Rejected("error: Can't swap fullscreen window");

        WindowId other;
        if (Sides.TryParse(text, out _))
        {
            if (home.Layout.Neighbor(current, text) is not WindowId neighbor || neighbor == current)
                return Rejected("error: No window to swap with in that direction");

            other = neighbor;
        }
        else if (WindowSelector.Find(text, current, EveryClient()) is not WindowId selected)
        {
            return Rejected("error: No window to swap with");
        }
        else if (selected == current)
        {
            return Rejected("error: Can't swap a window with itself");
        }
        else
        {
            other = selected;
        }

        if (WorkspaceContaining(other) is not LiveWorkspace away)
            return Rejected("error: No window to swap with");
        if (Find(other) is { Fullscreen: not FullscreenMode.None })
            return Rejected("error: Can't swap fullscreen window");
        if (away.Id != home.Id)
            return SwapAcross(current, other, home, away);

        var covered = home.Covered();
        var placements = home.Layout.Swap(current, other);
        if (placements.Count == 0)
            return Rejected("error: No window to swap with");

        return _workspaces.Shown.IsShown(home.Id)
            ? Present(home, covered, placements)
            : View(placements);
    }

    // Two windows on different Workspaces trade places, each taking the other's slot and Workspace, as the pinned
    // release switches their targets. A window in a Group is not swapped across Workspaces.
    private Outcome SwapAcross(WindowId current, WindowId other, LiveWorkspace home, LiveWorkspace away)
    {
        if (home.GroupOf(current) is not null || away.GroupOf(other) is not null)
            return Rejected("error: No window to swap with");

        // The first slot is held by a placeholder while the second changes hands.
        var placeholder = new WindowId(long.MinValue);
        if (!home.Layout.Substitute(current, placeholder, new Taken(Floating: false, Minimized: false, Bounds: default), out var mine))
            return Rejected("error: No window to swap with");

        if (!away.Layout.Substitute(other, current, mine, out var theirs))
        {
            _ = home.Layout.Substitute(placeholder, current, mine, out _);
            return Rejected("error: No window to swap with");
        }

        _ = home.Layout.Substitute(placeholder, other, theirs, out _);
        if (home.Focus == current)
            FocusState.Remember(home, other);
        if (away.Focus == other)
            FocusState.Remember(away, current);

        var homeShown = _workspaces.Shown.IsShown(home.Id);
        var awayShown = _workspaces.Shown.IsShown(away.Id);
        var show = new List<WindowId>();
        var hide = new List<WindowId>();
        if (homeShown != awayShown)
        {
            show.Add(homeShown ? other : current);
            hide.Add(homeShown ? current : other);
        }

        // The focus stays on a shown Workspace: the window that came onto the focused one takes it there.
        WindowId? focus = null;
        if (_focus.Focused == current && !awayShown)
        {
            Give(other, home);
            focus = other;
        }

        var placements = new List<Placement>();
        if (homeShown)
            placements.AddRange(home.Layout.Visible());
        if (awayShown)
            placements.AddRange(away.Layout.Visible());

        return View(placements, show, hide, focus: focus);
    }

    private Outcome ExitDaemon() =>
        View([]) with { Exit = true };

    private WindowId? Select(string arguments, bool emptyMeansActive)
    {
        var text = arguments.Trim();
        if (text.Length == 0)
            return emptyMeansActive ? ActiveWindow() : null;

        return WindowSelector.Find(text, ActiveWindow(), EveryClient());
    }

    // Every Client a window selector can find, including one waiting in a Group.
    private List<Client> EveryClient()
    {
        var clients = new List<Client>();
        foreach (var desk in _workspaces)
        {
            clients.AddRange(desk.Layout.Clients);
            clients.AddRange(desk.WaitingClients);
        }

        return clients;
    }

    private Outcome Rejected(string error) =>
        View([]) with { Error = error };

    // The same request killactive makes, so the application can still ask to save, for every Client.
    // Workspace 1 of the focused Monitor is what shows
    // afterwards, which closes a Special workspace open on that Monitor.
    private Outcome CloseAll()
    {
        var closes = EveryClient().ConvertAll(client => client.Window);

        var monitor = FocusedId();
        if (_workspaces.Shown.SpecialOn(monitor) is null)
        {
            return SwitchTo(WorkspaceId.FromNumber(1, monitor), focus: null, consumed: false)
                with { Closes = closes };
        }

        // Closing it first shows the Monitor's Workspace. Switching with it still open would leave it on top.
        var concealed = CloseSpecial(monitor);
        var shown = SwitchTo(WorkspaceId.FromNumber(1, monitor), focus: null, consumed: false);
        return concealed.Then(shown) with { Closes = closes, Focus = _focus.Focused };
    }
}
