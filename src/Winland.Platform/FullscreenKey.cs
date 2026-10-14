using System.Runtime.InteropServices;
using Winland.Core.Tiling;

namespace Winland.Platform;

/// <summary>
/// Holds F11 on a window that is fullscreen in its app on a tile behind a still picture of the monitor (ADR 0076).
/// The app puts its frame back before Winoma takes it off again, and lays its page out again after that. The picture covers both.
/// The keyboard hook asks <see cref="Take"/>, which only queues the work. This class's thread draws the picture, sends F11,
/// and lifts the picture once the window has settled.
/// <see cref="Ask"/> sends the same key for the tiled fullscreen chord, behind the same picture (ADR 0076).
/// The thread and its window are made by the first picture, and the device that copies the monitor is given back when none was taken for a while.
/// </summary>
internal sealed class FullscreenKey : IDisposable
{
    private const uint WmFreeze = 0x8003;
    private const string ClassName = "Winland.Freeze";
    private const int Popup = unchecked((int)0x80000000);
    private const int Layered = 0x00080000;
    private const int Transparent = 0x00000020;
    private const int TurnMs = 10;
    private const uint ReleaseAfterMs = 30_000;
    private const int ModifierWaitMs = 400;

    private readonly WindowPlacer _placer;
    private readonly DesktopCopy _copy = new();
    private readonly Thread _thread;
    private readonly Native.WndProc _proc = Native.DefWindowProc;
    private uint _threadId;
    private nint _picture;
    private long _target;
    private long _movedAt;
    private int _busy;
    private int _disposed;
    private int _started;
    private nint _firstWindow;
    private nuint _release;

    public FullscreenKey(WindowPlacer placer)
    {
        _placer = placer;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "winoma-fullscreen-key",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>
    /// A plain F11 went down. True takes it: the hook keeps it and its key-up from Windows, and this class sends F11 once the picture is up.
    /// F11 that makes a window fullscreen is left to the app, which then covers the monitor.
    /// Runs inside the keyboard hook, so it only reads and queues.
    /// </summary>
    public bool Take()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        // A second F11 while the picture is up would toggle the app back. It waits for this one.
        if (Volatile.Read(ref _busy) != 0)
        {
            return true;
        }

        var foreground = Native.GetForegroundWindow();
        var modified = KeyboardHook.SuperIsHeld || Held(Native.VkShift) || Held(Native.VkControl) || Held(Native.VkMenu);
        if (!FullscreenFreeze.Takes(foreground == 0 ? null : foreground, _placer.Tiles, WindowPlacer.HeldInApp(foreground), modified))
        {
            return false;
        }

        Volatile.Write(ref _busy, 1);
        Volatile.Write(ref _target, foreground);
        if (Post(foreground))
        {
            return true;
        }

        Volatile.Write(ref _busy, 0);
        return false;
    }

    /// <summary>
    /// Sends F11 to <paramref name="window"/>, which has the keyboard, behind the picture. The tiled fullscreen chord asks, on the desktop's thread.
    /// The picture lifts once the window has settled where the shell places it.
    /// </summary>
    /// <param name="window">The window that has the keyboard.</param>
    /// <returns>True when the key is sent, or an earlier one is still behind its picture. False when the caller must send it.</returns>
    public bool Ask(nint window)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        // A second key while the picture is up would toggle the app back.
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return true;
        }

        Volatile.Write(ref _target, window);
        if (Post(window))
        {
            return true;
        }

        Volatile.Write(ref _target, 0);
        Volatile.Write(ref _busy, 0);
        return false;
    }

    /// <summary>
    /// Sends F11 to the window that has the keyboard. A chord that asked for it can still have Shift, Control, or Alt down in Windows,
    /// and F11 with one of them is another key to the app, so the key waits a moment for them and then lets them go itself.
    /// </summary>
    internal static void PressKey()
    {
        var until = Environment.TickCount64 + ModifierWaitMs;
        while (ModifierHeld() && Environment.TickCount64 < until)
        {
            Thread.Sleep(TurnMs);
        }

        if (ModifierHeld())
        {
            KeyStroke.ReleaseModifiers();
        }

        KeyStroke.Send(KeyStroke.Down(Native.VkF11), KeyStroke.Up(Native.VkF11));
    }

    private static bool ModifierHeld() => Held(Native.VkShift) || Held(Native.VkControl) || Held(Native.VkMenu);

    /// <summary>A top-level window moved or resized. Runs on the window motion thread.</summary>
    public void Moved(nint hwnd)
    {
        if (hwnd != 0 && hwnd == Volatile.Read(ref _target))
        {
            Volatile.Write(ref _movedAt, Environment.TickCount64);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var thread = Volatile.Read(ref _threadId);
        if (thread != 0)
        {
            Native.PostThreadMessage(thread, Native.WmQuit, 0, 0);
        }
    }

    private static bool Held(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// Hands one picture to the thread. The first one starts the thread and rides in with it, since a thread that has not run has no queue to post to.
    /// This runs inside the keyboard hook too, so it does not wait for the thread to come up.
    /// </summary>
    /// <returns>False when the thread could not be given the work. The caller then does without the picture.</returns>
    private bool Post(nint window)
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _firstWindow = window;
            try
            {
                _thread.Start();
                return true;
            }
            catch (Exception ex) when (ex is OutOfMemoryException or ThreadStateException)
            {
                return false;
            }
        }

        var thread = Volatile.Read(ref _threadId);
        return thread != 0 && Native.PostThreadMessage(thread, WmFreeze, window, 0);
    }

    private void Run()
    {
        Native.PeekMessage(out _, 0, 0, 0, 0);
        _picture = OpenPicture();
        if (_picture == 0)
        {
            // The press that started this thread was kept from Windows. It is sent on without the picture.
            PressKey();
            Volatile.Write(ref _target, 0);
            Volatile.Write(ref _busy, 0);
            return;
        }

        Volatile.Write(ref _threadId, Native.GetCurrentThreadId());
        Handle(_firstWindow);
        while (Volatile.Read(ref _disposed) == 0 && Native.GetMessage(out var message, 0, 0, 0))
        {
            if (message.Message == WmFreeze && message.Hwnd == 0)
            {
                Handle(message.WParam);
                continue;
            }

            if (message.Message == Native.WmTimer && message.Hwnd == 0 && _release != 0 && message.WParam == (nint)_release)
            {
                // No picture for a while. The device and its driver state are worth more given back than kept warm.
                Native.KillTimer(0, _release);
                _release = 0;
                _copy.Release();
                continue;
            }

            Native.TranslateMessage(ref message);
            Native.DispatchMessage(ref message);
        }

        Native.DestroyWindow(_picture);
        _copy.Dispose();
    }

    private void Handle(nint window)
    {
        try
        {
            Freeze(window);
        }
        catch (Exception ex)
        {
            Log.Error("Tiling", "fullscreen freeze failed", ex);
            Native.ShowWindow(_picture, Native.SwHide);
            Shrink();
            PressKey();
        }
        finally
        {
            Volatile.Write(ref _target, 0);
            Volatile.Write(ref _busy, 0);
        }

        // A thread timer is named by the id Windows gives it. Setting that id again starts its wait over.
        _release = Native.SetTimer(0, _release, ReleaseAfterMs, 0);
    }

    private nint OpenPicture()
    {
        var instance = Native.GetModuleHandle(null);
        var cls = new Native.WndClass
        {
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            HInstance = instance,
            LpszClassName = ClassName,
        };
        if (Native.RegisterClass(ref cls) == 0 && Marshal.GetLastWin32Error() != Native.ErrorClassAlreadyExists)
        {
            Log.Warning("Tiling", "fullscreen freeze did not register; Win32 error " + Marshal.GetLastWin32Error());
            return 0;
        }

        var extended = Layered | Transparent | (int)Native.WsExTopmost | Native.WsExToolWindow | Native.WsExNoActivate;
        var window = Native.CreateWindowEx(extended, ClassName, null, Popup, 0, 0, 0, 0, 0, 0, instance, 0);
        if (window == 0)
        {
            Log.Warning("Tiling", "fullscreen freeze did not open; Win32 error " + Marshal.GetLastWin32Error());
        }

        return window;
    }

    private void Freeze(nint window)
    {
        var shown = Show(window, Monitor(window));
        PressKey();

        var sentAt = Environment.TickCount64;
        Volatile.Write(ref _movedAt, sentAt);
        if (!shown)
        {
            return;
        }

        while (true)
        {
            Thread.Sleep(TurnMs);
            var now = Environment.TickCount64;
            if (FullscreenFreeze.Lifts(now - sentAt, now - Volatile.Read(ref _movedAt), _placer.OnTile(window)))
            {
                break;
            }
        }

        Native.ShowWindow(_picture, Native.SwHide);
        Shrink();
    }

    /// <summary>
    /// Draws one pixel into the hidden picture. A layered window keeps the last surface it was given for as long as it exists,
    /// and the last one was the size of the monitor.
    /// </summary>
    private void Shrink()
    {
        var screen = Native.GetDC(0);
        if (screen == 0)
        {
            return;
        }

        var memory = Native.CreateCompatibleDC(screen);
        var bitmap = Native.CreateCompatibleBitmap(screen, 1, 1);
        var previous = Native.SelectObject(memory, bitmap);
        try
        {
            var destination = default(Native.Point);
            var size = new Native.Size { Cx = 1, Cy = 1 };
            var source = default(Native.Point);
            var blend = new Native.BlendFunction { SourceConstantAlpha = 255 };
            _ = Native.UpdateLayeredWindow(_picture, screen, ref destination, ref size, memory, ref source, 0, ref blend, Native.UlwOpaque);
        }
        finally
        {
            Native.SelectObject(memory, previous);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memory);
            Native.ReleaseDC(0, screen);
        }
    }

    private static TileRect Monitor(nint window)
    {
        var monitor = Native.MonitorFromWindow(window, 2);
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        if (monitor == 0 || !Native.GetMonitorInfo(monitor, ref info))
        {
            return default;
        }

        return new TileRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
    }

    /// <summary>Copies <paramref name="monitor"/> into the picture and puts it above every window, then waits for the compositor to draw it.</summary>
    /// <returns>False when the picture could not be drawn. F11 is still sent.</returns>
    private bool Show(nint window, TileRect monitor)
    {
        if (monitor.Width <= 0 || monitor.Height <= 0)
        {
            return false;
        }

        var screen = Native.GetDC(0);
        if (screen == 0)
        {
            return false;
        }

        var memory = Native.CreateCompatibleDC(screen);
        // The compositor's own copy, so a backdrop under a browser frame or the bar is not drawn lighter. GDI is the fallback.
        var bitmap = _copy.Copy(Native.MonitorFromWindow(window, 2), monitor.X, monitor.Y, monitor.Width, monitor.Height);
        var copied = bitmap != 0;
        if (!copied)
        {
            bitmap = Native.CreateCompatibleBitmap(screen, monitor.Width, monitor.Height);
        }

        var previous = Native.SelectObject(memory, bitmap);
        try
        {
            if (!copied && !Native.BitBlt(memory, 0, 0, monitor.Width, monitor.Height, screen, monitor.X, monitor.Y, Native.SrcCopy | Native.CaptureBlt))
            {
                return false;
            }

            var destination = new Native.Point { X = monitor.X, Y = monitor.Y };
            var size = new Native.Size { Cx = monitor.Width, Cy = monitor.Height };
            var source = default(Native.Point);
            var blend = new Native.BlendFunction { SourceConstantAlpha = 255 };
            if (!Native.UpdateLayeredWindow(_picture, screen, ref destination, ref size, memory, ref source, 0, ref blend, Native.UlwOpaque))
            {
                return false;
            }
        }
        finally
        {
            Native.SelectObject(memory, previous);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memory);
            Native.ReleaseDC(0, screen);
        }

        Native.SetWindowPos(_picture, Native.HwndTopMost, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | SwpShowWindow);
        // The picture must be on the glass before the app sees the key.
        _ = Native.DwmFlush();
        return true;
    }

    private const uint SwpShowWindow = 0x0040;
}
