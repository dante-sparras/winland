namespace Winland.Application;

/// <summary>When a window may take focus, how focus is given, and the stacking order. <see cref="FocusState"/> holds which Client has it.</summary>
public sealed partial class WindowManager
{
    private readonly HashSet<WindowId> _awaitingMapFocus = [];

    // The Clients from the bottom of the stacking order to the top. A new Client opens on top, and a floating
    // Client that takes the focus moves to the top.
    private readonly List<WindowId> _stacking = [];

    private void Stack(WindowId window)
    {
        _stacking.Remove(window);
        _stacking.Add(window);
    }

    private List<StreamEvent>? _extraEvents;

    private Outcome Consider(WindowFocused focused)
    {
        var window = focused.Window;
        if (_awaitingMapFocus.Remove(window) && focused.Cause != FocusCause.Requested)
            return Restore(Kept());

        if (focused.Cause == FocusCause.Requested)
        {
            Foreground(window);
            return View([]);
        }

        if (Find(window) is null)
        {
            if (focused.Cause == FocusCause.Activated)
                return Restore(Kept());

            _focus.UnmanagedTook(window);
            Foreground(window);
            return View([]);
        }

        if (HasNoFocus(window))
            return Restore(Kept());

        if (focused.Cause == FocusCause.User
            && _config.Input.FollowMouse == 3
            && _keyState.ButtonsHeld
            && _focus.Focused != window)
            return Restore(Kept());

        // Winland rejects an activation without a fresh input serial, and honors one with it only under
        // focus_on_activate, which a window rule or setprop wins over. A window that takes the foreground with no click or
        // key before it has no such serial. One that takes it right after a click in a Client of another program has one.
        var activated = focused.Cause == FocusCause.Activated;
        var afterInput = ActivatedAfterClick(focused);
        if ((activated || afterInput) && (activated || !(_props.Own(window, WindowProp.FocusOnActivate) ?? _config.Session.FocusOnActivate)))
        {
            if (_focus.Focused == window)
                return View([]);

            MarkUrgent(window);
            return Restore(Kept());
        }

        if (WorkspaceContaining(window) is { } desk
            && !desk.Id.IsSpecial
            && _workspaces.Shown.IsShown(desk.Id)
            && _workspaces.Shown.SpecialOn(desk.Monitor) is not null)
        {
            var hidden = ConcealSpecial(desk.Monitor);
            Foreground(window);
            return View([], hide: hidden);
        }

        if (WorkspaceContaining(window) is { } owner && _workspaces.Shown.IsShown(owner.Id))
        {
            var covered = owner.Covered();
            var target = FocusUnderFullscreen(owner, window, out var refit);
            if (target != window)
                return Restore(target);

            if (refit is not null)
            {
                Foreground(window);
                return Present(owner, covered, refit);
            }
        }

        Foreground(window);
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
        if (_focus.Adopted(window) is WindowId kept && IsShownClient(kept))
            Give(kept, WorkspaceContaining(kept));

        if (!SkipsInitialFocus(window))
            return;

        _awaitingMapFocus.Add(window);
    }

    private void ForgetFocusMarks(WindowId window)
    {
        if (_lastMouse == window)
            _lastMouse = null;
        _focus.Forget(window);
        foreach (var desk in _workspaces)
        {
            if (desk.Focus == window)
                FocusState.Remember(desk, null);
        }
    }

    // Gives focus to a window, or to none, and does all of it: the Workspace remembers the window, and a window on the
    // Workspace its Monitor gives the keyboard to focuses that Monitor, tells its Layout, and goes first in the focus
    // history with its urgency cleared.
    private void Give(WindowId? window, LiveWorkspace? desk, bool pointer = false)
    {
        _focus.Take(window, desk);
        if (window is not WindowId given || desk is null || !_workspaces.Shown.IsShown(desk.Id) || !IsCurrent(desk))
            return;

        if (_monitors.Count > 0)
            Focus(desk.Monitor);

        if (pointer)
            desk.Layout.NotePointerFocus(given);
        else
            desk.Layout.NoteFocus(given);

        _urgent.Remove(given);
        _focus.Confirm(given);
    }

    // The Workspace a Monitor gives the keyboard to: its special workspace when one is open, else the one it shows.
    private bool IsCurrent(LiveWorkspace desk) =>
        (_workspaces.Shown.SpecialOn(desk.Monitor) ?? _workspaces.Shown.ActiveOn(desk.Monitor)) == desk.Id;

    // Windows says this window has the foreground. The Client that already has focus is given nothing more, as it is when
    // Windows reports a focus Winland gave. A Client the Monitor gives the keyboard to takes the focus. One on a
    // Workspace that is shown behind a special workspace only focuses its Monitor. A window Winland does not manage
    // leaves nothing focused.
    private void Foreground(WindowId window, bool pointer = false)
    {
        if (_focus.Focused == window)
            return;

        if (WorkspaceContaining(window) is { } owner && _workspaces.Shown.IsShown(owner.Id))
        {
            if (IsCurrent(owner))
                Give(window, owner, pointer);
            else if (_monitors.Count > 0)
                Focus(owner.Monitor);
        }
        else if (Find(window) is null)
            Give(null, null);
    }

    private WindowId? FocusAfter(LiveWorkspace desk, bool floating, Rectangle bounds, bool hadBounds)
    {
        if (floating)
            return _focus.Recent(desk) ?? desk.LastVisible();

        if (desk.Layout.FocusAfterClose() is WindowId layout && Accepts(desk, layout))
            return layout;

        // The nearest tile wins. A float is taken only when no tile is left.
        var centre = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        if (hadBounds && (Closest(desk, centre, tiledOnly: true) ?? Closest(desk, centre, tiledOnly: false)) is WindowId near)
            return near;

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
        if (_props.FocusRuleOf(window) is not FocusRule rule)
            return false;

        return rule.NoInitialFocus || rule.NoFocus;
    }

    private bool HasNoFocus(WindowId window) =>
        ShadowedByModal(window) || _props.NoFocus(window);

    private WindowId? Kept() =>
        _focus.Focused is WindowId current && IsShownClient(current) ? current : null;

    private Outcome Restore(WindowId? window) =>
        window is WindowId target ? View([]) with { Focus = target } : View([]);

    private bool IsShownClient(WindowId window)
    {
        var desk = WorkspaceContaining(window);
        if (desk is null || !_workspaces.Shown.IsShown(desk.Id))
            return false;

        return desk.ClientOf(window) is { Minimized: false };
    }

    private static Outcome Offering(Outcome outcome, WindowId? focus) =>
        focus is WindowId window ? outcome with { Focus = window } : outcome;
}
