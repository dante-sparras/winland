namespace Winland;

public sealed partial class Core
{
    private readonly Dictionary<WindowId, FocusRule> _focusRules = [];
    private readonly HashSet<WindowId> _awaitingMapFocus = [];
    private readonly List<WindowId> _focusHistory = [];
    private SessionOptions _session = SessionOptions.Documented;
    private WindowId? _unmanagedFocus;
    private WindowId? _beforeUnmanaged;
    private List<StreamEvent>? _extraEvents;

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
            && _input.FollowMouse == 3
            && _keyState.DownButtons.Count > 0
            && _active != window)
            return Restore(Kept());

        if (focused.Cause == FocusCause.Activated && !_session.FocusOnActivate)
        {
            if (_active == window)
                return View([]);

            (_extraEvents ??= []).Add(new ClientUrgent(window));
            return Restore(Kept());
        }

        if (DeskContaining(window) is { } desk
            && !desk.Id.IsSpecial
            && IsShown(desk.Id)
            && SpecialOn(desk.Monitor) is not null
            && !_input.SpecialFallthrough)
        {
            var hidden = ConcealSpecial(desk.Monitor);
            NoteFocus(window);
            return View([], hide: hidden);
        }

        NoteFocus(window);
        return View([]);
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
                if (DeskContaining(kept) is { } desk)
                    desk.Focus = kept;
            }
        }

        if (!SkipsInitialFocus(window))
            return;

        _awaitingMapFocus.Add(window);
    }

    private void ForgetFocus(WindowId window)
    {
        _focusRules.Remove(window);
        _awaitingMapFocus.Remove(window);
        _focusHistory.Remove(window);
    }

    private void TouchHistory(WindowId window)
    {
        _focusHistory.Remove(window);
        _focusHistory.Insert(0, window);
    }

    private void TakeFocus(Desk desk, WindowId? window)
    {
        _active = window;
        desk.Focus = window;
        if (window is WindowId id)
            NoteFocus(id);
    }

    private WindowId? FocusAfter(Desk desk, bool floating, Rectangle bounds, bool hadBounds)
    {
        var mode = _input.FocusOnClose;
        if (mode == 1)
            return WindowUnder(_pointer ?? Cursor?.Read(), shrink: false);

        if (floating || mode == 2)
            return Recent(desk) ?? LastVisible(desk);

        if (desk.Layout.FocusAfterClose() is WindowId layout && Accepts(desk, layout))
            return layout;

        if (hadBounds && Closest(desk, new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2))) is WindowId near)
            return near;

        return null;
    }

    private WindowId? Recent(Desk desk)
    {
        foreach (var window in _focusHistory)
        {
            if (Accepts(desk, window))
                return window;
        }

        return null;
    }

    private WindowId? Closest(Desk desk, Point point)
    {
        WindowId? best = null;
        var bestDistance = long.MaxValue;
        var showing = Showing(desk);
        foreach (var client in desk.Layout.Clients)
        {
            if (!Accepts(desk, client.Window) || !showing.Contains(client.Window))
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

    private bool Accepts(Desk desk, WindowId window) =>
        ClientOn(desk, window) is { Minimized: false } && !HasNoFocus(window);

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
        _focusRules.TryGetValue(window, out var rule) && rule.NoFocus;

    private bool HeldByStay(WindowId incoming)
    {
        if (_active is not WindowId current || current == incoming)
            return false;

        return _focusRules.TryGetValue(current, out var rule) && rule.StayFocused && IsShownClient(current);
    }

    private WindowId? Kept() =>
        _active is WindowId current && IsShownClient(current) ? current : null;

    private Outcome Restore(WindowId? window) =>
        window is WindowId target ? View([]) with { Focus = target } : View([]);

    private bool IsShownClient(WindowId window)
    {
        var desk = DeskContaining(window);
        if (desk is null || !IsShown(desk.Id))
            return false;

        return ClientOn(desk, window) is { Minimized: false };
    }

    private static Outcome Offering(Outcome outcome, WindowId? focus) =>
        focus is WindowId window ? outcome with { Focus = window } : outcome;
}
