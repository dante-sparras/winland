namespace Winland;

public sealed partial class Core
{
    private readonly HashSet<WindowId> _pinned = [];

    private enum PinAction
    {
        Toggle,
        Enable,
        Disable,
    }

    private Outcome PinWindow(string arguments, bool allowsElevation)
    {
        const string name = "pin";
        var (action, selector) = PinRequest(arguments);
        var window = Select(selector, emptyMeansActive: true);
        if (window is not WindowId id || Find(id) is not Client client || WorkspaceContaining(id) is not LiveWorkspace desk)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        if (!client.Floating)
            return Rejected(name, "error: window does not qualify to be pinned", allowsElevation);

        var pinned = _pinned.Contains(id);
        var want = action switch
        {
            PinAction.Enable => true,
            PinAction.Disable => false,
            _ => !pinned,
        };
        if (want == pinned)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        if (want)
            _pinned.Add(id);
        else
            _pinned.Remove(id);

        (_extraEvents ??= []).Add(new ClientPinned(id, want));
        return Settle(desk, id, name, allowsElevation);
    }

    private Outcome FocusPinned(LiveWorkspace desk, WindowId id, string name, bool allowsElevation)
    {
        var fromShown = IsShown(desk.Id);
        var monitor = FocusedId();
        var active = ActiveOn(monitor);
        if (desk.Id != active)
        {
            var destination = WorkspaceFor(active, monitor);
            if (!Transfer(desk, destination, id))
                return View([], dispatcher: name, allowsElevation: allowsElevation);

            DropIfEmpty(desk.Id);
            desk = destination;
        }

        _active = id;
        desk.Focus = id;
        desk.Layout.NoteFocus(id);
        var placements = IsShown(desk.Id) ? desk.Layout.Visible() : [];
        var show = !fromShown && IsShown(desk.Id) && desk.ClientOf(id) is { Minimized: false }
            ? new[] { id }
            : null;
        return View(placements, show, dispatcher: name, focus: id, allowsElevation: allowsElevation);
    }

    private Outcome Settle(LiveWorkspace desk, WindowId id, string name, bool allowsElevation)
    {
        var active = ActiveOn(desk.Monitor);
        if (desk.Id == active)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        var destination = WorkspaceFor(active, desk.Monitor);
        var wasShown = IsShown(desk.Id);
        HashSet<WindowId> covered = wasShown ? desk.Covered() : [];
        if (!Transfer(desk, destination, id))
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        var show = new List<WindowId>();
        var hide = new List<WindowId>();
        if (wasShown && FindWorkspace(desk.Id) is { } source)
            source.Collect(covered, show, hide);

        if (IsShown(destination.Id))
        {
            if (destination.ClientOf(id) is { Minimized: false })
                show.Add(id);
        }
        else
        {
            hide.Add(id);
        }

        DropIfEmpty(desk.Id);
        var placements = new List<Placement>();
        if (FindWorkspace(desk.Id) is { } left && IsShown(left.Id))
            placements.AddRange(left.Layout.Placed());
        if (IsShown(destination.Id))
            placements.AddRange(destination.Layout.Visible());

        return View(placements, show, hide, dispatcher: name, allowsElevation: allowsElevation);
    }

    private void CarryPinned(WorkspaceId sourceId, LiveWorkspace destination)
    {
        var source = FindWorkspace(sourceId);
        if (source is null || source.Id == destination.Id)
            return;

        foreach (var window in PinnedFloating(source))
            Transfer(source, destination, window);
    }

    private List<WindowId> PinnedFloating(LiveWorkspace desk)
    {
        var windows = new List<WindowId>();
        foreach (var client in desk.Layout.Clients)
        {
            if (client.Floating && _pinned.Contains(client.Window))
                windows.Add(client.Window);
        }

        return windows;
    }

    private bool Transfer(LiveWorkspace source, LiveWorkspace destination, WindowId id)
    {
        if (source.Id == destination.Id || !source.Take(id, out var taken))
            return source.Id == destination.Id;

        var area = WorkArea(destination, _area);
        destination.Receive(id, taken, area);
        CarryGroup(source, destination, id);

        return true;
    }

    private bool StaysVisible(Client client)
    {
        if (!client.Pinned || !client.Floating || client.Minimized)
            return false;

        var desk = FindWorkspace(client.Workspace);
        return desk is not null && MonitorShowing(desk.Monitor);
    }

    private bool MonitorShowing(MonitorId monitor)
    {
        if (_monitors.Count == 0)
            return true;

        var screen = MonitorById(monitor);
        return screen is { Effective.Enabled: true, Active: not null };
    }

    private static (PinAction Action, string Selector) PinRequest(string arguments)
    {
        var text = arguments.Trim();
        if (text.Length == 0)
            return (PinAction.Toggle, "");

        var space = text.IndexOfAny([' ', '\t']);
        var head = space < 0 ? text : text[..space];
        var rest = space < 0 ? "" : text[(space + 1)..].Trim();
        var action = head switch
        {
            "toggle" => PinAction.Toggle,
            "enable" or "on" => PinAction.Enable,
            "disable" or "off" => PinAction.Disable,
            _ => (PinAction?)null,
        };
        return action is PinAction chosen ? (chosen, rest) : (PinAction.Toggle, text);
    }
}
