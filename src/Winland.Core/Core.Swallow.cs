using System.Text.RegularExpressions;

namespace Winland;

public sealed partial class Core
{
    private static readonly TimeSpan SwallowMatchTimeout = TimeSpan.FromMilliseconds(250);

    // Each child Client and the parent Client it swallowed. Hidden is false after toggleswallow
    // shows the parent; the link stays so the parent is not swallowed by a second child. Slot is set
    // when the child took the parent's place in its Group.
    private readonly Dictionary<WindowId, Swallow> _swallows = [];
    private List<WindowId>? _swallowShow;
    private List<WindowId>? _swallowHide;

    private sealed record Swallow(Client Parent, bool Hidden, bool Slot = false);

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

    // A swallowed parent is still a Client: hidden, and on its child's Workspace.
    private void AddSwallowed(List<Client> clients, List<Client> hidden)
    {
        foreach (var (child, swallow) in _swallows)
        {
            if (!swallow.Hidden || WorkspaceContaining(child) is not LiveWorkspace desk)
                continue;

            var parent = swallow.Parent with
            {
                Workspace = desk.Id,
                InhibitingIdle = InhibitsIdle(swallow.Parent, shown: false, active: null),
            };
            clients.Add(parent);
            hidden.Add(parent);
        }
    }

    // The Client of an ancestor process whose class matches swallow_regex and whose title does
    // not match swallow_exception_regex. With several, the one focused most recently.
    private Client? Swallowee(WindowAppeared appeared)
    {
        if (!_session.EnableSwallow || _session.SwallowRegex.Length == 0 || appeared.AncestorProcessIds.Count == 0)
            return null;

        var candidates = new List<Client>();
        var clients = EveryClient();
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

    // Takes the parent out of its Layout and hides it. Returns the placements of the child's
    // Workspace after the parent left it. A parent in a Group hands its place in the Group to the
    // child instead, as the pinned release does, unless the child may not join that Group.
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

    // Only a shown member on the child's own Workspace is swallowed into its Group. The child must be
    // in no Group, and the Group must let it in.
    private bool SwallowIntoGroup(WindowId child, Client parent, LiveWorkspace home, WindowGroup group, LiveWorkspace desk, GroupRuleFlags rules)
    {
        if (home != desk
            || group.Shown != parent.Window
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
        var incoming = new Taken(
            parent.Floating,
            Minimized: false,
            parent.Bounds,
            parent.Class,
            parent.Title,
            parent.ProcessId,
            parent.InitialClass,
            parent.InitialTitle);
        if (!desk.ReplaceMember(group, child, parent.Window, incoming))
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
        if (ActiveWindow() is not WindowId child
            || !_swallows.TryGetValue(child, out var swallow)
            || WorkspaceContaining(child) is not LiveWorkspace desk)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        var covered = desk.Covered();
        IReadOnlyList<Placement> placements;
        if (swallow.Hidden)
        {
            _swallows[child] = swallow with { Hidden = false, Slot = false };
            placements = ReturnParent(swallow.Parent, desk);
        }
        else
        {
            _swallows.Remove(child);
            placements = SwallowParent(child, swallow.Parent, desk, [], RulesOf(child));
        }

        return IsShown(desk.Id)
            ? Present(desk, covered, placements, dispatcher: name, allowsElevation: allowsElevation)
            : View([], dispatcher: name, allowsElevation: allowsElevation);
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
