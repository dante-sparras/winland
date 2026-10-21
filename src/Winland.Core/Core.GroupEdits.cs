using System.Globalization;

namespace Winland;

// groupadd and groupremove on the pipe: a window put into a given Group at a given place, or taken out of its Group, as
// Winland's Lua group objects do. Neither is a dispatcher, so the Lua config reaches them as pipe commands.
public sealed partial class Core
{
    /// <summary>
    /// <c>groupadd MEMBER WINDOW [INDEX]</c>: puts WINDOW into the Group MEMBER is in, at the 1-based INDEX, or where
    /// <c>group:insert_after_current</c> puts it, and shows it there. A window in another Group brings every member of
    /// that Group, in order. A window on another Workspace or Monitor moves to the Group's.
    /// </summary>
    private CommandReply GroupAdd(string arguments)
    {
        var words = arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 2 or > 3)
            return Refuse("error: expected a group member, a window and an optional index");

        if (Select(words[0], emptyMeansActive: false) is not WindowId member
            || WorkspaceContaining(member) is not LiveWorkspace there
            || there.GroupOf(member) is not WindowGroup group)
            return Refuse("error: window not in a group");

        if (Select(words[1], emptyMeansActive: false) is not WindowId window || WorkspaceContaining(window) is not LiveWorkspace from)
            return Refuse("error: window not found");

        int? index = null;
        if (words.Length == 3)
        {
            if (!int.TryParse(words[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
                return Refuse("error: index must be an integer");
            if (number < 1 || number > group.Count + 1)
                return Refuse("error: index out of range");

            index = number - 1;
        }

        if (group.Contains(window))
            return new CommandReply("ok", View([]));

        if (group.Denied)
            return Refuse("error: target group is denied");

        var own = from.GroupOf(window);
        if (!CanBeGroupedInto(window, own, group))
            return Refuse("error: window cannot be added to group");

        var before = ShowingNow();
        var active = _active;
        var shownBefore = group.Shown;
        WindowId[] moving = own is null ? [window] : [.. own.Members];
        var sources = new HashSet<WorkspaceId>();
        for (var i = 0; i < moving.Length; i++)
        {
            if (WorkspaceContaining(moving[i]) is LiveWorkspace source)
            {
                sources.Add(source.Id);
                AddOne(group, there, source, moving[i], index is int at ? at + i : null);
            }
        }

        WindowId? focus = null;
        if (active is WindowId focused && (focused == shownBefore || Array.IndexOf(moving, focused) >= 0))
            focus = FocusAfterGroupAdd(there, group, sources);

        foreach (var id in sources)
        {
            if (id != there.Id && FindWorkspace(id) is LiveWorkspace left)
            {
                _ = Restamp(left, []);
                DropIfEmpty(id);
            }
        }

        _ = Restamp(there, []);
        var (show, hide) = VisibilityDelta(before);
        return new CommandReply("ok", View(ShownPlacements(), show, hide, focus: focus));

        CommandReply Refuse(string error) => new(error, View([]) with { Error = error });
    }

    // Winland's canBeGroupedInto for a window that is already mapped: no lock on groups, on the Group or on the
    // window's own Group, and no group rule that denies it.
    private bool CanBeGroupedInto(WindowId window, WindowGroup? own, WindowGroup target) =>
        !_groupsLocked
        && !target.Locked
        && own?.Locked != true
        && !RulesOf(window).HasFlag(GroupRuleFlags.Deny);

    // One window into the Group, out of its own Group or tile, shown in the Group's tile. Its own fullscreen ends, and the
    // Group's fullscreen goes to it with the tile.
    private void AddOne(WindowGroup group, LiveWorkspace there, LiveWorkspace source, WindowId window, int? index)
    {
        if (source.ClientOf(window) is { Fullscreen: not FullscreenMode.None } && !source.IsWaiting(window))
            _ = EndFullscreen(source, window);

        if (!TakeForGroup(source, window, out var taken))
            return;

        if (source.Id != there.Id)
            taken = OntoMonitor(taken, source.Monitor, there.Monitor);

        var position = index is int at
            ? Math.Min(at, group.Count)
            : _groupOptions.InsertAfterCurrent ? group.Current + 1 : group.Count;
        there.Join(group, window, taken, position);
        _ = there.Show(group, position);
    }

    // The focus after the focused window left for the Group or was hidden in it. Winland clears the keyboard
    // focus when that window is a hidden member, because hiding it blocks its input. A hidden member is hidden with
    // SW_HIDE, which activates another window, and putting the foreground back on it would send keystrokes to a window
    // that is not visible, so on a Workspace that shows the shown member takes the focus, on its Monitor. Otherwise the
    // Workspace the window left keeps the focus, as a silent move does.
    private WindowId? FocusAfterGroupAdd(LiveWorkspace there, WindowGroup group, HashSet<WorkspaceId> sources)
    {
        if (IsShown(there.Id))
        {
            if (there.Monitor != FocusedId())
                Focus(there.Monitor);

            TakeGroupFocus(there, group.Shown);
            return group.Shown;
        }

        foreach (var id in sources)
        {
            if (id == there.Id || FindWorkspace(id) is not LiveWorkspace left || !IsShown(left.Id))
                continue;

            var next = Accept(left.Remembered());
            _active = next;
            left.Focus = next;
            if (next is WindowId noted)
                left.Layout.NoteFocus(noted);

            return next;
        }

        return null;
    }

    /// <summary>
    /// <c>groupremove WINDOW</c>: takes WINDOW out of its Group. It goes back into the Layout beside the Group's tile, or
    /// keeps the tile when it was the last member. The focus stays where it is.
    /// </summary>
    private CommandReply GroupRemove(string arguments)
    {
        var text = arguments.Trim();
        if (text.Length == 0 || Select(text, emptyMeansActive: false) is not WindowId window || WorkspaceContaining(window) is not LiveWorkspace desk)
            return Refuse("error: window not found");

        if (desk.GroupOf(window) is not WindowGroup group)
            return Refuse("error: window not in a group");

        var before = ShowingNow();
        var focus = desk.Focus;
        var exit = desk.Leave(group, window, out var taken);
        if (exit == GroupExit.Detached)
        {
            desk.Layout.NoteFocus(group.Shown);
            desk.Receive(window, taken, WorkArea(desk, _area));
        }

        if (focus is WindowId kept && desk.ClientOf(kept) is not null && !desk.IsWaiting(kept))
        {
            desk.Focus = kept;
            desk.Layout.NoteFocus(kept);
        }

        _ = Restamp(desk, []);
        var (show, hide) = VisibilityDelta(before);
        return new CommandReply("ok", View(ShownPlacements(), show, hide));

        CommandReply Refuse(string error) => new(error, View([]) with { Error = error });
    }
}
