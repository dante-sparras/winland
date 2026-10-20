namespace Winland;

public sealed partial class Core
{
    // Every finger a touch screen swipe holds: the one that swipes, and any that came down while it did, which stay
    // held until they lift.
    private readonly HashSet<(long Device, int Id)> _heldTouches = [];
    private EdgeSwipe? _touchSwipe;

    // gestures:workspace_swipe_touch: a finger that comes down at the left or right edge of the Monitor, inside the
    // outer gap and the border, swipes Workspaces as the touchpad does, or the top or bottom edge with a vertical
    // workspaces style. The Monitor is the one the touch screen is mapped to, or the focused one when that is not known.
    private Outcome TouchDown(TouchDown down)
    {
        if (!_digitizer.Touch.Enabled)
            return CancelTouch();

        down = PlaceTouch(down);
        var finger = (down.Device, down.Id);
        if (_touchSwipe is not null || _heldTouches.Count > 0 || _workspaceSwipe is not null)
        {
            _heldTouches.Add(finger);
            return View([]);
        }

        if (!_swipe.Touch || SessionLock?.Locked == true)
            return View([]);

        var monitor = TouchMonitor(down.Display);
        if (MonitorById(monitor) is not { Effective.Enabled: true } screen || screen.Bounds.Width <= 0 || screen.Bounds.Height <= 0)
            return View([]);

        var vertical = SwipeSpan(ActiveOn(monitor), screen.Bounds).Vertical;
        var position = TouchPosition(down.X, down.Y, down.Display, screen.Bounds, vertical);
        var size = vertical ? screen.Bounds.Height : screen.Bounds.Width;
        var scale = screen.Effective.Scale > 0 ? screen.Effective.Scale : 1;
        var near = ((vertical ? _options.GapsOut.Top : _options.GapsOut.Left) + _options.BorderSize) * scale / size;
        var far = 1 - (((vertical ? _options.GapsOut.Bottom : _options.GapsOut.Right) + _options.BorderSize) * scale / size);
        if (position >= near && position <= far)
            return View([]);

        BeginWorkspaceSwipe(monitor);
        var fromEnd = position > 0.5;
        _touchSwipe = new EdgeSwipe(finger, _workspaceSwipe!, fromEnd);
        _heldTouches.Add(finger);

        // The edge the finger came from sets the direction lock, so a swipe that restarts keeps it.
        _swipeInitialDirection = (fromEnd ? 1 : -1) * (_swipe.TouchInvert ? -1 : 1);
        return View([]);
    }

    private Outcome TouchMove(TouchMoved moved)
    {
        if (!_digitizer.Touch.Enabled)
            return CancelTouch();

        moved = PlaceTouch(moved);
        if (_touchSwipe is not { } touch || touch.Finger != (moved.Device, moved.Id) || !ReferenceEquals(_workspaceSwipe, touch.Swipe))
            return View([]);

        var screen = MonitorById(touch.Swipe.Monitor)?.Bounds ?? _area;
        var vertical = SwipeSpan(touch.Swipe.Begin, screen).Vertical;
        var position = TouchPosition(moved.X, moved.Y, moved.Display, screen, vertical);
        var delta = touch.FromEnd ? 1 - position : -position;
        var outcome = StepWorkspaceSwipe(touch.Swipe, Math.Max(1, _swipe.Distance) * delta * (_swipe.TouchInvert ? -1 : 1));

        // workspace_swipe_forever starts the next swipe from the Workspace it switched to, which the finger carries on.
        if (_workspaceSwipe is { } next && !ReferenceEquals(next, touch.Swipe))
            _touchSwipe = touch with { Swipe = next };

        return outcome;
    }

    private Outcome TouchUp(TouchUp lifted)
    {
        var finger = (lifted.Device, lifted.Id);
        if (!_heldTouches.Remove(finger) || _touchSwipe is not { } touch || touch.Finger != finger)
            return View([]);

        _touchSwipe = null;
        if (!ReferenceEquals(_workspaceSwipe, touch.Swipe))
            return View([]);

        if (!lifted.Cancelled)
            return FinishWorkspaceSwipe(touch.Swipe);

        _workspaceSwipe = null;
        _swipeInitialDirection = 0;
        return ReturnSwipe(touch.Swipe);
    }

    // input:touchdevice:enabled false drops a new finger and puts a swipe that is already going back.
    private Outcome CancelTouch()
    {
        if (_touchSwipe is { } touch)
            return TouchUp(new TouchUp(touch.Finger.Device, touch.Finger.Id, Cancelled: true));

        _heldTouches.Clear();
        return View([]);
    }

    private TouchDown PlaceTouch(TouchDown down)
    {
        var (x, y, display) = MapTouch(down.X, down.Y, down.Display);
        return down with { X = x, Y = y, Display = display };
    }

    private TouchMoved PlaceTouch(TouchMoved moved)
    {
        var (x, y, display) = MapTouch(moved.X, moved.Y, moved.Display);
        return moved with { X = x, Y = y, Display = display };
    }

    // Transform turns the finger after Windows. An output replaces the display Windows mapped the screen to.
    private (double X, double Y, Rectangle? Display) MapTouch(double x, double y, Rectangle? display)
    {
        (x, y) = DigitizerMap.Calibrate(x, y, _digitizer.Touch.Transform, false);
        if (double.IsFinite(x))
            x = Math.Clamp(x, 0, 1);
        if (double.IsFinite(y))
            y = Math.Clamp(y, 0, 1);

        if (DigitizerMap.TouchTarget(_digitizer.Touch, DigitizerMonitors(out var focused), focused) is Rectangle area)
            display = area;
        return (x, y, display);
    }

    private MonitorId TouchMonitor(Rectangle? display)
    {
        if (display is { } area)
        {
            var x = area.X + (area.Width / 2);
            var y = area.Y + (area.Height / 2);
            foreach (var screen in _monitors)
            {
                var bounds = screen.Bounds;
                if (screen.Effective.Enabled && x >= bounds.X && x < bounds.X + bounds.Width && y >= bounds.Y && y < bounds.Y + bounds.Height)
                    return screen.Id;
            }
        }

        return FocusedId();
    }

    // Where the finger is across the Monitor, from 0 to 1, along the axis the swipe runs.
    private static double TouchPosition(double x, double y, Rectangle? display, Rectangle monitor, bool vertical)
    {
        if (display is not { } area)
            return vertical ? y : x;

        return vertical
            ? (area.Y + (y * area.Height) - monitor.Y) / monitor.Height
            : (area.X + (x * area.Width) - monitor.X) / monitor.Width;
    }

    private sealed record EdgeSwipe((long Device, int Id) Finger, WorkspaceSwipe Swipe, bool FromEnd);
}
