namespace Winland.Application;

/// <summary>Dispatchers that act on one window and the daemon: focus, close, kill, float, swap, move to a Workspace, <c>exit</c>, <c>event</c> and <c>dpms</c>.</summary>
public sealed partial class WindowManager
{
    private readonly Dictionary<string, bool> _powered = new(StringComparer.OrdinalIgnoreCase);

    private Outcome FocusWindow(string arguments)
    {
        const string name = "focuswindow";
        if (Select(arguments, emptyMeansActive: false) is not WindowId id)
            return Rejected(name, "error: no such window");

        return FocusClient(id, name);
    }

    private Outcome FocusClient(WindowId id, string name)
    {
        if (WorkspaceContaining(id) is not LiveWorkspace desk)
            return Rejected(name, "error: no such window");

        // A window shadowed by a modal refuses the focus, though its Workspace is still shown.
        if (BlockedByModal(id))
        {
            return _workspaces.Shown.IsShown(desk.Id)
                ? View([], dispatcher: name)
                : SwitchTo(desk.Id, null, consumed: false, dispatcher: name);
        }

        if (_pins.IsPinned(id))
            return FocusPinned(desk, id, name);

        var covered = desk.Covered();
        id = FocusUnderFullscreen(desk, id, out var refit);
        var raised = RaiseMember(desk, id, name);
        desk.Layout.NoteFocus(id);
        if (desk.Id.IsSpecial)
        {
            var special = OpenSpecial(desk.Id, id, name);
            return raised is null ? special : raised.Then(special);
        }

        var outcome = SwitchTo(desk.Id, id, consumed: false, dispatcher: name);
        outcome = outcome.Focus is null ? outcome with { Focus = id } : outcome;
        if (refit is not null && _workspaces.Shown.IsShown(desk.Id))
            outcome = Present(desk, covered, refit).Then(outcome);
        return raised is null ? outcome : raised.Then(outcome);
    }

    private Outcome CloseWindow(string arguments)
    {
        const string name = "closewindow";
        var window = Select(arguments, emptyMeansActive: false);
        if (window is not WindowId id)
            return View([], dispatcher: name);

        if (!Closeable(id))
            return Rejected(name, NotCloseable);

        return View([], dispatcher: name, close: id);
    }

    private Outcome KillWindow(string arguments)
    {
        const string name = "killwindow";
        var window = Select(arguments, emptyMeansActive: false);
        if (window is not WindowId id || (Find(id) ?? EveryClient().Find(client => client.Window == id)) is not Client client || client.ProcessId <= 0)
            return View([], dispatcher: name);

        return View([], dispatcher: name) with
        {
            Kill = new KillRequest(client.ProcessId) { Window = id },
        };
    }

    private Outcome FloatWindow(string arguments, bool? floating)
    {
        var name = floating switch
        {
            true => "setfloating",
            false => "settiled",
            _ => "togglefloating",
        };
        var window = Select(arguments, emptyMeansActive: true);
        if (window is not WindowId id || Find(id) is not Client client || WorkspaceContaining(id) is not LiveWorkspace desk)
            return View([], dispatcher: name);

        var want = floating ?? !client.Floating;
        if (want == client.Floating)
            return View([], dispatcher: name);

        var covered = desk.Covered();
        var placements = desk.Layout.ToggleFloating(id);
        // Only a floating Client is pinned, including one whose pin a fullscreen mode holds.
        _pins.Forget(id);

        // A window that floats is raised, and so allowed over a fullscreen one.
        if (want)
            _cover.SetFlag(id, true);

        return _workspaces.Shown.IsShown(desk.Id)
            ? Present(desk, covered, placements, dispatcher: name)
            : View([], dispatcher: name);
    }

    private Outcome PseudoWindow(string arguments)
    {
        const string name = "pseudo";
        var window = Select(arguments, emptyMeansActive: true);
        if (window is not WindowId id)
            return View([], dispatcher: name);

        return ChangePseudo(id) with { Dispatcher = name };
    }

    private Outcome MoveWindowToWorkspace(string arguments, bool silent)
    {
        var name = silent ? "movetoworkspacesilent" : "movetoworkspace";
        var comma = arguments.LastIndexOf(',');
        if (comma < 0)
            return MoveTo(arguments, silent, consumed: false, name);

        var workspace = arguments[..comma];
        var window = Select(arguments[(comma + 1)..], emptyMeansActive: false);
        return MoveTo(workspace, silent, consumed: false, name, window, windowChosen: true);
    }

    private Outcome MoveRoot(string arguments)
    {
        const string name = "movetoroot";
        var tokens = arguments.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var window = ActiveWindow();
        if (tokens.Length > 0 && WindowSelector.Find(tokens[0], window, EveryClient()) is WindowId found)
            window = found;

        var desk = window is WindowId id ? WorkspaceContaining(id) ?? FocusedWorkspace() : FocusedWorkspace();
        var covered = desk.Covered();
        var message = arguments.Length == 0 ? name : string.Concat(name, " ", arguments);
        var placements = desk.Layout.LayoutMessage(window, message);
        if (desk.Layout.LayoutMessageError is string error)
            return View(placements, dispatcher: name) with { Error = error };

        return _workspaces.Shown.IsShown(desk.Id)
            ? Present(desk, covered, placements, dispatcher: name)
            : View([], dispatcher: name);
    }

    private Outcome SwapWindow(string arguments)
    {
        const string name = "swapwindow";
        var text = arguments.Trim();
        if (ActiveWindow() is not WindowId current || WorkspaceContaining(current) is not LiveWorkspace home)
            return Rejected(name, "error: No window to swap with");

        // Winland's answers, in its order.
        if (Find(current) is { Fullscreen: not FullscreenMode.None })
            return Rejected(name, "error: Can't swap fullscreen window");

        WindowId other;
        if (Sides.TryParse(text, out _))
        {
            if (home.Layout.Neighbor(current, text) is not WindowId neighbor || neighbor == current)
                return Rejected(name, "error: No window to swap with in that direction");

            other = neighbor;
        }
        else if (WindowSelector.Find(text, current, EveryClient()) is not WindowId selected)
        {
            return Rejected(name, "error: No window to swap with");
        }
        else if (selected == current)
        {
            return Rejected(name, "error: Can't swap a window with itself");
        }
        else
        {
            other = selected;
        }

        if (WorkspaceContaining(other) is not LiveWorkspace away)
            return Rejected(name, "error: No window to swap with");
        if (Find(other) is { Fullscreen: not FullscreenMode.None })
            return Rejected(name, "error: Can't swap fullscreen window");
        if (away.Id != home.Id)
            return SwapAcross(current, other, home, away, name);

        var covered = home.Covered();
        var placements = home.Layout.Swap(current, other);
        if (placements.Count == 0)
            return Rejected(name, "error: No window to swap with");

        return _workspaces.Shown.IsShown(home.Id)
            ? Present(home, covered, placements, dispatcher: name)
            : View(placements, dispatcher: name);
    }

    // Two windows on different Workspaces trade places, each taking the other's slot and Workspace, as the pinned
    // release switches their targets. A window in a Group is not swapped across Workspaces.
    private Outcome SwapAcross(WindowId current, WindowId other, LiveWorkspace home, LiveWorkspace away, string name)
    {
        if (home.GroupOf(current) is not null || away.GroupOf(other) is not null)
            return Rejected(name, "error: No window to swap with");

        // The first slot is held by a placeholder while the second changes hands.
        var placeholder = new WindowId(long.MinValue);
        if (!home.Layout.Substitute(current, placeholder, new Taken(Floating: false, Minimized: false, Bounds: default), out var mine))
            return Rejected(name, "error: No window to swap with");

        if (!away.Layout.Substitute(other, current, mine, out var theirs))
        {
            _ = home.Layout.Substitute(placeholder, current, mine, out _);
            return Rejected(name, "error: No window to swap with");
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
            _focus.Take(other);
            home.Layout.NoteFocus(other);
            focus = other;
        }

        var placements = new List<Placement>();
        if (homeShown)
            placements.AddRange(home.Layout.Visible());
        if (awayShown)
            placements.AddRange(away.Layout.Visible());

        return View(placements, show, hide, dispatcher: name, focus: focus);
    }

    private Outcome ExitDaemon() =>
        View([], dispatcher: "exit") with { Exit = true };

    private Outcome EmitCustom(string arguments)
    {
        (_extraEvents ??= []).Add(new CustomEvent(arguments));
        return View([], dispatcher: "event");
    }

    private Outcome Dpms(string arguments)
    {
        var text = arguments.Trim();
        var toggle = text.StartsWith("toggle", StringComparison.Ordinal);
        var turnOn = text.StartsWith("on", StringComparison.Ordinal);
        string? token = null;
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        if (space >= 0)
            token = text[(space + 1)..].Trim();

        var chosen = new List<LiveMonitor>();
        if (_monitors.Count == 0)
        {
            var on = PowerAfter("", turnOn, toggle);
            Volatile.Write(ref _displaysOn, on);
            return View([], dispatcher: "dpms") with
            {
                DisplayPower = [new DisplayPowerRequest("", on)],
            };
        }

        foreach (var screen in _monitors)
        {
            if (!screen.Effective.Enabled)
                continue;

            if (token is not { Length: > 0 } || MonitorMatches(screen, token))
                chosen.Add(screen);
        }

        if (token is { Length: > 0 } && chosen.Count == 0)
        {
            foreach (var screen in _monitors)
            {
                if (screen.Effective.Enabled)
                    chosen.Add(screen);
            }
        }

        var requests = new DisplayPowerRequest[chosen.Count];
        for (var i = 0; i < chosen.Count; i++)
        {
            var device = chosen[i].Effective.Name;
            requests[i] = new DisplayPowerRequest(device, PowerAfter(device, turnOn, toggle));
        }

        if (requests.Length > 0)
            Volatile.Write(ref _displaysOn, requests[^1].On);

        return View([], dispatcher: "dpms") with { DisplayPower = requests };
    }

    private bool PowerAfter(string device, bool turnOn, bool toggle)
    {
        var current = _powered.TryGetValue(device, out var stored) ? stored : true;
        var next = toggle ? !current : turnOn;
        _powered[device] = next;
        return next;
    }

    private WindowId? Select(string arguments, bool emptyMeansActive)
    {
        var text = arguments.Trim();
        if (text.Length == 0)
            return emptyMeansActive ? ActiveWindow() : null;

        var clients = EveryClient();
        if (text.StartsWith("tag:", StringComparison.Ordinal))
        {
            for (var i = 0; i < clients.Count; i++)
                clients[i] = clients[i] with { Tags = _props.Tags(clients[i].Window) };
        }

        return WindowSelector.Find(text, ActiveWindow(), clients);
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

    private Outcome Rejected(string name, string error) =>
        View([], dispatcher: name) with { Error = error };

    private static bool MonitorMatches(LiveMonitor screen, string token) =>
        screen.Effective.Name.Equals(token, StringComparison.OrdinalIgnoreCase)
        || screen.Reported.Name.Equals(token, StringComparison.OrdinalIgnoreCase)
        || screen.Effective.Description.Equals(token, StringComparison.OrdinalIgnoreCase)
        || screen.Reported.Description.Equals(token, StringComparison.OrdinalIgnoreCase);

    // The same request closewindow makes, so the application can still ask to save, for every Client.
    // A Client no_close_for still protects is left open. Workspace 1 of the focused Monitor is what shows
    // afterwards, which closes a Special workspace open on that Monitor.
    private Outcome CloseAll()
    {
        const string name = "closeall";
        var closes = new List<WindowId>();
        foreach (var client in EveryClient())
        {
            if (Closeable(client.Window))
                closes.Add(client.Window);
        }

        var monitor = FocusedId();
        if (_workspaces.Shown.SpecialOn(monitor) is null)
        {
            return SwitchTo(WorkspaceId.FromNumber(1, monitor), focus: null, consumed: false, dispatcher: name)
                with { Closes = closes };
        }

        // Closing it first shows the Monitor's Workspace. Switching with it still open would leave it on top.
        var concealed = CloseSpecial(monitor, name);
        var shown = SwitchTo(WorkspaceId.FromNumber(1, monitor), focus: null, consumed: false, dispatcher: name);
        return concealed.Then(shown) with { Closes = closes, Dispatcher = name, Focus = _focus.Focused };
    }

    // Whether the displays are on, as the last dpms left them, which Winland keeps for every display at once.
    private bool _displaysOn = true;

    /// <summary>
    /// Whether a key press or release should turn the displays back on: <c>misc:key_press_enables_dpms</c> is set and
    /// dpms turned them off. The keyboard hook reads it with no lock and sends <c>dpms on</c>.
    /// </summary>
    public bool KeyWakesDisplays => _config.Session.KeyPressEnablesDpms && !Volatile.Read(ref _displaysOn);

    // misc:mouse_move_enables_dpms: a pointer move, or the start of a swipe, after dpms off turns every display on, as
    // in Winland.
    private Outcome WakeDisplays(Input input, Outcome outcome)
    {
        if (input is not (PointerMoved or SwipeBegan) || !_config.Session.MouseMoveEnablesDpms || Volatile.Read(ref _displaysOn))
            return outcome;

        var woken = Dpms("on");
        return outcome with { DisplayPower = [.. outcome.DisplayPower, .. woken.DisplayPower] };
    }
}
