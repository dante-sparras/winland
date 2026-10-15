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
    private string? _gestureButton;
    private string? _gestureDispatcher;
    private bool _disposed;

    public MouseHook(Core core, KeyboardHook keys)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(keys);
        _core = core;
        _keys = keys;
        _callback = OnMouse;
    }

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

        // A drag whose release was missed is over.
        _gestureButton = null;
        _gestureDispatcher = null;
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
            var position = new Point(info.pt.X, info.pt.Y);
            var message = (uint)wParam.Value;
            if (message == MoveMessage)
                return OnMove(code, wParam, lParam, position, info.time);

            if (TryButton(message, info.mouseData, out var button, out var up))
            {
                if (!up && (info.flags & 0x1) == 0)
                    InputClock.NoteClickOrKey();

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
        if (_gestureButton is not null && _gestureDispatcher is not null)
            _keys.PostGesture(_gestureDispatcher, position, PointerDragPhase.Move, _gestureButton);
        else
            _keys.PostMove(position, time);

        return Next(code, wParam, lParam);
    }

    private bool OnButton(string button, bool up, Point position)
    {
        if (up)
        {
            if (_gestureButton is not null && _gestureDispatcher is not null && _gestureButton.Equals(button, StringComparison.OrdinalIgnoreCase))
            {
                _keys.PostGesture(_gestureDispatcher, position, PointerDragPhase.End, button);
                _gestureButton = null;
                _gestureDispatcher = null;
            }

            var released = _core.Decide(new PointerButtonReleased(button) { Position = position }, _keys.Session);
            _keys.NoteTiming(released);
            _keys.Enqueue(released);
            return released.Consumed;
        }

        var decision = _core.Decide(new PointerButtonPressed(button) { Position = position }, _keys.Session);
        if (decision.Mask)
            KeyboardHook.Mask();

        _keys.NoteTiming(decision);
        if (TryDrag(decision, out var dispatcher))
        {
            _gestureButton = button;
            _gestureDispatcher = dispatcher;
            _keys.PostGesture(dispatcher, position, PointerDragPhase.Begin, button);
            foreach (var dispatch in decision.Dispatches)
            {
                if (dispatch.Dispatcher.Length == 0)
                    continue;
                if (dispatch.Dispatcher.Equals(dispatcher, StringComparison.OrdinalIgnoreCase) && dispatch.Arguments.Length == 0)
                    continue;

                _keys.PostDispatch(dispatch.Dispatcher, dispatch.Arguments);
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

    private static bool TryDrag(BindDecision decision, out string dispatcher)
    {
        dispatcher = "";
        if (decision.Dispatches.Count == 0)
        {
            if (string.IsNullOrEmpty(decision.Dispatcher) || !IsDrag(decision.Dispatcher, decision.Arguments))
                return false;

            dispatcher = decision.Dispatcher;
            return true;
        }

        foreach (var dispatch in decision.Dispatches)
        {
            if (!IsDrag(dispatch.Dispatcher, dispatch.Arguments))
                continue;

            dispatcher = dispatch.Dispatcher;
            return true;
        }

        return false;
    }

    private static bool IsDrag(string dispatcher, string arguments) =>
        (dispatcher.Equals("movewindow", StringComparison.OrdinalIgnoreCase) && arguments.Length == 0)
        || dispatcher.Equals("resizewindow", StringComparison.OrdinalIgnoreCase);

    private static bool TryButton(uint message, uint mouseData, out string button, out bool up)
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
                button = (mouseData & 0xFFFF) == 1 ? "mouse:275" : "mouse:276";
                up = message == ExtraUp;
                return true;
            default:
                button = "";
                return false;
        }
    }

    private static bool TryWheel(uint message, uint mouseData, out string key)
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

    private LRESULT Next(int code, WPARAM wParam, LPARAM lParam) =>
        PInvoke.CallNextHookEx(_hook, code, wParam, lParam);
}
