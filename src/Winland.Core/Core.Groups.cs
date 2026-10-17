using static Winland.OrdinalText;

namespace Winland;

public sealed partial class Core
{
    private readonly Dictionary<WindowId, GroupRuleFlags> _groupRules = [];
    private GroupOptions _groupOptions = GroupOptions.Documented;
    private bool _groupsLocked;

    private Outcome? GroupDispatch(string name, string arguments, bool allowsElevation)
    {
        var text = arguments.Trim();
        if (Is(name, "togglegroup"))
            return ToggleGroup(name, allowsElevation);
        if (Is(name, "changegroupactive"))
            return ChangeGroupActive(text, name, allowsElevation);
        if (Is(name, "movegroupwindow"))
            return MoveGroupWindow(text, name, allowsElevation);
        if (Is(name, "lockgroups"))
            return LockGroups(text, name, allowsElevation);
        if (Is(name, "lockactivegroup"))
            return LockActiveGroup(text, name, allowsElevation);
        if (Is(name, "denywindowfromgroup"))
            return DenyWindowFromGroup(text, name, allowsElevation);
        if (Is(name, "moveintogroup"))
            return MoveIntoGroup(text, create: false, name, allowsElevation);
        if (Is(name, "moveintoorcreategroup"))
            return MoveIntoGroup(text, create: true, name, allowsElevation);
        if (Is(name, "moveoutofgroup"))
            return MoveOutOfGroup(text, name, allowsElevation);
        if (Is(name, "movewindoworgroup"))
            return MoveWindowOrGroup(text, name, allowsElevation);
        if (Is(name, "setignoregrouplock"))
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        return null;
    }

    private Outcome ToggleGroup(string name, bool allowsElevation)
    {
        if (ActiveWindow() is not WindowId id || WorkspaceContaining(id) is not LiveWorkspace desk)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        var covered = desk.Covered();
        if (Find(id) is { Fullscreen: not FullscreenMode.None })
            _ = Restamp(desk, EndFullscreen(desk, id));

        if (desk.GroupOf(id) is not WindowGroup group)
        {
            StartGroup(desk, id);
            return Present(desk, covered, desk.Layout.Visible(), dispatcher: name, allowsElevation: allowsElevation);
        }

        var head = group.Members[0];
        var focus = desk.Focus;
        var area = WorkArea(desk, _area);
        foreach (var (window, taken) in desk.EndGroup(group))
            desk.Receive(window, taken, area);

        desk.Focus = focus;
        if (focus is WindowId kept)
            desk.Layout.NoteFocus(kept);

        (_extraEvents ??= []).Add(new GroupToggled(head, false));
        return Present(desk, covered, desk.Layout.Visible(), dispatcher: name, allowsElevation: allowsElevation);
    }

    private WindowGroup StartGroup(LiveWorkspace desk, WindowId window)
    {
        var group = desk.StartGroup(window);
        var rules = RulesOf(window);
        group.Locked = rules.HasFlag(GroupRuleFlags.Lock);
        group.Denied = rules.HasFlag(GroupRuleFlags.Deny);
        (_extraEvents ??= []).Add(new GroupToggled(window, true));
        return group;
    }

    private Outcome ChangeGroupActive(string text, string name, bool allowsElevation)
    {
        if (ActiveWindow() is not WindowId id || WorkspaceContaining(id) is not LiveWorkspace desk)
            return Rejected(name, "error: no window found", allowsElevation);

        if (desk.GroupOf(id) is not WindowGroup group)
            return Rejected(name, "error: window is not in a group", allowsElevation);

        if (group.Count == 1)
            return Rejected(name, "error: only one window in group", allowsElevation);

        int index;
        if (int.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var number))
            index = number <= 0 ? group.Count - 1 : Math.Min(number - 1, group.Count - 1);
        else
            index = text is "b" or "prev" ? (group.Current + group.Count - 1) % group.Count : (group.Current + 1) % group.Count;

        return ShowMember(desk, group, index, name, allowsElevation);
    }

    private Outcome ShowMember(LiveWorkspace desk, WindowGroup group, int index, string name, bool allowsElevation)
    {
        var covered = desk.Covered();
        var shown = group.Shown;
        var hadFocus = _active == shown;
        if (!desk.Show(group, index))
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        WindowId? focus = null;
        if (hadFocus)
        {
            focus = group.Shown;
            TakeGroupFocus(desk, group.Shown);
        }

        return IsShown(desk.Id)
            ? Present(desk, covered, desk.Layout.Visible(), dispatcher: name, focus: focus, allowsElevation: allowsElevation)
            : View([], dispatcher: name, allowsElevation: allowsElevation);
    }

    private void TakeGroupFocus(LiveWorkspace desk, WindowId window)
    {
        _active = window;
        desk.Focus = window;
        desk.Layout.NoteFocus(window);
        TouchHistory(window);
    }

    private Outcome MoveGroupWindow(string text, string name, bool allowsElevation)
    {
        if (ActiveWindow() is not WindowId id || WorkspaceContaining(id) is not LiveWorkspace desk)
            return Rejected(name, "error: no window found", allowsElevation);

        if (desk.GroupOf(id) is not WindowGroup group)
            return Rejected(name, "error: window not in a group", allowsElevation);

        var other = text is "b" or "prev"
            ? (group.Current == 0 ? group.Count - 1 : group.Current - 1)
            : (group.Current + 1 >= group.Count ? 0 : group.Current + 1);
        (group.Members[group.Current], group.Members[other]) = (group.Members[other], group.Members[group.Current]);
        group.Current = other;
        return View([], dispatcher: name, allowsElevation: allowsElevation);
    }

    private Outcome LockGroups(string text, string name, bool allowsElevation)
    {
        _groupsLocked = text switch
        {
            "toggle" => !_groupsLocked,
            "lock" or "" or "lockgroups" => true,
            _ => false,
        };
        (_extraEvents ??= []).Add(new GroupsLocked(_groupsLocked));
        return View([], dispatcher: name, allowsElevation: allowsElevation);
    }

    private Outcome LockActiveGroup(string text, string name, bool allowsElevation)
    {
        if (ActiveWindow() is not WindowId id || WorkspaceContaining(id) is not LiveWorkspace desk)
            return Rejected(name, "error: no window found", allowsElevation);

        if (desk.GroupOf(id) is not WindowGroup group)
            return Rejected(name, "error: window not in a group", allowsElevation);

        group.Locked = text switch
        {
            "toggle" => !group.Locked,
            "lock" => true,
            _ => false,
        };
        return View([], dispatcher: name, allowsElevation: allowsElevation);
    }

    private Outcome DenyWindowFromGroup(string text, string name, bool allowsElevation)
    {
        if (ActiveWindow() is WindowId id && WorkspaceContaining(id)?.GroupOf(id) is WindowGroup group)
        {
            group.Denied = text switch
            {
                "toggle" => !group.Denied,
                "on" => true,
                _ => false,
            };
        }

        return View([], dispatcher: name, allowsElevation: allowsElevation);
    }

    private Outcome MoveIntoGroup(string text, bool create, string name, bool allowsElevation)
    {
        if (!TryGroupDirection(text, out var direction))
            return Rejected(name, "error: unsupported direction: " + (text.Length == 0 ? "" : text[..1]), allowsElevation);

        if (!_groupOptions.IgnoreGroupLock && _groupsLocked)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        if (ActiveWindow() is not WindowId id || WorkspaceContaining(id) is not LiveWorkspace desk
            || desk.Layout.Neighbor(id, direction) is not WindowId neighbor || neighbor == id)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        var covered = desk.Covered();
        var target = desk.GroupOf(neighbor);
        if (target is null)
        {
            if (!create || Find(neighbor) is { Fullscreen: not FullscreenMode.None })
                return View([], dispatcher: name, allowsElevation: allowsElevation);

            target = StartGroup(desk, neighbor);
        }

        if (!_groupOptions.IgnoreGroupLock && (target.Locked || desk.GroupOf(id)?.Locked == true))
            return Present(desk, covered, desk.Layout.Visible(), dispatcher: name, allowsElevation: allowsElevation);

        return IntoGroup(desk, covered, id, target, name, allowsElevation);
    }

    private Outcome IntoGroup(LiveWorkspace desk, HashSet<WindowId> covered, WindowId window, WindowGroup target, string name, bool allowsElevation)
    {
        if (target.Denied || target.Contains(window))
            return Present(desk, covered, desk.Layout.Visible(), dispatcher: name, allowsElevation: allowsElevation);

        if (!TakeForGroup(desk, window, out var taken))
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        var position = _groupOptions.InsertAfterCurrent ? target.Current + 1 : target.Count;
        desk.Join(target, window, taken, position);
        desk.Show(target, position);
        TakeGroupFocus(desk, window);
        (_extraEvents ??= []).Add(new ClientIntoGroup(window));
        return Present(desk, covered, desk.Layout.Visible(), dispatcher: name, focus: window, allowsElevation: allowsElevation);
    }

    // Takes a window out of its own Group, or its tile, so it can join another Group.
    private static bool TakeForGroup(LiveWorkspace desk, WindowId window, out Taken taken)
    {
        taken = default;
        if (desk.GroupOf(window) is WindowGroup own)
        {
            var exit = desk.Leave(own, window, out taken);
            if (exit == GroupExit.Detached)
                return true;
            if (exit == GroupExit.NotMember)
                return false;
        }

        return desk.Layout.TryTake(window, out taken);
    }

    private Outcome MoveOutOfGroup(string text, string name, bool allowsElevation)
    {
        if (!_groupOptions.IgnoreGroupLock && _groupsLocked)
            return Rejected(name, "error: groups locked", allowsElevation);

        var window = text != "active" && text.Length > 1 ? Select(text, emptyMeansActive: false) : ActiveWindow();
        if (window is not WindowId id || WorkspaceContaining(id) is not LiveWorkspace desk)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        if (desk.GroupOf(id) is not WindowGroup group)
            return Rejected(name, "error: window not in a group", allowsElevation);

        return OutOfGroup(desk, group, id, direction: null, name, allowsElevation);
    }

    private Outcome OutOfGroup(LiveWorkspace desk, WindowGroup group, WindowId window, string? direction, string name, bool allowsElevation)
    {
        var covered = desk.Covered();
        var exit = desk.Leave(group, window, out var taken);
        if (exit == GroupExit.Detached)
        {
            desk.Layout.NoteFocus(group.Shown);
            if (direction is not null)
                desk.Layout.Preselect(direction);
            desk.Receive(window, taken, WorkArea(desk, _area));
        }

        var focus = _groupOptions.FocusRemovedWindow || exit != GroupExit.Detached ? window : group.Shown;
        TakeGroupFocus(desk, focus);
        (_extraEvents ??= []).Add(new ClientOutOfGroup(window));
        return IsShown(desk.Id)
            ? Present(desk, covered, desk.Layout.Visible(), dispatcher: name, focus: focus, allowsElevation: allowsElevation)
            : View([], dispatcher: name, allowsElevation: allowsElevation);
    }

    private Outcome MoveWindowOrGroup(string text, string name, bool allowsElevation)
    {
        if (!TryGroupDirection(text, out var direction))
            return Rejected(name, "error: unsupported direction: " + (text.Length == 0 ? "" : text[..1]), allowsElevation);

        if (ActiveWindow() is not WindowId id || WorkspaceContaining(id) is not LiveWorkspace desk || Find(id) is not Client client)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        if (client.Fullscreen != FullscreenMode.None)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        var ignore = _groupOptions.IgnoreGroupLock;
        if (!ignore && _groupsLocked)
            return MoveInstead(direction, name, allowsElevation);

        var own = desk.GroupOf(id);
        var ownLocked = own?.Locked == true;
        var neighbor = desk.Layout.Neighbor(id, direction) is WindowId found && found != id ? found : (WindowId?)null;
        var covered = desk.Covered();
        if (neighbor is WindowId next && desk.GroupOf(next) is WindowGroup target)
        {
            if (!ignore && (target.Locked || ownLocked || own?.Denied == true))
                return MoveInstead(direction, name, allowsElevation);

            return IntoGroup(desk, covered, id, target, name, allowsElevation);
        }

        if (neighbor is not null)
        {
            var single = own?.Count == 1 && RulesOf(id).HasFlag(GroupRuleFlags.SetAlways);
            if ((!ignore && ownLocked) || own is null || single)
                return MoveInstead(direction, name, allowsElevation);

            return OutOfGroup(desk, own, id, direction, name, allowsElevation);
        }

        if ((ignore || !ownLocked) && own is not null)
            return OutOfGroup(desk, own, id, direction, name, allowsElevation);

        return own is null ? MoveInstead(direction, name, allowsElevation) : View([], dispatcher: name, allowsElevation: allowsElevation);
    }

    // binds:movefocus_cycles_groupfirst: left and right step through the Group before leaving it at either end.
    private Outcome? CycleGroupFirst(LiveWorkspace desk, WindowId active, string arguments, string name, bool allowsElevation)
    {
        if (!_groupOptions.MoveFocusCyclesGroupFirst || desk.GroupOf(active) is not WindowGroup group)
            return null;

        var word = arguments.Trim();
        var left = word is "l" or "left";
        var right = word is "r" or "right";
        if (!left && !right)
            return null;

        var only = _monitors.Count <= 1 && desk.Layout.Neighbor(active, left ? "l" : "r") is null;
        if (left && (active != group.Members[0] || only))
            return ShowMember(desk, group, group.Current == 0 ? group.Count - 1 : group.Current - 1, name, allowsElevation);
        if (right && (active != group.Members[^1] || only))
            return ShowMember(desk, group, (group.Current + 1) % group.Count, name, allowsElevation);

        return null;
    }

    private Outcome MoveInstead(string direction, string name, bool allowsElevation) =>
        MoveWindowDirection(direction, allowsElevation) with { Dispatcher = name };

    private GroupRuleFlags RulesOf(WindowId window) =>
        _groupRules.TryGetValue(window, out var rules) ? rules : GroupRuleFlags.None;

    private static bool TryGroupDirection(string text, out string direction)
    {
        direction = text.Length == 0 ? "" : text[0] switch
        {
            'l' => "l",
            'r' => "r",
            'u' or 't' => "u",
            'd' or 'b' => "d",
            _ => "",
        };
        return direction.Length > 0;
    }
}
