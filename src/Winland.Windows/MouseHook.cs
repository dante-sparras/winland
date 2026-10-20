using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

/// <summary>
/// Low-level mouse hook. The callback only matches and queues; a worker applies the drag.
/// It shares the keyboard hook's session so a modifier held on the keyboard completes a button chord.
/// </summary>
public sealed class MouseHook : IDisposable
{
    private const uint MoveMessage = 0x0200;
    private const uint LeftDown = 0x0201;
    private const uint LeftUp = 0x0202;
    private const uint RightDown = 0x0204;
    private const uint RightUp = 0x0205;
    private const uint MiddleDown = 0x0207;
    private const uint MiddleUp = 0x0208;
    private const uint Wheel = 0x020A;
    private const uint ExtraDown = 0x020B;
    private const uint ExtraUp = 0x020C;
    private const uint HorizontalWheel = 0x020E;

    private readonly Core _core;
    private readonly KeyboardHook _keys;
    private readonly HOOKPROC _callback;
    private HHOOK _hook;
    private readonly MouseDrag _drag = new();
    private readonly ButtonScroller _scroller = new();
    private readonly PointerRotation _rotation = new();
    private readonly TouchpadPointer _touchpadWheel = new();
    private readonly ScrollAccelerator _curveWheel = new();
    private readonly DiscreteScroll _discreteWheel = new();
    private readonly HashSet<string> _killed = new(StringComparer.Ordinal);
    private bool _disposed;

    public MouseHook(Core core, KeyboardHook keys)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(keys);
        _core = core;
        _keys = keys;
        _callback = OnMouse;
    }

    // Stryker disable all : from here to TryDrag, installing the low-level mouse hook and its callback, which need real mouse input.
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_hook.IsNull)
            return;

        _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _callback, default(HINSTANCE), 0);
        if (_hook.IsNull)
            throw new InvalidOperationException($"The mouse hook was not installed ({Marshal.GetLastPInvokeError()}).");

        InputClock.Watch();
    }

    internal void Reinstall()
    {
        if (_disposed)
            return;

        var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _callback, default(HINSTANCE), 0);
        if (hook.IsNull)
        {
            Console.Error.WriteLine($"The mouse hook was not reinstalled ({Marshal.GetLastPInvokeError()}).");
            return;
        }

        var old = _hook;
        _hook = hook;
        if (!old.IsNull)
            _ = PInvoke.UnhookWindowsHookEx(old);

        // A drag whose release was missed is over. The core is told, or the window would
        // keep following the cursor with no button held.
        if (_drag.Abandon() is MouseDrag.Active drag)
        {
            var position = PInvoke.GetCursorPos(out var point) ? new Point(point.X, point.Y) : _keys.Session.Pointer;
            _keys.PostGesture(drag.Dispatcher, drag.Arguments, position, PointerDragPhase.End, drag.Button);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_hook.IsNull)
            return;

        _ = PInvoke.UnhookWindowsHookEx(_hook);
        _hook = default;
    }

    private LRESULT OnMouse(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code < 0 || _disposed)
            return Next(code, wParam, lParam);

        try
        {
            _keys.Saw();
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (info.dwExtraInfo == KeyboardHook.InjectedTag)
                return Next(code, wParam, lParam);

            var position = new Point(info.pt.X, info.pt.Y);
            var message = (uint)wParam.Value;
            var source = PointerSources.Of(info.flags, (ulong)info.dwExtraInfo);
            if (ButtonScroll(message, info.mouseData, position))
                return new LRESULT(1);

            if (message == MoveMessage)
                return OnMove(code, wParam, lParam, position, info.time, source);

            if (TryButton(message, info.mouseData, out var button, out var up))
            {
                if (!up && source is PointerSource.Touch or PointerSource.Pen)
                    SessionEffects.NoteTouch(source == PointerSource.Pen);

                if (!up && (info.flags & 0x1) == 0)
                    InputClock.NoteClickOrKey(info.time, ClickedWindow(info.pt));

                return OnButton(button, up, position) ? new LRESULT(1) : Next(code, wParam, lParam);
            }

            if (TryWheel(message, info.mouseData, out var key))
                return OnWheel(key, position) || ScaleWheel(message, info.mouseData, source, info.time)? new LRESULT(1) : Next(code, wParam, lParam);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }

        return Next(code, wParam, lParam);
    }

    // The top-level window under a click, so the core can tell a window the user clicked from one another activated.
    private static unsafe nint ClickedWindow(System.Drawing.Point point)
    {
        var under = PInvoke.WindowFromPoint(point);
        return under.IsNull ? 0 : (nint)PInvoke.GetAncestor(under, GET_ANCESTOR_FLAGS.GA_ROOT).Value;
    }

    // input:scroll_method on_button_down: the scroll button is held back, and moving the pointer while it is down turns
    // the wheel instead. True when the event was taken.
    private bool ButtonScroll(uint message, uint mouseData, Point position)
    {
        _scroller.Configure(_core.ButtonScroll);
        if (message == MoveMessage)
        {
            if (!_scroller.Scrolling || !PInvoke.GetCursorPos(out var from))
                return false;

            var step = _scroller.Move(position.X - from.X, position.Y - from.Y);
            if (step.WheelY != 0)
                InputInjector.Post(Mouse(MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL, step.WheelY));
            if (step.WheelX != 0)
                InputInjector.Post(Mouse(MOUSE_EVENT_FLAGS.MOUSEEVENTF_HWHEEL, step.WheelX));

            return step.Swallow;
        }

        if (!TryButton(message, mouseData, out var button, out var up))
            return false;

        var result = up ? _scroller.Release(button) : _scroller.Press(button);
        if (result.Click && Buttons(button) is var (press, release, data))
            InputInjector.Post(Mouse(press, data), Mouse(release, data));

        return result.Swallow;
    }

    private static (MOUSE_EVENT_FLAGS Press, MOUSE_EVENT_FLAGS Release, int Data)? Buttons(string button) => button switch
    {
        "mouse:272" => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, 0),
        "mouse:273" => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP, 0),
        "mouse:274" => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP, 0),
        "mouse:275" => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP, 1),
        "mouse:276" => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP, 2),
        _ => null,
    };

    // Sent with Winland's tag, so this hook passes it on and the window under the pointer gets it.
    private static INPUT Mouse(MOUSE_EVENT_FLAGS flags, int data)
    {
        var input = new INPUT { type = INPUT_TYPE.INPUT_MOUSE };
        input.Anonymous.mi.dwFlags = flags;
        input.Anonymous.mi.mouseData = unchecked((uint)data);
        input.Anonymous.mi.dwExtraInfo = KeyboardHook.InjectedTag;
        return input;
    }

    private LRESULT OnMove(int code, WPARAM wParam, LPARAM lParam, Point position, uint time, PointerSource source)
    {
        var redirected = false;
        if (source != PointerSource.Mouse)
        {
            SessionEffects.NoteNonMouse();
        }
        else if (PInvoke.GetCursorPos(out var from) && SessionEffects.MouseMoved(new Point(from.X, from.Y), position) is Point back)
        {
            // cursor:warp_back_after_non_mouse_input: the move starts where the mouse left the cursor instead.
            _ = PInvoke.SetCursorPos(back.X, back.Y);
            position = PInvoke.GetCursorPos(out var landed) ? new Point(landed.X, landed.Y) : back;
            redirected = true;
        }

        // input:rotation: the mouse's move turns about where the cursor is. input:touchpad:flip_x and flip_y then turn
        // the touchpad's move around, as the pinned release flips the touchpad's motion after libinput rotated it.
        var touchpad = _core.Touchpad;
        var flip = source == PointerSource.Mouse && touchpad.Flips && TouchpadFingers.Moves(Environment.TickCount64);
        if (source == PointerSource.Mouse && (_core.PointerRotation != 0 || flip) && PInvoke.GetCursorPos(out var at))
        {
            var (dx, dy) = _rotation.Turn(position.X - at.X, position.Y - at.Y, _core.PointerRotation);
            if (flip)
            {
                var (flippedX, flippedY) = touchpad.Flip(dx, dy);
                (dx, dy) = ((int)flippedX, (int)flippedY);
            }

            var turned = new Point(at.X + dx, at.Y + dy);
            if (turned != position)
            {
                _ = PInvoke.SetCursorPos(turned.X, turned.Y);
                position = PInvoke.GetCursorPos(out var moved) ? new Point(moved.X, moved.Y) : turned;
                redirected = true;
            }
        }

        // cursor:hotspot_padding: the cursor stops short of the monitors' outer edges.
        if (SessionEffects.Padded(position) is var padded && padded != position)
        {
            _ = PInvoke.SetCursorPos(padded.X, padded.Y);
            position = padded;
            redirected = true;
        }

        _ = _core.Decide(new PointerMoved(position), _keys.Session);
        SessionEffects.NoteMove(position, source);
        if (_drag.Current is MouseDrag.Active drag)
            _keys.PostGesture(drag.Dispatcher, drag.Arguments, position, PointerDragPhase.Move, drag.Button);
        else
            _keys.PostMove(position, time);

        return redirected ? new LRESULT(1) : Next(code, wParam, lParam);
    }

    private bool OnButton(string button, bool up, Point position)
    {
        // kill: the armed click goes to the core, not to the window, and so does its release.
        if (up && _killed.Remove(button))
            return true;

        if (!up && _core.ClickKillArmed)
        {
            _killed.Add(button);
            _keys.PostInput(new ClickKillChosen(position));
            return true;
        }

        if (up)
        {
            if (_drag.Release(button) is MouseDrag.Active drag)
                _keys.PostGesture(drag.Dispatcher, drag.Arguments, position, PointerDragPhase.End, button);

            var released = _core.Decide(new PointerButtonReleased(button) { Position = position }, _keys.Session);
            _keys.NoteTiming(released);
            _keys.Enqueue(released);
            return released.Consumed;
        }

        var decision = _core.Decide(new PointerButtonPressed(button) { Position = position }, _keys.Session);
        if (decision.Mask)
            KeyboardHook.Mask();

        _keys.NoteTiming(decision);
        if (TryDrag(decision, out var dispatcher, out var arguments))
        {
            // A second drag button pressed during a drag starts nothing, so the held drag still ends on its own release.
            if (_drag.Begin(button, dispatcher, arguments))
                _keys.PostGesture(dispatcher, arguments, position, PointerDragPhase.Begin, button);

            foreach (var dispatch in decision.Dispatches)
            {
                if (dispatch.Dispatcher.Length == 0)
                    continue;
                if (dispatch.Dispatcher.Equals(dispatcher, StringComparison.OrdinalIgnoreCase) && dispatch.Arguments == arguments)
                    continue;

                _keys.PostDispatch(dispatch.Dispatcher, dispatch.Arguments, decision.Trigger);
            }
        }
        else
        {
            _keys.Enqueue(decision);
        }

        return decision.Consumed;
    }

    private bool OnWheel(string key, Point position)
    {
        _ = _core.Decide(new PointerMoved(position), _keys.Session);
        var decision = _core.Decide(new WheelTurned(key, Environment.TickCount64), _keys.Session);
        if (decision.Mask)
            KeyboardHook.Mask();

        _keys.NoteTiming(decision);
        _keys.Enqueue(decision);
        return decision.Consumed;
    }

    // input:touchpad:scroll_factor: the touchpad's wheel turn is taken and sent again multiplied. True when it was taken.
    private bool ScaleWheel(uint message, uint mouseData, PointerSource source, uint time)
    {
        if (source != PointerSource.Mouse)
            return false;

        var delta = (short)(mouseData >> 16);
        var touchpad = TouchpadFingers.Scrolls(Environment.TickCount64, delta);
        var scale = _core.Touchpad.WheelScale(touchpad);
        if (scale == 1)
            return !touchpad && (DiscreteWheel(message, delta, time) || (_core.ScrollCurve is ScrollCurve curve && CurveWheel(curve, message, delta, time)));

        var horizontal = message == HorizontalWheel;
        var scaled = _touchpadWheel.Scale(delta, scale, horizontal);
        if (scaled != 0)
            InputInjector.Post(Mouse(horizontal ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_HWHEEL : MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL, scaled));

        return true;
    }

    // input:scroll_points: a mouse wheel turn is taken and sent again at the factor its speed asks for. Always taken.
    private bool CurveWheel(ScrollCurve curve, uint message, int delta, uint time)
    {
        var horizontal = message == HorizontalWheel;
        var accelerated = _curveWheel.Accelerate(curve, delta, horizontal, time);
        if (accelerated != 0)
            InputInjector.Post(Mouse(horizontal ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_HWHEEL : MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL, accelerated));

        return true;
    }

    // input:emulate_discrete_scroll: a wheel turn that is part of a notch (or any turn, for force_all) is taken, and the
    // whole notches it adds up to are sent in its place. True when the turn was taken. A turn it takes skips scroll_points.
    private bool DiscreteWheel(uint message, int delta, uint time)
    {
        if (!DiscreteScroll.Emulates(_core.DiscreteScrollMode, delta))
            return false;

        var horizontal = message == HorizontalWheel;
        var factor = _core.Touchpad.WheelFactor ?? 1;
        var notches = _discreteWheel.Emulate(delta, horizontal, factor, time);
        if (notches != 0)
            InputInjector.Post(Mouse(horizontal ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_HWHEEL : MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL, notches));

        return true;
    }

    // Stryker restore all
    internal static bool TryDrag(BindDecision decision, out string dispatcher, out string arguments)
    {
        dispatcher = "";
        arguments = "";
        if (decision.Dispatches.Count == 0)
        {
            if (string.IsNullOrEmpty(decision.Dispatcher) || !IsDrag(decision.Dispatcher, decision.Arguments))
                return false;

            dispatcher = decision.Dispatcher;
            arguments = decision.Arguments;
            return true;
        }

        foreach (var dispatch in decision.Dispatches)
        {
            if (!IsDrag(dispatch.Dispatcher, dispatch.Arguments))
                continue;

            dispatcher = dispatch.Dispatcher;
            arguments = dispatch.Arguments;
            return true;
        }

        return false;
    }

    internal static bool IsDrag(string dispatcher, string arguments) =>
        (dispatcher.Equals("movewindow", StringComparison.OrdinalIgnoreCase) && arguments.Length == 0)
        || dispatcher.Equals("resizewindow", StringComparison.OrdinalIgnoreCase);

    internal static bool TryButton(uint message, uint mouseData, out string button, out bool up)
    {
        up = false;
        switch (message)
        {
            case LeftDown:
                button = "mouse:272";
                return true;
            case LeftUp:
                button = "mouse:272";
                up = true;
                return true;
            case RightDown:
                button = "mouse:273";
                return true;
            case RightUp:
                button = "mouse:273";
                up = true;
                return true;
            case MiddleDown:
                button = "mouse:274";
                return true;
            case MiddleUp:
                button = "mouse:274";
                up = true;
                return true;
            case ExtraDown:
            case ExtraUp:
                // The button is in the high word. The low word is reserved.
                button = (mouseData >> 16) == 1 ? "mouse:275" : "mouse:276";
                up = message == ExtraUp;
                return true;
            default:
                button = "";
                return false;
        }
    }

    internal static bool TryWheel(uint message, uint mouseData, out string key)
    {
        if (message is not (Wheel or HorizontalWheel))
        {
            key = "";
            return false;
        }

        var delta = (short)(mouseData >> 16);
        if (delta == 0)
        {
            key = "";
            return false;
        }

        key = message == Wheel
            ? delta > 0 ? "mouse_up" : "mouse_down"
            : delta > 0 ? "mouse_right" : "mouse_left";
        return true;
    }

    // Stryker disable once all : passes the event on to the next hook.
    private LRESULT Next(int code, WPARAM wParam, LPARAM lParam) =>
        PInvoke.CallNextHookEx(_hook, code, wParam, lParam);
}
