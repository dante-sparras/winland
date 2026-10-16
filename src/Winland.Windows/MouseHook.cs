using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
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
            if (message == MoveMessage)
                return OnMove(code, wParam, lParam, position, info.time);

            if (TryButton(message, info.mouseData, out var button, out var up))
            {
                if (!up && (info.flags & 0x1) == 0)
                    InputClock.NoteClickOrKey(info.time);

                return OnButton(button, up, position) ? new LRESULT(1) : Next(code, wParam, lParam);
            }

            if (TryWheel(message, info.mouseData, out var key))
                return OnWheel(key, position) ? new LRESULT(1) : Next(code, wParam, lParam);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }

        return Next(code, wParam, lParam);
    }

    private LRESULT OnMove(int code, WPARAM wParam, LPARAM lParam, Point position, uint time)
    {
        _ = _core.Decide(new PointerMoved(position), _keys.Session);
        SessionEffects.NoteMove();
        if (_drag.Current is MouseDrag.Active drag)
            _keys.PostGesture(drag.Dispatcher, drag.Arguments, position, PointerDragPhase.Move, drag.Button);
        else
            _keys.PostMove(position, time);

        return Next(code, wParam, lParam);
    }

    private bool OnButton(string button, bool up, Point position)
    {
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
            _drag.Begin(button, dispatcher, arguments);
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
