namespace Winland;

// How Group members follow the Clients' lifecycle: opening, closing, focus and moves.
public sealed partial class Core
{
    /// <summary>
    /// Puts a window that just opened into the focused Group when <c>group:auto_group</c> allows it, or
    /// makes it a Group when a <c>group</c> rule says so. True when the Layout changed.
    /// </summary>
    private bool GroupOnOpen(LiveWorkspace desk, WindowId window, WindowDecision decision, bool dialog, WindowId? focused)
    {
        var rules = decision.Group;
        if (rules != GroupRuleFlags.None)
            _groupRules[window] = rules;
        else
            _groupRules.Remove(window);

        if (_groupOptions.AutoGroup
            && focused is WindowId owner
            && owner != window
            && desk.GroupOf(owner) is WindowGroup group
            && CanJoin(rules, group)
            && !dialog
            && !(decision.Floating && desk.ClientOf(owner)?.Floating != true)
            && desk.Layout.TryTake(window, out var taken))
        {
            var position = _groupOptions.InsertAfterCurrent ? group.Current + 1 : group.Count;
            desk.Join(group, window, taken, position);
            desk.Show(group, position);
            return true;
        }

        if (rules.HasFlag(GroupRuleFlags.Set) && desk.GroupOf(window) is null)
            StartGroup(desk, window);

        return false;
    }

    /// <summary>True when the window is a Group member hidden behind the shown one.</summary>
    private bool IsWaiting(WindowId window) => WorkspaceContaining(window)?.IsWaiting(window) == true;

    private bool CanJoin(GroupRuleFlags rules, WindowGroup group) =>
        !_groupsLocked
        && (rules.HasFlag(GroupRuleFlags.Invade) || !group.Locked)
        && !rules.HasFlag(GroupRuleFlags.Deny)
        && !rules.HasFlag(GroupRuleFlags.Barred);

    /// <summary>
    /// Closes a member of a Group that keeps other members. The next member takes the tile when the
    /// closed one was shown. <paramref name="returned"/> is a parent the member swallowed that has just
    /// come back into the Layout, and it takes the focus the member had. Null when the window was in no
    /// such Group.
    /// </summary>
    private Outcome? CloseGroupMember(WindowId window, LiveWorkspace desk, HashSet<WindowId> covered, WindowId? returned)
    {
        if (desk.GroupOf(window) is not WindowGroup group)
            return null;

        if (group.Count == 1)
        {
            desk.Leave(group, window, out _);
            return null;
        }

        var wasShown = group.Shown == window;
        var wasActive = _active == window;
        desk.Leave(group, window, out _);
        if ((!wasShown && returned is null) || !IsShown(desk.Id))
            return View([]);

        WindowId? focus = null;
        if (wasActive)
        {
            focus = returned ?? group.Shown;
            TakeGroupFocus(desk, focus.Value);
        }

        return Present(desk, covered, desk.Layout.Visible(), focus: focus);
    }

    /// <summary>Shows a hidden member before it is focused. Null when the window is not hidden in a Group.</summary>
    private Outcome? RaiseMember(LiveWorkspace desk, WindowId window, string name, bool allowsElevation)
    {
        if (!desk.IsWaiting(window) || desk.GroupOf(window) is not WindowGroup group)
            return null;

        return ShowMember(desk, group, group.Members.IndexOf(window), name, allowsElevation);
    }

    /// <summary>Moves the Group of a shown member that just moved from <paramref name="source"/> to <paramref name="destination"/>.</summary>
    private static void CarryGroup(LiveWorkspace source, LiveWorkspace destination, WindowId window)
    {
        if (source.Id != destination.Id && source.GroupOf(window) is WindowGroup group && group.Shown == window)
            source.HandGroup(group, destination);
    }

    /// <summary>
    /// The solitary unlocked Group on a Workspace that a moved window joins under
    /// <c>group:group_on_movetoworkspace</c>, or null.
    /// </summary>
    private WindowGroup? SolitaryGroup(LiveWorkspace destination, WindowId moving)
    {
        if (!_groupOptions.GroupOnMoveToWorkspace)
            return null;

        var visible = destination.VisibleWindows();
        if (visible.Length != 1 || visible[0] == moving || destination.GroupOf(visible[0]) is not WindowGroup group)
            return null;

        return CanJoin(RulesOf(moving), group) && !group.Locked ? group : null;
    }

    /// <summary>Joins a window that was just added to <paramref name="destination"/> into <paramref name="group"/>.</summary>
    private void JoinOnMove(LiveWorkspace destination, WindowGroup? group, WindowId window)
    {
        if (group is null || destination.GroupOf(window) is not null || !destination.Layout.TryTake(window, out var taken))
            return;

        var position = _groupOptions.InsertAfterCurrent ? group.Current + 1 : group.Count;
        destination.Join(group, window, taken, position);
        destination.Show(group, position);
    }
}
