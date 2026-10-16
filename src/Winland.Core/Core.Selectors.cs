namespace Winland;

public sealed partial class Core
{
    private readonly Dictionary<string, bool> _powered = new(StringComparer.OrdinalIgnoreCase);

    private Outcome FocusWindow(string arguments, bool allowsElevation)
    {
        const string name = "focuswindow";
        if (Select(arguments, emptyMeansActive: false) is not WindowId id)
            return Rejected(name, "error: no such window", allowsElevation);

        return FocusClient(id, name, allowsElevation);
    }

    private Outcome FocusClient(WindowId id, string name, bool allowsElevation)
    {
        if (WorkspaceContaining(id) is not LiveWorkspace desk)
            return Rejected(name, "error: no such window", allowsElevation);

        if (_pinned.Contains(id) && Find(id) is { Floating: true })
            return FocusPinned(desk, id, name, allowsElevation);

        var raised = RaiseMember(desk, id, name, allowsElevation);
        desk.Layout.NoteFocus(id);
        if (desk.Id.IsSpecial)
        {
            var special = OpenSpecial(desk.Id, id, name, allowsElevation);
            return raised is null ? special : Merge(raised, special);
        }

        var outcome = SwitchTo(desk.Id, id, consumed: false, dispatcher: name, allowsElevation: allowsElevation);
        outcome = outcome.Focus is null ? outcome with { Focus = id } : outcome;
        return raised is null ? outcome : Merge(raised, outcome);
    }

    private Outcome CloseWindow(string arguments, bool allowsElevation)
    {
        const string name = "closewindow";
        var window = Select(arguments, emptyMeansActive: false);
        if (window is not WindowId id)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        if (!Closeable(id))
            return Rejected(name, NotCloseable, allowsElevation);

        return View([], dispatcher: name, close: id, allowsElevation: allowsElevation);
    }

    private Outcome KillWindow(string arguments, bool allowsElevation)
    {
        const string name = "killwindow";
        var window = Select(arguments, emptyMeansActive: false);
        if (window is not WindowId id || Find(id) is not Client client || client.ProcessId <= 0)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        return View([], dispatcher: name, allowsElevation: allowsElevation) with
        {
            Kill = new KillRequest(client.ProcessId) { Window = id },
        };
    }

    private Outcome FloatWindow(string arguments, bool? floating, bool allowsElevation)
    {
        var name = floating switch
        {
            true => "setfloating",
            false => "settiled",
            _ => "togglefloating",
        };
        var window = Select(arguments, emptyMeansActive: true);
        if (window is not WindowId id || Find(id) is not Client client || WorkspaceContaining(id) is not LiveWorkspace desk)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        var want = floating ?? !client.Floating;
        if (want == client.Floating)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        var covered = desk.Covered();
        var placements = desk.Layout.ToggleFloating(id);
        _pinned.Remove(id);
        return IsShown(desk.Id)
            ? Present(desk, covered, placements, dispatcher: name, allowsElevation: allowsElevation)
            : View([], dispatcher: name, allowsElevation: allowsElevation);
    }

    private Outcome PseudoWindow(string arguments, bool allowsElevation)
    {
        const string name = "pseudo";
        var window = Select(arguments, emptyMeansActive: true);
        if (window is not WindowId id)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        return ChangePseudo(id) with { Dispatcher = name, AllowsElevation = allowsElevation };
    }

    private Outcome MoveWindowToWorkspace(string arguments, bool silent, bool allowsElevation)
    {
        var name = silent ? "movetoworkspacesilent" : "movetoworkspace";
        var comma = arguments.LastIndexOf(',');
        if (comma < 0)
            return MoveTo(arguments, silent, consumed: false, name, allowsElevation);

        var workspace = arguments[..comma];
        var window = Select(arguments[(comma + 1)..], emptyMeansActive: false);
        return MoveTo(workspace, silent, consumed: false, name, allowsElevation, window, windowChosen: true);
    }

    private Outcome MoveRoot(string arguments, bool allowsElevation)
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
            return View(placements, dispatcher: name, allowsElevation: allowsElevation) with { Error = error };

        return IsShown(desk.Id)
            ? Present(desk, covered, placements, dispatcher: name, allowsElevation: allowsElevation)
            : View([], dispatcher: name, allowsElevation: allowsElevation);
    }

    private Outcome SwapWindow(string arguments, bool allowsElevation)
    {
        const string name = "swapwindow";
        var text = arguments.Trim();
        if (ActiveWindow() is not WindowId current || WorkspaceContaining(current) is not LiveWorkspace home)
            return Rejected(name, "error: no window to swap with", allowsElevation);

        if (Find(current) is { Fullscreen: not FullscreenMode.None })
            return Rejected(name, "error: can't swap fullscreen window", allowsElevation);

        WindowId other;
        if (IsDirection(text))
        {
            if (home.Layout.MoveFocus(current, text) is not WindowId neighbor || neighbor == current)
                return Rejected(name, "error: no window to swap with", allowsElevation);

            other = neighbor;
        }
        else if (WindowSelector.Find(text, current, EveryClient()) is WindowId selected && selected != current)
        {
            other = selected;
        }
        else
        {
            return Rejected(name, "error: no window to swap with", allowsElevation);
        }

        if (WorkspaceContaining(other) is not LiveWorkspace away || away.Id != home.Id)
            return Rejected(name, "error: no window to swap with", allowsElevation);
        if (Find(other) is { Fullscreen: not FullscreenMode.None })
            return Rejected(name, "error: can't swap fullscreen window", allowsElevation);

        var covered = home.Covered();
        var placements = home.Layout.Swap(current, other);
        if (placements.Count == 0)
            return Rejected(name, "error: no window to swap with", allowsElevation);

        return IsShown(home.Id)
            ? Present(home, covered, placements, dispatcher: name, allowsElevation: allowsElevation)
            : View(placements, dispatcher: name, allowsElevation: allowsElevation);
    }

    private Outcome ExitDaemon(bool allowsElevation) =>
        View([], dispatcher: "exit", allowsElevation: allowsElevation) with { Exit = true };

    private Outcome EmitCustom(string arguments, bool allowsElevation)
    {
        (_extraEvents ??= []).Add(new CustomEvent(arguments));
        return View([], dispatcher: "event", allowsElevation: allowsElevation);
    }

    private Outcome Dpms(string arguments, bool allowsElevation)
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
            return View([], dispatcher: "dpms", allowsElevation: allowsElevation) with
            {
                DisplayPower = [new DisplayPowerRequest("", PowerAfter("", turnOn, toggle))],
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

        return View([], dispatcher: "dpms", allowsElevation: allowsElevation) with { DisplayPower = requests };
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

        return WindowSelector.Find(text, ActiveWindow(), EveryClient());
    }

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

    private Outcome Rejected(string name, string error, bool allowsElevation) =>
        View([], dispatcher: name, allowsElevation: allowsElevation) with { Error = error };

    private static bool IsDirection(string text) =>
        text is "l" or "r" or "u" or "d" or "t" or "b"
            or "left" or "right" or "up" or "down" or "top" or "bottom";

    private static bool MonitorMatches(LiveMonitor screen, string token) =>
        screen.Effective.Name.Equals(token, StringComparison.OrdinalIgnoreCase)
        || screen.Reported.Name.Equals(token, StringComparison.OrdinalIgnoreCase)
        || screen.Effective.Description.Equals(token, StringComparison.OrdinalIgnoreCase)
        || screen.Reported.Description.Equals(token, StringComparison.OrdinalIgnoreCase);
}
