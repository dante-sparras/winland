using System.Text.RegularExpressions;

namespace Winland;

public sealed partial class Core
{
    private static readonly TimeSpan SwallowMatchTimeout = TimeSpan.FromMilliseconds(250);

    // Each child Client and the parent Client it swallowed. Hidden is false after toggleswallow
    // shows the parent; the link stays so the parent is not swallowed by a second child. Slot is set
    // when the child took the parent's place in its Group. HiddenChild is the child, hidden, when toggleswallow
    // gave that place back to the parent, as the pinned release reveals a swallowed Group member.
    private readonly Dictionary<WindowId, Swallow> _swallows = [];
    private List<WindowId>? _swallowShow;
    private List<WindowId>? _swallowHide;

    private sealed record Swallow(Client Parent, bool Hidden, bool Slot = false, Client? HiddenChild = null);

    private WindowId? SwallowedBy(WindowId child) =>
        _swallows.TryGetValue(child, out var swallow) ? swallow.Parent.Window : null;

    private bool IsSwallowedParent(WindowId window)
    {
        foreach (var swallow in _swallows.Values)
        {
            if (swallow.Hidden && swallow.Parent.Window == window)
                return true;
        }

        return false;
    }

    private bool HasSwallower(WindowId window)
    {
        foreach (var swallow in _swallows.Values)
        {
            if (swallow.Parent.Window == window)
                return true;
        }

        return false;
    }

    private bool IsHiddenChild(WindowId window)
    {
        foreach (var swallow in _swallows.Values)
        {
            if (swallow.HiddenChild?.Window == window)
                return true;
        }

        return false;
    }

    // The Clients swallowing hides: hidden parents, and children hidden behind a parent shown in their Group slot.
    private WindowId[] SwallowedParents()
    {
        var parents = new List<WindowId>();
        foreach (var swallow in _swallows.Values)
        {
            if (swallow.Hidden)
                parents.Add(swallow.Parent.Window);
            if (swallow.HiddenChild is Client child)
                parents.Add(child.Window);
        }

        return [.. parents];
    }

    // A swallowed parent is still a Client: hidden, and on its child's Workspace.
    private void AddSwallowed(List<Client> clients, List<Client> hidden)
    {
        foreach (var (child, swallow) in _swallows)
        {
            if (!swallow.Hidden || SwallowHome(child, _swallows.Count) is not LiveWorkspace desk)
                continue;

            var parent = swallow.Parent with
            {
                Workspace = desk.Id,
                Swallowing = SwallowedBy(swallow.Parent.Window),
                InhibitingIdle = InhibitsIdle(swallow.Parent, shown: false, active: null),
            };
            clients.Add(parent);
            hidden.Add(parent);
        }

        foreach (var swallow in _swallows.Values)
        {
            if (swallow.HiddenChild is not Client child || WorkspaceContaining(swallow.Parent.Window) is not LiveWorkspace home)
                continue;

            var hiddenChild = child with
            {
                Workspace = home.Id,
                Swallowing = swallow.Parent.Window,
                InhibitingIdle = InhibitsIdle(child, shown: false, active: null),
            };
            clients.Add(hiddenChild);
            hidden.Add(hiddenChild);
        }
    }

    // The Workspace of a child, or, for a child that is swallowed in turn, of the child that hides it.
    private LiveWorkspace? SwallowHome(WindowId child, int depth)
    {
        if (WorkspaceContaining(child) is LiveWorkspace desk)
            return desk;

        if (depth <= 0)
            return null;

        foreach (var (outer, swallow) in _swallows)
        {
            if (swallow.Hidden && swallow.Parent.Window == child)
                return SwallowHome(outer, depth - 1);
        }

        return null;
    }

    // The Client of an ancestor process whose class matches swallow_regex and whose title does
    // not match swallow_exception_regex. With several, the one focused most recently.
    private Client? Swallowee(WindowAppeared appeared)
    {
        if (!_session.EnableSwallow || _session.SwallowRegex.Length == 0 || appeared.AncestorProcessIds.Count == 0)
            return null;

        // A swallowed Client is hidden, so it takes no input and swallows nothing, as in the pinned release.
        var candidates = new List<Client>();
        var clients = EveryClient();
        clients.RemoveAll(client => IsSwallowedParent(client.Window) || IsHiddenChild(client.Window));
        foreach (var ancestor in appeared.AncestorProcessIds)
        {
            foreach (var client in clients)
            {
                if (client.ProcessId == ancestor && client.Window != appeared.Window)
                    candidates.Add(client);
            }
        }

        candidates.RemoveAll(client => !FullMatch(client.Class, _session.SwallowRegex));
        if (_session.SwallowExceptionRegex.Length > 0)
            candidates.RemoveAll(client => FullMatch(client.Title, _session.SwallowExceptionRegex));

        if (candidates.Count == 0)
            return null;

        var chosen = candidates[0];
        if (candidates.Count > 1)
        {
            foreach (var window in _focusHistory)
            {
                if (candidates.Find(client => client.Window == window) is { } recent)
                {
                    chosen = recent;
                    break;
                }
            }
        }

        return HasSwallower(chosen.Window) ? null : chosen;
    }

    // Takes the parent out of its Layout and hides it. Returns the placements of the Workspace the
    // child ended on. A shown parent in a Group hands its place in the Group to the child, and the
    // Group stays in the Layout, as the pinned release's replaceMember does. A Group that denies the
    // child keeps its parent. A Group on another Workspace takes the child onto that Workspace.
    private IReadOnlyList<Placement> SwallowParent(WindowId child, Client parent, LiveWorkspace desk, IReadOnlyList<Placement> placements, GroupRuleFlags rules = GroupRuleFlags.None)
    {
        if (WorkspaceContaining(parent.Window) is not LiveWorkspace home || home.ClientOf(parent.Window) is not Client record)
            return placements;

        if (home.GroupOf(parent.Window) is WindowGroup group)
            return SwallowIntoGroup(child, record, home, group, desk, rules) ? home.Layout.Visible() : placements;

        var after = home.Layout.Remove(parent.Window);
        _swallows[child] = new Swallow(record, Hidden: true);
        HideParent(parent.Window, home);

        if (home == desk)
            return after;

        DropIfEmpty(home.Id);
        return placements;
    }

    // Only a shown member is swallowed into its Group. The child must be in no Group, and the Group
    // must let it in. A Group on another Workspace takes the child there, as replaceMember moves the
    // new window onto the Group's Workspace. A hidden member is not swallowed.
    private bool SwallowIntoGroup(WindowId child, Client parent, LiveWorkspace home, WindowGroup group, LiveWorkspace desk, GroupRuleFlags rules)
    {
        if (group.Shown != parent.Window
            || group.Denied
            || !CanJoin(rules, group)
            || desk.GroupOf(child) is not null
            || !desk.Take(child, out var taken))
            return false;

        if (!home.ReplaceMember(group, parent.Window, child, taken))
        {
            desk.Receive(child, taken, WorkArea(desk, _area));
            return false;
        }

        // The slot keeps the parent's modes. The child has its own, such as a fullscreen rule asked for,
        // as it would in the pinned release, where it opens in a slot of its own.
        if (home.ClientOf(child) is Client slotted
            && (slotted.Fullscreen != taken.Fullscreen || slotted.FullscreenClient != taken.FullscreenClient))
            home.Layout.SetFullscreen(child, taken.Fullscreen, taken.FullscreenClient);

        _swallows[child] = new Swallow(parent, Hidden: true, Slot: true);
        HideParent(parent.Window, home);
        if (home != desk)
            DropIfEmpty(desk.Id);

        return true;
    }

    private void HideParent(WindowId parent, LiveWorkspace home)
    {
        (_swallowHide ??= []).Add(parent);
        if (_active == parent)
            _active = null;
        if (home.Focus == parent)
            home.Focus = null;
    }

    // The child closed. A parent it still hides goes back into the child's Workspace.
    private WindowId? Unswallow(WindowId child, LiveWorkspace desk)
    {
        if (!_swallows.Remove(child, out var swallow) || !swallow.Hidden)
            return null;

        ReturnParent(swallow.Parent, desk);
        return swallow.Parent.Window;
    }

    // The child closed while it held its parent's place in a Group. The parent takes that place back.
    // Null when the child is no longer in a Group, so the parent comes back as its own tile.
    private Outcome? GiveSlotBack(WindowId child, LiveWorkspace desk, HashSet<WindowId> covered, bool wasActive)
    {
        if (!_swallows.TryGetValue(child, out var swallow)
            || swallow is not { Hidden: true, Slot: true }
            || desk.GroupOf(child) is not WindowGroup group)
            return null;

        var parent = swallow.Parent;
        if (desk.ClientOf(child) is not Client slotted || !SwapSlot(desk, group, slotted, parent))
            return null;

        _swallows.Remove(child);
        if (!IsShown(desk.Id))
        {
            // The window registry leaves _active to Close, which must not keep the closed child.
            if (wasActive)
                _active = null;

            return View([]);
        }

        WindowId? focus = null;
        if (group.Shown == parent.Window)
        {
            (_swallowShow ??= []).Add(parent.Window);
            if (wasActive)
            {
                focus = parent.Window;
                TakeGroupFocus(desk, parent.Window);
            }
        }

        return Present(desk, covered, desk.Layout.Visible(), focus: focus);
    }

    private IReadOnlyList<Placement> ReturnParent(Client parent, LiveWorkspace desk)
    {
        var shown = IsShown(desk.Id);
        var placements = desk.Layout.Add(
            parent.Window,
            WorkArea(desk, _area),
            pointer: null,
            parent.Floating,
            parent.Bounds,
            desk.Id,
            parent.Class,
            parent.Title,
            shown,
            FocusedMonitorBounds(),
            parent.ProcessId,
            parent.InitialClass,
            parent.InitialTitle);
        if (shown)
            (_swallowShow ??= []).Add(parent.Window);

        return placements;
    }

    // A hidden parent closed that had swallowed a window of its own. That window comes back on the Workspace of the
    // child that hid the parent, as in the pinned release.
    private Outcome ReturnSwallowedInTurn(WindowId closed, LiveWorkspace? home)
    {
        if (!_swallows.Remove(closed, out var own) || !own.Hidden || home is null)
            return View([]);

        var covered = home.Covered();
        var placements = ReturnParent(own.Parent, home);
        return IsShown(home.Id) ? Present(home, covered, placements) : View([]);
    }

    // The parent closed. True when it was hidden, so no Layout holds it.
    private bool ForgetSwallowedParent(WindowId window)
    {
        var hidden = false;
        foreach (var (child, swallow) in _swallows.ToArray())
        {
            if (swallow.Parent.Window != window)
                continue;

            hidden |= swallow.Hidden;
            _swallows.Remove(child);
        }

        return hidden;
    }

    private Outcome ToggleSwallow(bool allowsElevation)
    {
        const string name = "toggleswallow";
        if (ActiveWindow() is not WindowId active
            || SwallowerOf(active) is not WindowId child
            || !_swallows.TryGetValue(child, out var swallow)
            || (WorkspaceContaining(child) ?? WorkspaceContaining(swallow.Parent.Window)) is not LiveWorkspace desk)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        var covered = desk.Covered();
        IReadOnlyList<Placement> placements;
        WindowId? focus = null;
        if (ToggleSlot(desk, child, swallow, active) is WindowId slotted)
        {
            focus = active == slotted ? null : FocusAfterSlot(desk, active, child, swallow.Parent.Window);
            placements = desk.Layout.Visible();
        }
        else if (swallow.Hidden)
        {
            _swallows[child] = swallow with { Hidden = false, Slot = false };
            placements = ReturnParent(swallow.Parent, desk);
        }
        else
        {
            // Hiding the parent again from the parent itself gives the focus to the child, as in the pinned release.
            _swallows.Remove(child);
            placements = SwallowParent(child, swallow.Parent, desk, [], RulesOf(child));
            if (active == swallow.Parent.Window)
            {
                TakeFocus(desk, child);
                focus = child;
            }
        }

        return IsShown(desk.Id)
            ? Present(desk, covered, placements, dispatcher: name, focus: focus, allowsElevation: allowsElevation)
            : View([], dispatcher: name, allowsElevation: allowsElevation);
    }

    // toggleswallow in a Group slot, as the pinned release's toggleGroupSlot: the hidden one of the parent and the child
    // takes the slot, and the other is hidden. The window now in the slot, or null when the relation is not in a slot.
    private WindowId? ToggleSlot(LiveWorkspace desk, WindowId child, Swallow swallow, WindowId active)
    {
        if (!swallow.Slot)
            return null;

        if (swallow.Hidden)
        {
            if (desk.GroupOf(child) is not WindowGroup group || desk.ClientOf(child) is not Client current || !SwapSlot(desk, group, current, swallow.Parent))
                return null;

            _swallows[child] = swallow with { Hidden = false, HiddenChild = Unslotted(current) };
            Conceal(child, swallow.Parent.Window, group, desk);
            return swallow.Parent.Window;
        }

        if (swallow.HiddenChild is not Client hidden
            || desk.GroupOf(swallow.Parent.Window) is not WindowGroup slot
            || desk.ClientOf(swallow.Parent.Window) is not Client parent
            || !SwapSlot(desk, slot, parent, hidden))
            return null;

        _swallows[child] = new Swallow(Unslotted(parent), Hidden: true, Slot: true);
        Conceal(swallow.Parent.Window, child, slot, desk);
        return child;
    }

    // The window that left a slot is hidden, and the one that came in is shown when its Group shows it.
    private void Conceal(WindowId left, WindowId came, WindowGroup group, LiveWorkspace desk)
    {
        (_swallowHide ??= []).Add(left);
        if (group.Shown == came && IsShown(desk.Id))
            (_swallowShow ??= []).Add(came);
    }

    // A window that left focus with its slot hands it to the window that took the slot.
    private WindowId? FocusAfterSlot(LiveWorkspace desk, WindowId active, WindowId child, WindowId parent)
    {
        var came = active == child ? parent : child;
        TakeGroupFocus(desk, came);
        return came;
    }

    // A Client out of its slot keeps its own client mode and none of the slot's internal one.
    private static Client Unslotted(Client client) =>
        client with { Fullscreen = FullscreenMode.None, LayoutFullscreen = false };

    // Puts incoming in the Group slot outgoing holds, as the pinned release's replaceMember: the slot's internal
    // fullscreen mode stays with the slot, and each window keeps its own client mode.
    private bool SwapSlot(LiveWorkspace desk, WindowGroup group, Client outgoing, Client incoming)
    {
        var taken = new Taken(
            incoming.Floating,
            Minimized: false,
            incoming.Bounds,
            incoming.Class,
            incoming.Title,
            incoming.ProcessId,
            incoming.InitialClass,
            incoming.InitialTitle);
        if (!desk.ReplaceMember(group, outgoing.Window, incoming.Window, taken))
            return false;

        if (group.Shown == incoming.Window
            && desk.ClientOf(incoming.Window) is Client placed
            && (placed.Fullscreen != outgoing.Fullscreen || placed.FullscreenClient != incoming.FullscreenClient))
            desk.Layout.SetFullscreen(incoming.Window, outgoing.Fullscreen, incoming.FullscreenClient, outgoing.LayoutFullscreen);

        return true;
    }

    // The parent of a child it hides in its Group slot closed. The child takes the slot back, as in the pinned release.
    private Outcome? GiveSlotToChild(WindowId closed)
    {
        foreach (var (child, swallow) in _swallows)
        {
            if (swallow.Parent.Window != closed || swallow.HiddenChild is not Client hidden)
                continue;

            _swallows.Remove(child);
            if (WorkspaceContaining(closed) is not LiveWorkspace desk
                || desk.GroupOf(closed) is not WindowGroup group
                || desk.ClientOf(closed) is not Client parent)
                return null;

            var covered = desk.Covered();
            var wasActive = _active == closed;
            if (!SwapSlot(desk, group, parent, hidden))
                return null;

            if (!IsShown(desk.Id))
                return View([]);

            WindowId? focus = null;
            if (group.Shown == child)
            {
                (_swallowShow ??= []).Add(child);
                if (wasActive)
                {
                    TakeGroupFocus(desk, child);
                    focus = child;
                }
            }

            return Present(desk, covered, desk.Layout.Visible(), focus: focus);
        }

        return null;
    }

    // The child that swallowed this window, or the window itself when it swallowed one.
    private WindowId? SwallowerOf(WindowId window)
    {
        if (_swallows.ContainsKey(window))
            return window;

        foreach (var (child, swallow) in _swallows)
        {
            if (swallow.Parent.Window == window)
                return child;
        }

        return null;
    }

    private (IReadOnlyList<WindowId> Show, IReadOnlyList<WindowId> Hide) TakeSwallowChanges()
    {
        IReadOnlyList<WindowId> show = _swallowShow ?? [];
        IReadOnlyList<WindowId> hide = _swallowHide ?? [];
        _swallowShow = null;
        _swallowHide = null;
        return (show, hide);
    }

    private static bool FullMatch(string text, string pattern)
    {
        try
        {
            return Regex.IsMatch(text, $"\\A(?:{pattern})\\z", RegexOptions.CultureInvariant, SwallowMatchTimeout);
        }
        catch (Exception exception) when (exception is ArgumentException or RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
