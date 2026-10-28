using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

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

    private readonly WindowManager _core;
    private readonly KeyboardHook _keys;
    private readonly HOOKPROC _callback;
    private HHOOK _hook;
    // Buttons whose press a drag bind let through, with no window to drag, so their release goes through too.
    private readonly HashSet<string> _passed = new(StringComparer.OrdinalIgnoreCase);
    private readonly DiscreteScroll _discreteWheel = new();
    private readonly HashSet<string> _killed = new(StringComparer.Ordinal);

    // Buttons pressed on the ring around a tile, whose press and release go to the core to resize it.
    private readonly HashSet<string> _bordered = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public MouseHook(WindowManager core, KeyboardHook keys)
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
        var position = PInvoke.GetCursorPos(out var point) ? new Point(point.X, point.Y) : _keys.Session.Pointer;
        EndDrags(position);
    }

    internal void EndDrags(Point position)
    {
        if (_keys.Drag.Abandon() is BindDrag.Active drag)
            _keys.PostGesture(drag.Dispatcher, drag.Arguments, position, PointerDragPhase.End, drag.Button);

        foreach (var button in _bordered)
            _keys.PostInput(new PointerButtonReleased(button) { Position = position, BindsHandled = true });
        _bordered.Clear();
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

    private unsafe LRESULT OnMouse(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code < 0 || _disposed)
            return Next(code, wParam, lParam);

        try
        {
            _keys.Saw();
            var info = *(MSLLHOOKSTRUCT*)lParam.Value;
            if (info.dwExtraInfo == KeyboardHook.InjectedTag)
                return Next(code, wParam, lParam);

            var position = new Point(info.pt.X, info.pt.Y);
            var message = (uint)wParam.Value;
            var source = PointerSources.Of(info.flags, (ulong)info.dwExtraInfo);
            if (message == MoveMessage)
                return OnMove(code, wParam, lParam, position, info.time, source);

            if (TryButton(message, info.mouseData, out var button, out var up))
            {
                if (!up && source == PointerSource.Touch)
                    SessionEffects.NoteTouch();

                if (!up && (info.flags & 0x1) == 0)
                    InputClock.NoteClickOrKey(info.time, ClickedWindow(info.pt));

                return OnButton(button, up, position) ? new LRESULT(1) : Next(code, wParam, lParam);
            }

            if (TryWheel(message, info.mouseData, out var key))
                return OnWheel(key, position) || OffWindowWheel(message, info.mouseData, position) || DiscreteWheel(message, info.mouseData, source, info.time) ? new LRESULT(1) : Next(code, wParam, lParam);
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
        SessionEffects.NoteMove(position, source);
        if (_keys.Drag.Current is BindDrag.Active drag)
            _keys.PostGesture(drag.Dispatcher, drag.Arguments, position, PointerDragPhase.Move, drag.Button);
        else
            _keys.PostMove(position, time);

        return Next(code, wParam, lParam);
    }

    internal bool OnButton(string button, bool up, Point position)
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

        if (up && _bordered.Remove(button))
        {
            _keys.Enqueue(_core.Decide(new PointerButtonReleased(button) { Position = position }, _keys.Session));
            _keys.PostInput(new PointerButtonReleased(button) { Position = position, BindsHandled = true });
            return true;
        }

        if (up)
        {
            var passed = _passed.Remove(button);
            if (_keys.Drag.Release(button) is BindDrag.Active drag)
                _keys.PostGesture(drag.Dispatcher, drag.Arguments, position, PointerDragPhase.End, button);

            var released = _core.Decide(new PointerButtonReleased(button) { Position = position }, _keys.Session);
            _keys.Enqueue(released);
            return !passed && released.Consumed;
        }

        var decision = _core.Decide(new PointerButtonPressed(button) { Position = position }, _keys.Session);
        if (decision.Mask)
            KeyboardHook.Mask();

        if (TryDrag(decision, out var dispatcher, out var arguments))
        {
            // With no window under the pointer the press goes on to whatever is there, as Winland lets it
            // through, when the drag is all the bind does.
            if (_keys.Drag.Current is null && OnlyDrag(decision) && !_core.HasDragTarget(position))
            {
                _passed.Add(button);
                return false;
            }

            // A second drag button pressed during a drag starts nothing, so the held drag still ends on its own release.
            if (_keys.Drag.Begin(button, dispatcher, arguments))
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
        else if (!decision.Consumed && _keys.Drag.Current is null && _core.OnBorder(position))
        {
            // A press on a tile's ring resizes it. Its bind was already decided here.
            _bordered.Add(button);
            _keys.Enqueue(decision);
            _keys.PostInput(new PointerButtonPressed(button) { Position = position, BindsHandled = true });
            return true;
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

        _keys.Enqueue(decision);
        return decision.Consumed;
    }

    // A mouse wheel turn that is part of a notch is taken, and the whole notches it adds up
    // to are sent in its place. True when the turn was taken.
    private bool DiscreteWheel(uint message, uint mouseData, PointerSource source, uint time)
    {
        if (source != PointerSource.Mouse)
            return false;

        var delta = (short)(mouseData >> 16);
        if (delta == 0 || TouchpadFingers.Scrolls(Environment.TickCount64, delta) || !DiscreteScroll.Emulates(delta))
            return false;

        var horizontal = message == HorizontalWheel;
        var notches = _discreteWheel.Emulate(delta, horizontal, time);
        if (notches != 0)
            InputInjector.Post(Mouse(horizontal ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_HWHEEL : MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL, notches));
        return true;
    }

    // A wheel turn over the ring around a window, not inside it, is posted to the window as a wheel message at the nearest
    // point inside the window, as Windows apps cannot take the reference's coordinates outside their window. The lookup
    // never waits on the core: a busy core leaves the turn to Windows. True when the turn was taken.
    private bool OffWindowWheel(uint message, uint mouseData, Point position)
    {
        if (_core.OffWindowTarget(position) is not OffWindowScroll target)
            return false;

        var delta = (short)(mouseData >> 16);
        var keys = ((PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_SHIFT) & 0x8000) != 0 ? 0x4u : 0u)
            | ((PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_CONTROL) & 0x8000) != 0 ? 0x8u : 0u);
        var wParam = (nuint)(((uint)(ushort)delta << 16) | keys);
        var lParam = (nint)(int)(((uint)(ushort)target.Inside.Y << 16) | (ushort)target.Inside.X);
        _ = PInvoke.PostMessage(new HWND((nint)target.Window.Value), message, new WPARAM(wParam), new LPARAM(lParam));
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

    internal static bool OnlyDrag(BindDecision decision)
    {
        if (decision.Dispatches.Count == 0)
            return !string.IsNullOrEmpty(decision.Dispatcher) && IsDrag(decision.Dispatcher, decision.Arguments);

        foreach (var dispatch in decision.Dispatches)
        {
            if (dispatch.Dispatcher.Length > 0 && !IsDrag(dispatch.Dispatcher, dispatch.Arguments))
                return false;
        }

        return true;
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
