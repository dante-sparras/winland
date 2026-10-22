namespace Winland.Application;

/// <summary>The group dispatchers, and moving Clients into and out of groups.</summary>
public sealed partial class WindowManager
{
    private readonly Dictionary<WindowId, GroupRuleFlags> _groupRules = [];
    private bool _groupsLocked;

    private Outcome ToggleGroup(string name)
    {
        if (ActiveWindow() is not WindowId id || WorkspaceContaining(id) is not LiveWorkspace desk)
            return View([], dispatcher: name);

        var covered = desk.Covered();
        if (Find(id) is { Fullscreen: not FullscreenMode.None })
            _ = Restamp(desk, EndFullscreen(desk, id));

        if (desk.GroupOf(id) is not WindowGroup group)
        {
            StartGroup(desk, id);
            return Present(desk, covered, desk.Layout.Visible(), dispatcher: name);
        }

        var head = group.Members[0];
        var focus = desk.Focus;
        var area = WorkArea(desk, _area);
        foreach (var (window, taken) in desk.EndGroup(group))
            desk.Receive(window, taken, area);

        FocusState.Remember(desk, focus);
        if (focus is WindowId kept)
            desk.Layout.NoteFocus(kept);

        (_extraEvents ??= []).Add(new GroupToggled(head, false));
        return Present(desk, covered, desk.Layout.Visible(), dispatcher: name);
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

    private Outcome ChangeGroupActive(string text, string name)
    {
        if (ActiveWindow() is not WindowId id || WorkspaceContaining(id) is not LiveWorkspace desk)
            return Rejected(name, "error: no window found");

        if (desk.GroupOf(id) is not WindowGroup group)
            return Rejected(name, "error: window is not in a group");

        if (group.Count == 1)
            return Rejected(name, "error: only one window in group");

        int index;
        if (int.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var number))
            index = number <= 0 ? group.Count - 1 : Math.Min(number - 1, group.Count - 1);
        else
            index = text is "b" or "prev" ? (group.Current + group.Count - 1) % group.Count : (group.Current + 1) % group.Count;

        return ShowMember(desk, group, index, name);
    }

    // The wheel over a Group's tab bar shows the next or previous member, with group:groupbar:scrolling, and moves the focus
    // only from a shown member that had it, as Winland's moveCurrent does.
    private Outcome ScrollGroupBar(GroupBarScrolled scrolled)
    {
        if (!_config.Groups.Bar.Scrolling || WorkspaceContaining(scrolled.Shown) is not LiveWorkspace desk
            || desk.GroupOf(scrolled.Shown) is not WindowGroup group || group.Count < 2)
        {
            return View([]);
        }

        var index = scrolled.Forward ? (group.Current + 1) % group.Count : (group.Current + group.Count - 1) % group.Count;
        return ShowMember(desk, group, index, "changegroupactive");
    }

    private Outcome ShowMember(LiveWorkspace desk, WindowGroup group, int index, string name)
    {
        var covered = desk.Covered();
        var shown = group.Shown;
        var hadFocus = _focus.Focused == shown;
        if (!desk.Show(group, index))
            return View([], dispatcher: name);

        WindowId? focus = null;
        if (hadFocus)
        {
            focus = group.Shown;
            TakeGroupFocus(desk, group.Shown);
        }

        return IsShown(desk.Id)
            ? Present(desk, covered, desk.Layout.Visible(), dispatcher: name, focus: focus)
            : View([], dispatcher: name);
    }

    private void TakeGroupFocus(LiveWorkspace desk, WindowId window)
    {
        _focus.Take(window, desk);
        desk.Layout.NoteFocus(window);
        TouchHistory(window);
    }

    private Outcome MoveGroupWindow(string text, string name)
    {
        if (ActiveWindow() is not WindowId id || WorkspaceContaining(id) is not LiveWorkspace desk)
            return Rejected(name, "error: no window found");

        if (desk.GroupOf(id) is not WindowGroup group)
            return Rejected(name, "error: window not in a group");

        var other = text is "b" or "prev"
            ? (group.Current == 0 ? group.Count - 1 : group.Current - 1)
            : (group.Current + 1 >= group.Count ? 0 : group.Current + 1);
        group.MoveShownTo(other);
        return View([], dispatcher: name);
    }

    private Outcome LockGroups(string text, string name)
    {
        _groupsLocked = text switch
        {
            "toggle" => !_groupsLocked,
            "lock" or "" or "lockgroups" => true,
            _ => false,
        };
        (_extraEvents ??= []).Add(new GroupsLocked(_groupsLocked));
        return View([], dispatcher: name);
    }

    private Outcome LockActiveGroup(string text, string name)
    {
        if (ActiveWindow() is not WindowId id || WorkspaceContaining(id) is not LiveWorkspace desk)
            return Rejected(name, "error: no window found");

        if (desk.GroupOf(id) is not WindowGroup group)
            return Rejected(name, "error: window not in a group");

        group.Locked = text switch
        {
            "toggle" => !group.Locked,
            "lock" => true,
            _ => false,
        };
        return View([], dispatcher: name);
    }

    private Outcome DenyWindowFromGroup(string text, string name)
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

        return View([], dispatcher: name);
    }

    private Outcome MoveIntoGroup(string text, bool create, string name)
    {
        if (!TryGroupDirection(text, out var direction))
            return Rejected(name, "error: unsupported direction: " + (text.Length == 0 ? "" : text[..1]));

        if (!_config.Groups.IgnoreGroupLock && _groupsLocked)
            return View([], dispatcher: name);

        if (ActiveWindow() is not WindowId id || WorkspaceContaining(id) is not LiveWorkspace desk)
            return View([], dispatcher: name);

        // With nothing beside it on its Workspace, the window looks onto the Monitor in that direction, as the pinned
        // release finds the neighbour across Monitors.
        if (desk.Layout.Neighbor(id, direction) is not WindowId neighbor || neighbor == id)
        {
            if (NeighborAcross(desk, id, direction) is not (WindowId across, LiveWorkspace there))
                return View([], dispatcher: name);

            return IntoGroupAcross(desk, there, id, across, create, name);
        }

        var covered = desk.Covered();
        var target = desk.GroupOf(neighbor);
        if (target is null)
        {
            if (!create || Find(neighbor) is { Fullscreen: not FullscreenMode.None })
                return View([], dispatcher: name);

            target = StartGroup(desk, neighbor);
        }

        if (!_config.Groups.IgnoreGroupLock && (target.Locked || desk.GroupOf(id)?.Locked == true))
            return Present(desk, covered, desk.Layout.Visible(), dispatcher: name);

        return IntoGroup(desk, covered, id, target, name);
    }

    private Outcome IntoGroup(LiveWorkspace desk, HashSet<WindowId> covered, WindowId window, WindowGroup target, string name)
    {
        if (target.Denied || target.Contains(window))
            return Present(desk, covered, desk.Layout.Visible(), dispatcher: name);

        if (!TakeForGroup(desk, window, out var taken))
            return View([], dispatcher: name);

        var position = _config.Groups.InsertAfterCurrent ? target.Current + 1 : target.Count;
        desk.Join(target, window, taken, position);
        desk.Show(target, position);
        TakeGroupFocus(desk, window);
        (_extraEvents ??= []).Add(new ClientIntoGroup(window));
        return Present(desk, covered, desk.Layout.Visible(), dispatcher: name, focus: window);
    }

    private (WindowId Window, LiveWorkspace Desk)? NeighborAcross(LiveWorkspace desk, WindowId window, string direction)
    {
        if (!_config.BindOptions.MonitorFallback || !MonitorSelector.TryDirection(direction, out var side)
            || MonitorHolding(desk) is not LiveMonitor origin || TargetOf(origin) is not MonitorTarget from
            || MonitorSelector.InDirection(Targets(), from, side) is not MonitorTarget neighbor
            || MonitorOf(neighbor) is not LiveMonitor screen || ShownOn(screen) is not WorkspaceId shown
            || FindWorkspace(shown) is not LiveWorkspace there || !desk.Layout.TryBounds(window, out var box)
            || AdjacentOn(screen, Ideal(desk, window, box), side) is not WindowId found)
            return null;

        return (found, there);
    }

    // Moves a window into the Group of a window on another Monitor's Workspace, creating it when asked, and focuses it
    // there. The Workspace it left is laid out again.
    private Outcome IntoGroupAcross(LiveWorkspace desk, LiveWorkspace there, WindowId window, WindowId neighbor, bool create, string name)
    {
        var target = there.GroupOf(neighbor);
        if (target is null)
        {
            if (!create || Find(neighbor) is { Fullscreen: not FullscreenMode.None })
                return View([], dispatcher: name);

            target = StartGroup(there, neighbor);
        }

        if (target.Denied || (!_config.Groups.IgnoreGroupLock && (target.Locked || desk.GroupOf(window)?.Locked == true)))
            return View([], dispatcher: name);

        var covered = there.Covered();
        if (!TakeForGroup(desk, window, out var taken))
            return View([], dispatcher: name);

        var position = _config.Groups.InsertAfterCurrent ? target.Current + 1 : target.Count;
        there.Join(target, window, taken, position);
        there.Show(target, position);
        Restamp(desk, []);
        Restamp(there, []);
        DropIfEmpty(desk.Id);
        Focus(there.Monitor);
        TakeGroupFocus(there, window);
        (_extraEvents ??= []).Add(new ClientIntoGroup(window));
        var outcome = Present(there, covered, there.Layout.Visible(), dispatcher: name, focus: window);
        return FindWorkspace(desk.Id) is LiveWorkspace left && IsShown(left.Id)
            ? outcome with { Placements = [.. left.Layout.Placed(), .. outcome.Placements] }
            : outcome;
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

    private Outcome MoveOutOfGroup(string text, string name)
    {
        if (!_config.Groups.IgnoreGroupLock && _groupsLocked)
            return Rejected(name, "error: groups locked");

        var window = text != "active" && text.Length > 1 ? Select(text, emptyMeansActive: false) : ActiveWindow();
        if (window is not WindowId id || WorkspaceContaining(id) is not LiveWorkspace desk)
            return View([], dispatcher: name);

        if (desk.GroupOf(id) is not WindowGroup group)
            return Rejected(name, "error: window not in a group");

        return OutOfGroup(desk, group, id, direction: null, name);
    }

    private Outcome OutOfGroup(LiveWorkspace desk, WindowGroup group, WindowId window, string? direction, string name)
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

        var focus = _config.Groups.FocusRemovedWindow || exit != GroupExit.Detached ? window : group.Shown;
        TakeGroupFocus(desk, focus);
        (_extraEvents ??= []).Add(new ClientOutOfGroup(window));
        return IsShown(desk.Id)
            ? Present(desk, covered, desk.Layout.Visible(), dispatcher: name, focus: focus)
            : View([], dispatcher: name);
    }

    private Outcome MoveWindowOrGroup(string text, string name)
    {
        if (!TryGroupDirection(text, out var direction))
            return Rejected(name, "error: unsupported direction: " + (text.Length == 0 ? "" : text[..1]));

        if (ActiveWindow() is not WindowId id || WorkspaceContaining(id) is not LiveWorkspace desk || Find(id) is not Client client)
            return View([], dispatcher: name);

        if (client.Fullscreen != FullscreenMode.None)
            return View([], dispatcher: name);

        var ignore = _config.Groups.IgnoreGroupLock;
        if (!ignore && _groupsLocked)
            return MoveInstead(direction, name);

        var own = desk.GroupOf(id);
        var ownLocked = own?.Locked == true;
        var neighbor = desk.Layout.Neighbor(id, direction) is WindowId found && found != id ? found : (WindowId?)null;
        var covered = desk.Covered();
        if (neighbor is WindowId next && desk.GroupOf(next) is WindowGroup target)
        {
            if (!ignore && (target.Locked || ownLocked || own?.Denied == true))
                return MoveInstead(direction, name);

            return IntoGroup(desk, covered, id, target, name);
        }

        if (neighbor is not null)
        {
            var single = own?.Count == 1 && RulesOf(id).HasFlag(GroupRuleFlags.SetAlways);
            if ((!ignore && ownLocked) || own is null || single)
                return MoveInstead(direction, name);

            return OutOfGroup(desk, own, id, direction, name);
        }

        if ((ignore || !ownLocked) && own is not null)
            return OutOfGroup(desk, own, id, direction, name);

        return own is null ? MoveInstead(direction, name) : View([], dispatcher: name);
    }

    // binds:movefocus_cycles_groupfirst: left and right step through the Group before leaving it at either end.
    private Outcome? CycleGroupFirst(LiveWorkspace desk, WindowId active, string arguments, string name)
    {
        if (!_config.Groups.MoveFocusCyclesGroupFirst || desk.GroupOf(active) is not WindowGroup group)
            return null;

        var word = arguments.Trim();
        var left = word is "l" or "left";
        var right = word is "r" or "right";
        if (!left && !right)
            return null;

        var only = _monitors.Count <= 1 && desk.Layout.Neighbor(active, left ? "l" : "r") is null;
        if (left && (active != group.Members[0] || only))
            return ShowMember(desk, group, group.Current == 0 ? group.Count - 1 : group.Current - 1, name);
        if (right && (active != group.Members[^1] || only))
            return ShowMember(desk, group, (group.Current + 1) % group.Count, name);

        return null;
    }

    private Outcome MoveInstead(string direction, string name) =>
        MoveWindowDirection(direction) with { Dispatcher = name };

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
