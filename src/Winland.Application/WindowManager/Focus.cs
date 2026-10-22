namespace Winland.Application;

/// <summary>Which Client takes focus, the focus history, and the stacking order.</summary>
public sealed partial class WindowManager
{
    private readonly Dictionary<WindowId, FocusRule> _focusRules = [];
    private readonly HashSet<WindowId> _awaitingMapFocus = [];
    private readonly List<WindowId> _focusHistory = [];

    // The Clients from the bottom of the stacking order to the top. A new Client opens on top, and alterzorder and
    // bringactivetotop move one, as Winland orders its window list.
    private readonly List<WindowId> _stacking = [];

    private void Stack(WindowId window, bool top)
    {
        _stacking.Remove(window);
        if (top)
            _stacking.Add(window);
        else
            _stacking.Insert(0, window);
    }
    private WindowId? _unmanagedFocus;
    private WindowId? _beforeUnmanaged;
    private List<StreamEvent>? _extraEvents;

    // A reason a handler sets when the dispatcher name is not why the focus moved, such as the pointer.
    private FocusReason _focusReason;

    private readonly record struct FocusRule(bool NoInitialFocus, bool NoFocus, bool StayFocused);

    private Outcome Consider(WindowFocused focused)
    {
        var window = focused.Window;
        if (_awaitingMapFocus.Remove(window) && focused.Cause != FocusCause.Requested)
            return Restore(Kept());

        if (focused.Cause == FocusCause.Requested)
        {
            NoteFocus(window);
            return View([]);
        }

        if (Find(window) is null)
        {
            if (focused.Cause == FocusCause.Activated)
                return Restore(Kept());

            _beforeUnmanaged = _active;
            _unmanagedFocus = window;
            NoteFocus(window);
            return View([]);
        }

        if (HeldByStay(window) || HasNoFocus(window))
            return Restore(Kept());

        if (focused.Cause == FocusCause.User
            && _config.Input.FollowMouse == 3
            && _keyState.ButtonsHeld
            && _active != window)
            return Restore(Kept());

        // Winland rejects an activation without a fresh input serial, and honors one with it only under
        // focus_on_activate, which a window rule or setprop wins over. A window that takes the foreground with no click or
        // key before it has no such serial. One that takes it right after a click in a Client of another program has one.
        var activated = focused.Cause == FocusCause.Activated;
        var afterInput = ActivatedAfterClick(focused);
        if ((activated || afterInput) && (activated || !(Own(window, WindowProp.FocusOnActivate) ?? _config.Session.FocusOnActivate)))
        {
            if (_active == window)
                return View([]);

            MarkUrgent(window);
            return Restore(Kept());
        }

        if (WorkspaceContaining(window) is { } desk
            && !desk.Id.IsSpecial
            && IsShown(desk.Id)
            && SpecialOn(desk.Monitor) is not null
            && !_config.Input.SpecialFallthrough)
        {
            var hidden = ConcealSpecial(desk.Monitor);
            NoteFocus(window);
            return View([], hide: hidden);
        }

        if (WorkspaceContaining(window) is { } owner && IsShown(owner.Id))
        {
            var covered = owner.Covered();
            var target = FocusUnderFullscreen(owner, window, out var refit);
            if (target != window)
                return Restore(target);

            if (refit is not null)
            {
                NoteFocus(window);
                return Present(owner, covered, refit);
            }
        }

        NoteFocus(window);
        return View([]);
    }

    // A click in a Client of one program, then another program's window taking the foreground, is an activation with
    // input. A click on the window itself, on a window Winland does not manage such as the taskbar, or in the same
    // program, is the user's own change.
    private bool ActivatedAfterClick(WindowFocused focused)
    {
        if (focused.Cause != FocusCause.User
            || focused.Clicked is not WindowId clicked
            || clicked == focused.Window
            || Find(clicked) is not { } source
            || Find(focused.Window) is not { } target)
            return false;

        // A program Winland does not know the process of counts as another program.
        return source.ProcessId == 0 || target.ProcessId == 0 || source.ProcessId != target.ProcessId;
    }

    private void RememberFocus(WindowId window, WindowDecision decision)
    {
        _focusRules[window] = new FocusRule(decision.NoInitialFocus, decision.NoFocus, decision.StayFocused);
        if (_unmanagedFocus == window)
        {
            var prior = _beforeUnmanaged;
            _unmanagedFocus = null;
            _beforeUnmanaged = null;
            if (prior is WindowId kept && IsShownClient(kept))
            {
                _active = kept;
                if (WorkspaceContaining(kept) is { } desk)
                    desk.Focus = kept;
            }
        }

        if (!SkipsInitialFocus(window))
            return;

        _awaitingMapFocus.Add(window);
    }

    private void ForgetFocusMarks(WindowId window)
    {
        if (_lastMouse == window)
            _lastMouse = null;
        if (_unmanagedFocus == window)
        {
            _unmanagedFocus = null;
            _beforeUnmanaged = null;
        }

        if (_beforeUnmanaged == window)
            _beforeUnmanaged = null;
    }

    private void TouchHistory(WindowId window)
    {
        _urgent.Remove(window);
        _focusHistory.Remove(window);
        _focusHistory.Insert(0, window);
    }

    private void Because(FocusReason reason) => _focusReason = reason;

    private void TakeFocus(LiveWorkspace desk, WindowId? window)
    {
        _active = window;
        desk.Focus = window;
        if (window is WindowId id)
            NoteFocus(id);
    }

    private WindowId? FocusAfter(LiveWorkspace desk, bool floating, Rectangle bounds, bool hadBounds)
    {
        var mode = _config.Input.FocusOnClose;
        if (mode == 1)
            return WindowUnder(_pointer ?? Cursor?.Read(), shrink: false);

        if (floating || mode == 2)
            return Recent(desk) ?? desk.LastVisible();

        if (desk.Layout.FocusAfterClose() is WindowId layout && Accepts(desk, layout))
            return layout;

        // The nearest tile wins. A float is taken only when no tile is left.
        var centre = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        if (hadBounds && (Closest(desk, centre, tiledOnly: true) ?? Closest(desk, centre, tiledOnly: false)) is WindowId near)
            return near;

        return null;
    }

    private WindowId? Recent(LiveWorkspace desk)
    {
        foreach (var window in _focusHistory)
        {
            if (Accepts(desk, window))
                return window;
        }

        return null;
    }

    private WindowId? Closest(LiveWorkspace desk, Point point, bool tiledOnly)
    {
        WindowId? best = null;
        var bestDistance = long.MaxValue;
        var showing = desk.Showing();
        foreach (var client in desk.Layout.Clients)
        {
            if (!Accepts(desk, client.Window) || !showing.Contains(client.Window) || (tiledOnly && client.Floating))
                continue;

            if (!desk.Layout.TryBounds(client.Window, out var bounds))
                continue;

            var distance = DistanceTo(point, bounds);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = client.Window;
        }

        return best;
    }

    private bool Accepts(LiveWorkspace desk, WindowId window) =>
        desk.ClientOf(window) is { Minimized: false } && !HasNoFocus(window);

    private static long DistanceTo(Point point, Rectangle box)
    {
        var dx = 0L;
        if (point.X < box.X)
            dx = box.X - point.X;
        else if (point.X >= box.X + box.Width)
            dx = point.X - (box.X + box.Width);

        var dy = 0L;
        if (point.Y < box.Y)
            dy = box.Y - point.Y;
        else if (point.Y >= box.Y + box.Height)
            dy = point.Y - (box.Y + box.Height);

        return (dx * dx) + (dy * dy);
    }

    private bool SkipsInitialFocus(WindowId window)
    {
        if (!_focusRules.TryGetValue(window, out var rule))
            return false;

        return rule.NoInitialFocus || rule.NoFocus || HeldByStay(window);
    }

    private bool HasNoFocus(WindowId window) =>
        BlockedByModal(window) || (SetBool(window, "no_focus") ?? (_focusRules.TryGetValue(window, out var rule) && rule.NoFocus));

    private bool HeldByStay(WindowId incoming)
    {
        if (_active is not WindowId current || current == incoming)
            return false;

        var stays = SetBool(current, "stay_focused") ?? (_focusRules.TryGetValue(current, out var rule) && rule.StayFocused);
        return stays && IsShownClient(current);
    }

    private WindowId? Kept() =>
        _active is WindowId current && IsShownClient(current) ? current : null;

    private Outcome Restore(WindowId? window) =>
        window is WindowId target ? View([]) with { Focus = target } : View([]);

    private bool IsShownClient(WindowId window)
    {
        var desk = WorkspaceContaining(window);
        if (desk is null || !IsShown(desk.Id))
            return false;

        return desk.ClientOf(window) is { Minimized: false };
    }

    private static Outcome Offering(Outcome outcome, WindowId? focus) =>
        focus is WindowId window ? outcome with { Focus = window } : outcome;
}
