using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

/// <summary>
/// Owns the windows that stand in for a growing window during its motion, on a thread of its own with a
/// message loop. Any thread may create one, move it and close it. The windows belong to this process, so
/// they go away with it.
/// </summary>
internal static class StandInHost
{
    private const string HostClass = "WinlandStandInHost";
    private const string StandInClass = "WinlandStandIn";
    private const int ClassAlreadyExists = 1410;
    private const uint CreateMessage = 0x8000 + 0x48;
    private const uint QuitMessage = 0x0012;
    private const uint MouseActivateMessage = 0x0021;
    private const int NoActivate = 3;

    private static readonly WNDPROC Proc = OnWindow;
    private static readonly Lock Gate = new();
    private static HWND _host;
    private static uint _thread;
    private static Thread? _worker;

    /// <summary>A new hidden stand-in window, or a null handle when none could be made.</summary>
    internal static HWND Create()
    {
        HWND host;
        lock (Gate)
        {
            if (_worker is null)
                Start();

            host = _host;
        }

        if (host.IsNull)
            return HWND.Null;

        return new HWND((nint)PInvoke.SendMessage(host, CreateMessage, default, default).Value);
    }

    /// <summary>Destroys a stand-in window on the thread that owns it.</summary>
    internal static void Close(HWND standIn)
    {
        if (!standIn.IsNull)
            _ = PInvoke.SendMessage(standIn, PInvoke.WM_CLOSE, default, default);
    }

    /// <summary>Ends the thread, and with it every stand-in window.</summary>
    internal static void Stop()
    {
        Thread? worker;
        uint thread;
        lock (Gate)
        {
            worker = _worker;
            thread = _thread;
            _worker = null;
            _thread = 0;
            _host = HWND.Null;
        }

        if (worker is null)
            return;

        if (thread != 0)
            _ = PInvoke.PostThreadMessage(thread, QuitMessage, default, default);

        _ = worker.Join(TimeSpan.FromSeconds(2));
    }

    // Waits until the thread has its host window, so the first stand-in is not lost.
    private static void Start()
    {
        using var ready = new ManualResetEventSlim();
        _worker = new Thread(() => Run(ready))
        {
            IsBackground = true,
            Name = "Winland stand-ins",
        };
        _worker.Start();

        // A thread that got ready with no host window failed, so the next stand-in starts one again.
        if (ready.Wait(TimeSpan.FromSeconds(2)) && _host.IsNull)
            _worker = null;
    }

    private static void Run(ManualResetEventSlim ready)
    {
        try
        {
            Register(HostClass);
            Register(StandInClass);
            var host = CreateHost();
            if (host.IsNull)
                return;

            _host = host;
            _thread = PInvoke.GetCurrentThreadId();
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return;
        }
        finally
        {
            ready.Set();
        }

        while (PInvoke.GetMessage(out var message, default, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(in message);
            PInvoke.DispatchMessage(in message);
        }
    }

    private static LRESULT OnWindow(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == CreateMessage)
            return new LRESULT((nint)CreateStandIn());

        if (message == MouseActivateMessage)
            return new LRESULT(NoActivate);

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static unsafe HWND CreateHost() =>
        PInvoke.CreateWindowEx(0, HostClass, "", 0, 0, 0, 0, 0, new HWND(-3), null, null, null);

    // No redirection surface, so all it shows is the thumbnail placed on it.
    private static unsafe HWND CreateStandIn() =>
        PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP,
            StandInClass,
            "",
            WINDOW_STYLE.WS_POPUP,
            0,
            0,
            0,
            0,
            HWND.Null,
            null,
            null,
            null);

    private static unsafe void Register(string name)
    {
        var module = PInvoke.GetModuleHandle((string?)null);
        var instance = (HINSTANCE)module.DangerousGetHandle();
        module.SetHandleAsInvalid();
        fixed (char* className = name)
        {
            var info = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Proc,
                hInstance = instance,
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(in info) != 0)
                return;

            if (Marshal.GetLastPInvokeError() != ClassAlreadyExists)
                throw new InvalidOperationException($"The stand-in window class was not registered ({Marshal.GetLastPInvokeError()}).");
        }
    }
}
