using System.Text.RegularExpressions;

namespace Winland;

public sealed partial class Core
{
    private static readonly TimeSpan SwallowMatchTimeout = TimeSpan.FromMilliseconds(250);

    // Each child Client and the parent Client it swallowed. Hidden is false after toggleswallow
    // shows the parent; the link stays so the parent is not swallowed by a second child.
    private readonly Dictionary<WindowId, Swallow> _swallows = [];
    private List<WindowId>? _swallowShow;
    private List<WindowId>? _swallowHide;

    private sealed record Swallow(Client Parent, bool Hidden);

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
    // Workspace after the parent left it.
    private IReadOnlyList<Placement> SwallowParent(WindowId child, Client parent, LiveWorkspace desk, IReadOnlyList<Placement> placements)
    {
        if (WorkspaceContaining(parent.Window) is not LiveWorkspace home || home.ClientOf(parent.Window) is not Client record)
            return placements;

        var after = home.Layout.Remove(parent.Window);
        _swallows[child] = new Swallow(record, Hidden: true);
        (_swallowHide ??= []).Add(parent.Window);
        if (_active == parent.Window)
            _active = null;
        if (home.Focus == parent.Window)
            home.Focus = null;

        if (home == desk)
            return after;

        DropIfEmpty(home.Id);
        return placements;
    }

    // The child closed. A parent it still hides goes back into the child's Workspace.
    private WindowId? Unswallow(WindowId child, LiveWorkspace desk)
    {
        if (!_swallows.Remove(child, out var swallow) || !swallow.Hidden)
            return null;

        ReturnParent(swallow.Parent, desk);
        return swallow.Parent.Window;
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
            _swallows[child] = swallow with { Hidden = false };
            placements = ReturnParent(swallow.Parent, desk);
        }
        else
        {
            _swallows.Remove(child);
            placements = SwallowParent(child, swallow.Parent, desk, []);
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
            return Regex.IsMatch(text, "^(?:" + pattern + ")$", RegexOptions.CultureInvariant, SwallowMatchTimeout);
        }
        catch (Exception exception) when (exception is ArgumentException or RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
