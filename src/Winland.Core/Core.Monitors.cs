namespace Winland;

public sealed partial class Core
{
    private bool _monitorFallback = true;

    private void ReadBinds(ConfigDocument document)
    {
        _monitorFallback = true;
        foreach (var setting in document.Settings)
        {
            if (setting.Keyword == "binds:window_direction_monitor_fallback"
                && ConfigValueSyntax.TryParseBool(setting.Value, out var enabled))
                _monitorFallback = enabled;
        }
    }

    private Outcome MoveFocusDirection(string arguments, bool allowsElevation)
    {
        const string name = "movefocus";
        var desk = ActiveDesk();
        var covered = Covered(desk);
        var active = ActiveWindow();
        if (!MonitorSelector.TryDirection(FirstToken(arguments), out var direction))
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        if (active is WindowId current)
        {
            var focus = desk.Layout.MoveFocus(current, arguments);
            if (focus is WindowId chosen)
            {
                if (HasNoFocus(chosen))
                    return View([], dispatcher: name, allowsElevation: allowsElevation);

                _active = chosen;
                desk.Focus = chosen;
                desk.Layout.NoteFocus(chosen);
                return Present(desk, covered, [], dispatcher: name, focus: chosen, allowsElevation: allowsElevation);
            }
        }

        if (!_monitorFallback || ScreenHolding(desk) is not Screen origin || TargetOf(origin) is not MonitorTarget from)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        if (MonitorSelector.InDirection(Targets(), from, direction) is not MonitorTarget neighbor
            || ScreenOf(neighbor) is not Screen destination)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        WindowId? adjacent = null;
        if (active is WindowId fromWindow && desk.Layout.TryBounds(fromWindow, out var box))
            adjacent = AdjacentOn(destination, box, direction);

        return FocusOnto(destination, name, allowsElevation, adjacent);
    }

    private Outcome MoveWindowDirection(string arguments, bool allowsElevation)
    {
        const string name = "movewindow";
        if (TryMonitorArgument(arguments, out var monitorText, out var silent))
        {
            if (ScreenOf(MonitorSelector.Resolve(monitorText, Targets(), TargetOf(FocusedScreen()))) is not Screen chosen)
                return Rejected(name, "error: monitor not found", allowsElevation);

            return MoveActiveOnto(chosen, silent, name, allowsElevation);
        }

        var desk = ActiveDesk();
        var covered = Covered(desk);
        var active = ActiveWindow();
        if (active is not WindowId window || !MonitorSelector.TryDirection(FirstToken(arguments), out var direction))
        {
            IReadOnlyList<Placement> stayed = active is WindowId id ? desk.Layout.MoveWindow(id, arguments) : [];
            return Present(desk, covered, stayed, dispatcher: name, allowsElevation: allowsElevation);
        }

        if (_monitorFallback
            && ScreenHolding(desk) is Screen origin
            && desk.Layout.TryBounds(window, out var box))
        {
            var hit = MonitorSelector.At(Targets(), MonitorSelector.Focal(box, direction));
            if (hit is MonitorTarget other && other.Id != origin.Id && ScreenOf(other) is Screen across)
                return MoveActiveOnto(across, silent: false, name, allowsElevation);
        }

        var placements = desk.Layout.MoveWindow(window, arguments);
        if (_monitorFallback
            && placements.Count == 0
            && desk.Layout.FollowsMonitorWhenUnmoved
            && ScreenHolding(desk) is Screen home
            && TargetOf(home) is MonitorTarget from
            && MonitorSelector.InDirection(Targets(), from, direction) is MonitorTarget neighbor
            && ScreenOf(neighbor) is Screen next)
            return MoveActiveOnto(next, silent: false, name, allowsElevation);

        return Present(desk, covered, placements, dispatcher: name, allowsElevation: allowsElevation);
    }

    private Outcome FocusMonitorDispatch(string arguments, bool allowsElevation)
    {
        const string name = "focusmonitor";
        if (ScreenOf(MonitorSelector.Resolve(arguments, Targets(), TargetOf(FocusedScreen()))) is not Screen screen)
            return Rejected(name, "error: monitor not found", allowsElevation);

        return FocusOnto(screen, name, allowsElevation, preferred: null);
    }

    private Outcome MoveCurrentWorkspace(string arguments, bool allowsElevation)
    {
        const string name = "movecurrentworkspacetomonitor";
        if (ScreenOf(MonitorSelector.Resolve(arguments, Targets(), TargetOf(FocusedScreen()))) is not Screen destination)
            return Rejected(name, "error: monitor not found", allowsElevation);

        if (FindDesk(ActiveId()) is not Desk desk)
            return Rejected(name, "error: invalid workspace", allowsElevation);

        return MoveDeskTo(desk, destination, name, allowsElevation);
    }

    private Outcome MoveWorkspace(string arguments, bool allowsElevation)
    {
        const string name = "moveworkspacetomonitor";
        if (!SplitPair(arguments, out var workspaceText, out var monitorText))
            return Rejected(name, "error: expected workspace and monitor", allowsElevation);

        if (!WorkspaceAddress.TryResolve(workspaceText, Context(), out var id))
            return Rejected(name, "error: invalid workspace", allowsElevation);

        if (FindDesk(id) is not Desk desk)
            return Rejected(name, "error: workspace not found", allowsElevation);

        if (ScreenOf(MonitorSelector.Resolve(monitorText, Targets(), TargetOf(FocusedScreen()))) is not Screen destination)
            return Rejected(name, "error: monitor not found", allowsElevation);

        return MoveDeskTo(desk, destination, name, allowsElevation);
    }

    private Outcome SwapActiveWorkspaces(string arguments, bool allowsElevation)
    {
        const string name = "swapactiveworkspaces";
        if (!SplitPair(arguments, out var first, out var second))
            return Rejected(name, "error: monitor not found", allowsElevation);

        var targets = Targets();
        var current = TargetOf(FocusedScreen());
        if (ScreenOf(MonitorSelector.Resolve(first, targets, current)) is not Screen left
            || ScreenOf(MonitorSelector.Resolve(second, targets, current)) is not Screen right)
            return Rejected(name, "error: monitor not found", allowsElevation);

        if (left.Id == right.Id)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        var before = ShowingNow();
        var deskLeft = DeskFor(left.Active ?? NextFree(left.Id), left.Id);
        var deskRight = DeskFor(right.Active ?? NextFree(right.Id), right.Id);
        var dx = right.Effective.X - left.Effective.X;
        var dy = right.Effective.Y - left.Effective.Y;
        deskLeft.Layout.NudgeFloating(dx, dy);
        deskRight.Layout.NudgeFloating(-dx, -dy);
        deskLeft.Monitor = right.Id;
        deskRight.Monitor = left.Id;
        left.Active = deskRight.Id;
        right.Active = deskLeft.Id;
        _activeOn[left.Id] = deskRight.Id;
        _activeOn[right.Id] = deskLeft.Id;
        Refit(deskLeft);
        Refit(deskRight);

        WindowId? focus = null;
        var focused = left.Focused ? left : right.Focused ? right : null;
        if (focused is Screen screen && screen.Active is WorkspaceId active && FindDesk(active) is Desk shown)
        {
            var next = Accept(Remembered(shown));
            _active = next;
            shown.Focus = next;
            if (next is WindowId noted)
                shown.Layout.NoteFocus(noted);
            Remember(active, screen.Id);
            focus = next;
        }

        SyncActive();
        var (show, hide) = VisibilityDelta(before);
        return View(ShownPlacements(), show, hide, dispatcher: name, focus: focus, allowsElevation: allowsElevation);
    }

    private Outcome MoveDeskTo(Desk desk, Screen destination, string dispatcher, bool allowsElevation)
    {
        if (desk.Monitor == destination.Id)
            return View([], dispatcher: dispatcher, allowsElevation: allowsElevation);

        var before = ShowingNow();
        var origin = ScreenById(desk.Monitor);
        var wasActive = origin is not null && origin.Active == desk.Id;
        var follow = wasActive && origin is { Focused: true };
        var dx = destination.Effective.X - (origin?.Effective.X ?? destination.Effective.X);
        var dy = destination.Effective.Y - (origin?.Effective.Y ?? destination.Effective.Y);
        if (wasActive && origin is not null)
            Plug(origin, desk);

        desk.Layout.NudgeFloating(dx, dy);
        desk.Monitor = destination.Id;
        Refit(desk);

        WindowId? focus = null;
        if (follow)
        {
            destination.Active = desk.Id;
            _activeOn[destination.Id] = desk.Id;
            var next = Accept(Remembered(desk));
            _active = next;
            desk.Focus = next;
            if (next is WindowId noted)
                desk.Layout.NoteFocus(noted);
            Focus(destination.Id);
            focus = next;
        }

        SyncActive();
        var (show, hide) = VisibilityDelta(before);
        return View(ShownPlacements(), show, hide, dispatcher: dispatcher, focus: focus, allowsElevation: allowsElevation);
    }

    private Outcome MoveActiveOnto(Screen screen, bool silent, string dispatcher, bool allowsElevation)
    {
        if (screen.Active is not WorkspaceId target)
            return View([], dispatcher: dispatcher, allowsElevation: allowsElevation);

        return MoveTo(target.Name, silent, consumed: false, dispatcher, allowsElevation);
    }

    private Outcome FocusOnto(Screen screen, string dispatcher, bool allowsElevation, WindowId? preferred)
    {
        if (screen.Focused && preferred is null)
            return View([], dispatcher: dispatcher, allowsElevation: allowsElevation);

        var desk = screen.Active is WorkspaceId workspace ? FindDesk(workspace) : null;
        var next = preferred;
        if (next is WindowId wanted && (desk is null || ClientOn(desk, wanted) is null || HasNoFocus(wanted)))
            next = null;
        if (next is null && desk is not null)
            next = Accept(Remembered(desk));

        Focus(screen.Id);
        _active = next;
        if (desk is not null)
        {
            desk.Focus = next;
            if (next is WindowId noted)
                desk.Layout.NoteFocus(noted);
        }

        return View([], focus: next, dispatcher: dispatcher, allowsElevation: allowsElevation);
    }

    private void Plug(Screen origin, Desk leaving)
    {
        Desk? replacement = null;
        foreach (var other in _desks)
        {
            if (other.Id == leaving.Id || other.Monitor != origin.Id)
                continue;

            replacement = other;
            break;
        }

        replacement ??= DeskFor(NextFree(origin.Id), origin.Id);
        origin.Active = replacement.Id;
        _activeOn[origin.Id] = replacement.Id;
    }

    private WindowId? AdjacentOn(Screen screen, Rectangle origin, MonitorDirection direction)
    {
        if (screen.Active is not WorkspaceId workspace || FindDesk(workspace) is not Desk desk)
            return null;

        WindowId? best = null;
        var bestOverlap = 1.0;
        foreach (var client in desk.Layout.Clients)
        {
            if (client.Minimized || HasNoFocus(client.Window) || !desk.Layout.TryBounds(client.Window, out var box))
                continue;
            if (!MonitorSelector.Overlaps(origin, box, direction, out var overlap) || overlap <= bestOverlap)
                continue;

            bestOverlap = overlap;
            best = client.Window;
        }

        return best;
    }

    private WindowId? Accept(WindowId? window) =>
        window is WindowId candidate && !HasNoFocus(candidate) ? candidate : null;

    private List<MonitorTarget> Targets()
    {
        var targets = new List<MonitorTarget>();
        foreach (var screen in _screens)
        {
            if (screen.Effective.Enabled)
                targets.Add(Target(screen));
        }

        return targets;
    }

    private static MonitorTarget Target(Screen screen)
    {
        var monitor = screen.Effective;
        return new MonitorTarget(screen.Id, monitor.Name, monitor.Description, monitor.X, monitor.Y, monitor.Width, monitor.Height);
    }

    private MonitorTarget? TargetOf(Screen? screen) =>
        screen is { Effective.Enabled: true } enabled ? Target(enabled) : null;

    private Screen? ScreenOf(MonitorTarget? target)
    {
        if (target is not MonitorTarget found)
            return null;

        var screen = ScreenById(found.Id);
        return screen is { Effective.Enabled: true } ? screen : null;
    }

    private Screen? ScreenHolding(Desk desk)
    {
        var screen = ScreenById(desk.Monitor);
        return screen is { Effective.Enabled: true } ? screen : null;
    }

    private Screen? FocusedScreen()
    {
        foreach (var screen in _screens)
        {
            if (screen.Focused && screen.Effective.Enabled)
                return screen;
        }

        foreach (var screen in _screens)
        {
            if (screen.Effective.Enabled)
                return screen;
        }

        return null;
    }

    private HashSet<WindowId> ShowingNow()
    {
        var shown = new HashSet<WindowId>();
        foreach (var window in ShownWindows())
            shown.Add(window);

        return shown;
    }

    private (WindowId[] Show, WindowId[] Hide) VisibilityDelta(HashSet<WindowId> before)
    {
        var after = ShowingNow();
        var show = new List<WindowId>();
        var hide = new List<WindowId>();
        foreach (var window in after)
        {
            if (!before.Contains(window))
                show.Add(window);
        }

        foreach (var window in before)
        {
            if (!after.Contains(window))
                hide.Add(window);
        }

        return (show.ToArray(), hide.ToArray());
    }



    private static bool TryMonitorArgument(string arguments, out string monitor, out bool silent)
    {
        silent = false;
        monitor = "";
        var text = arguments.Trim();
        if (text.EndsWith(" silent", StringComparison.OrdinalIgnoreCase))
        {
            silent = true;
            text = text[..^7].TrimEnd();
        }

        if (!text.StartsWith("mon:", StringComparison.OrdinalIgnoreCase))
            return false;

        monitor = text[4..].Trim();
        return true;
    }

    private static bool SplitPair(string arguments, out string first, out string second)
    {
        var text = arguments.Trim();
        var space = text.IndexOf(' ');
        if (space < 0)
        {
            first = text;
            second = "";
            return false;
        }

        first = text[..space].Trim();
        second = text[(space + 1)..].Trim();
        return first.Length > 0 && second.Length > 0;
    }

    private static string FirstToken(string arguments)
    {
        var text = arguments.Trim();
        var space = text.IndexOf(' ');
        return space < 0 ? text : text[..space];
    }
}
