namespace Winland.Application;

// How Group members follow the Clients' lifecycle: opening, closing, focus and moves.
public sealed partial class WindowManager
{
    /// <summary>
    /// Puts a window that just opened into the focused Group, after the shown member. A dialog, and a floating window
    /// opened over a tiled Group, open on their own. True when the Layout changed.
    /// </summary>
    private static bool GroupOnOpen(LiveWorkspace desk, WindowId window, WindowDecision decision, bool dialog, WindowId? focused)
    {
        if (focused is not WindowId owner
            || owner == window
            || desk.GroupOf(owner) is not WindowGroup group
            || dialog
            || (decision.Floating && desk.ClientOf(owner)?.Floating != true)
            || !desk.Layout.TryTake(window, out var taken))
            return false;

        var position = group.Current + 1;
        desk.Join(group, window, taken, position);
        desk.Show(group, position);
        return true;
    }

    /// <summary>True when the window is a Group member hidden behind the shown one.</summary>
    private bool IsWaiting(WindowId window) => WorkspaceContaining(window)?.IsWaiting(window) == true;

    /// <summary>
    /// Closes a member of a Group that keeps other members. The next member takes the tile when the
    /// closed one was shown. Null when the window was in no such Group.
    /// </summary>
    private Outcome? CloseGroupMember(WindowId window, LiveWorkspace desk, HashSet<WindowId> covered, FullscreenMode mode)
    {
        if (desk.GroupOf(window) is not WindowGroup group)
            return null;

        if (group.Count == 1)
        {
            desk.Leave(group, window, out _);
            return null;
        }

        var wasShown = group.Shown == window;
        var wasActive = _focus.Focused == window;
        desk.Leave(group, window, out _);

        // The member that takes the slot does not inherit the closed member's mode, which
        // ends that mode before the window leaves its Group.
        if (wasShown && mode != FullscreenMode.None && desk.ClientOf(group.Shown) is { Fullscreen: not FullscreenMode.None })
            desk.Layout.SetFullscreen(group.Shown, FullscreenMode.None);

        if (!wasShown || !_workspaces.Shown.IsShown(desk.Id))
        {
            // A focused member closing on a hidden Workspace leaves no window focused, as any other close does.
            if (wasActive)
                Give(null, null);

            return View([]);
        }

        WindowId? focus = null;
        if (wasActive)
        {
            // the member focused most recently takes the focus, and so is shown.
            focus = RecentMember(group) ?? group.Shown;
            if (focus != group.Shown && group.IndexOf(focus.Value) is var index and >= 0)
                _ = desk.Show(group, index);

            Give(focus.Value, desk);
        }

        return Present(desk, covered, desk.Layout.Visible(), focus: focus);
    }

    private WindowId? RecentMember(WindowGroup group)
    {
        foreach (var window in _focus.History)
        {
            if (group.Members.Contains(window))
                return window;
        }

        return null;
    }

    /// <summary>Shows a hidden member before it is focused. Null when the window is not hidden in a Group.</summary>
    private Outcome? RaiseMember(LiveWorkspace desk, WindowId window)
    {
        if (!desk.IsWaiting(window) || desk.GroupOf(window) is not WindowGroup group)
            return null;

        return ShowMember(desk, group, group.IndexOf(window));
    }

    /// <summary>Moves the Group of a shown member that just moved from <paramref name="source"/> to <paramref name="destination"/>.</summary>
    private static void CarryGroup(LiveWorkspace source, LiveWorkspace destination, WindowId window)
    {
        if (source.Id != destination.Id && source.GroupOf(window) is WindowGroup group && group.Shown == window)
            source.HandGroup(group, destination);
    }

}
