using System.Runtime.InteropServices;

namespace Winland.Platform.Win32;

/// <summary>
/// Drops clicks on the minimize and maximize caption buttons.
/// Minimizing hides a window the taskbar can no longer restore, and maximizing collapses the caption.
/// The same hook reports that the pointer moved, so hover focus waits on movement instead of a timer,
/// and that the person used the mouse or touchpad, so the pointer can be drawn.
/// It also takes a left press that <see cref="CarryPressed"/> claims, and that press's release, so the window under it never sees the click,
/// and a move of the wheel that <see cref="WheelTurned"/> claims.
/// </summary>
internal sealed class CaptionGuard
{
    private const uint HitTimeout = 50;

    private readonly Native.HookProc _proc;
    private readonly Thread _thread;
    private nint _hook;
    private uint _threadId;
    private int _started;
    private int _disposed;
    private int _eatRelease;
    private int _carrying;
    private const int WmMouseMove = 0x0200;
    private const int WmMouseLast = 0x020E;

    /// <summary>The pointer moved. Raised inside the mouse hook on every move: the handler must only signal and return.</summary>
    public event Action? PointerMoved;

    /// <summary>
    /// The mouse or touchpad moved, pressed, or scrolled, with the event's tick time. Raised inside the mouse hook: the handler must only signal and return.
    /// Injected events count too. A precision touchpad reports its own events as injected.
    /// </summary>
    public event Action<uint>? PointerInput;

    /// <summary>
    /// The left button went down, with the pointer, and the press goes on to the window under it: no Carry took it, and it is on no size button.
    /// Raised inside the mouse hook: the handler must only signal and return.
    /// </summary>
    public event Action<int, int>? Pressed;

    /// <summary>
    /// Asked inside the mouse hook for each left press, with the pointer. True takes the press and its release from every window.
    /// It must only read and signal, and return at once.
    /// </summary>
    public Func<int, int, bool>? CarryPressed { get; set; }

    /// <summary>The release of a press <see cref="CarryPressed"/> took, with the pointer. Raised inside the mouse hook: only signal and return.</summary>
    public Action<int, int>? CarryReleased { get; set; }

    /// <summary>
    /// Asked inside the mouse hook for each move of the vertical wheel, with its delta: positive is away from the person, and 120 is a notch.
    /// True takes the move from every window. It must only read and signal, and return at once.
    /// </summary>
    public Func<int, bool>? WheelTurned { get; set; }

    public CaptionGuard()
    {
        _proc = Hook;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "winoma-caption",

            // The pointer of every program waits on this thread, and Windows drops a hook that answers late.
            Priority = ThreadPriority.Highest,
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>Starts ignoring caption size buttons. A second call does nothing.</summary>
    public void Install()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            return;
        }

        _thread.Start();
    }

    /// <summary>Stops ignoring caption size buttons.</summary>
    public void Stop()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var hook = Interlocked.Exchange(ref _hook, 0);
        if (hook != 0)
        {
            Native.UnhookWindowsHookEx(hook);
        }

        var thread = _threadId;
        if (thread != 0)
        {
            Native.PostThreadMessage(thread, Native.WmQuit, 0, 0);
        }
    }

    private void Run()
    {
        _threadId = Native.GetCurrentThreadId();
        Native.PeekMessage(out _, 0, 0, 0, 0);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _hook = Native.SetWindowsHookEx(Native.WhMouseLl, _proc, Native.GetModuleHandle(null), 0);
        var error = Marshal.GetLastWin32Error();
        if (Volatile.Read(ref _disposed) != 0)
        {
            if (_hook != 0)
            {
                Native.UnhookWindowsHookEx(_hook);
                _hook = 0;
            }

            return;
        }

        if (_hook == 0)
        {
            Log.Error("Shell", "caption hook did not install; Win32 error " + error);
            return;
        }

        Log.Information("Shell", "caption buttons ignored");
        while (Native.GetMessage(out var message, 0, 0, 0))
        {
            Native.TranslateMessage(ref message);
            Native.DispatchMessage(ref message);
        }
    }

    private nint Hook(int code, nint wParam, nint lParam)
    {
        if (code < 0 || Volatile.Read(ref _disposed) != 0)
        {
            return Native.CallNextHookEx(_hook, code, wParam, lParam);
        }

        var message = (int)wParam;
        if (message is >= WmMouseMove and <= WmMouseLast && PointerInput is { } input)
        {
            uint time;
            unsafe
            {
                time = ((Native.MsLlHook*)lParam)->Time;
            }

            input(time);
        }

        if (message == WmMouseMove)
        {
            PointerMoved?.Invoke();
            return Native.CallNextHookEx(_hook, code, wParam, lParam);
        }

        if (message == Native.WmMouseWheel && WheelTurned is { } wheel)
        {
            int delta;
            unsafe
            {
                delta = (short)(((Native.MsLlHook*)lParam)->MouseData >> 16);
            }

            return wheel(delta) ? 1 : Native.CallNextHookEx(_hook, code, wParam, lParam);
        }

        if (message == Native.WmLButtonUp && Interlocked.Exchange(ref _carrying, 0) == 1)
        {
            Native.Point released;
            unsafe
            {
                released = ((Native.MsLlHook*)lParam)->Pt;
            }

            CarryReleased?.Invoke(released.X, released.Y);
            return 1;
        }

        if (message == Native.WmLButtonUp && Interlocked.Exchange(ref _eatRelease, 0) == 1)
        {
            return 1;
        }

        if (message is not (Native.WmLButtonDown or Native.WmLButtonDblClk))
        {
            return Native.CallNextHookEx(_hook, code, wParam, lParam);
        }

        Native.MsLlHook info;
        unsafe
        {
            info = *(Native.MsLlHook*)lParam;
        }

        if (CarryPressed is { } carry && carry(info.Pt.X, info.Pt.Y))
        {
            Volatile.Write(ref _carrying, 1);
            return 1;
        }

        var hit = SizeHit(info.Pt);
        if (!CaptionStyle.IsSizeButton(hit))
        {
            Pressed?.Invoke(info.Pt.X, info.Pt.Y);
            return Native.CallNextHookEx(_hook, code, wParam, lParam);
        }

        // The log takes a lock. This callback is the pointer hook, so the line waits until the hook has returned.
        Volatile.Write(ref _eatRelease, 1);
        return 1;
    }

    private static int SizeHit(Native.Point point)
    {
        var window = Native.WindowFromPoint(point);
        if (window == 0)
        {
            return 0;
        }

        // A hit test waits on the window's own thread, and the pointer of every program waits on this hook meanwhile.
        // So a press that cannot be on a size button is let through unasked.
        var root = Native.GetAncestor(window, Native.GaRoot);
        if (root == 0)
        {
            root = window;
        }

        if (!MayHoldSizeButton(root, point))
        {
            return 0;
        }

        var hit = Hit(window, point);
        if (CaptionStyle.IsSizeButton(hit) || root == window)
        {
            return hit;
        }

        var rootHit = Hit(root, point);
        return CaptionStyle.IsSizeButton(rootHit) ? rootHit : hit;
    }

    private static bool MayHoldSizeButton(nint root, Native.Point point)
    {
        _ = Native.GetWindowThreadProcessId(root, out var pid);
        if (pid == Environment.ProcessId)
        {
            return false;
        }

        // The window's style is no guide. Winoma takes the size boxes off every tile, and a program that draws its own caption
        // still answers the hit test with the button it drew.
        if (!Native.GetWindowRect(root, out var rect))
        {
            return true;
        }

        var dpi = Native.GetDpiForWindow(root);
        var scale = dpi == 0 ? 96 : (int)dpi;
        // The title and the tab strip sit in the same band. Only the button strip on the right can be a size button,
        // and a press anywhere else must not wait on the window.
        return point.Y <= rect.Top + (CaptionBand * scale / 96)
            && point.X >= rect.Right - (ButtonStrip * scale / 96);
    }


    // Taller than any title bar, tab strip, or ribbon header a size button sits in, at 100% scale.
    private const int CaptionBand = 96;

    // Minimize, maximize, and close, plus a little room for a program that draws one extra box, at 100% scale.
    private const int ButtonStrip = 184;

    private static int Hit(nint window, Native.Point point)
    {
        var packed = (nint)((ushort)point.X | ((uint)(ushort)point.Y << 16));
        var sent = Native.SendMessageTimeout(
            window,
            Native.WmNcHitTest,
            0,
            packed,
            Native.SmtoAbortIfHung | Native.SmtoBlock,
            HitTimeout,
            out var hit);
        return sent == 0 ? 0 : (int)hit;
    }
}
