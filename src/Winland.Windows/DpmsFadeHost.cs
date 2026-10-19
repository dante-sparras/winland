using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

/// <summary>
/// Fades the displays to black before they turn off and from black after they turn on, for <c>animation:fadeDpms</c>.
/// The black is a click-through layered window over every display, on a thread of its own, that <see cref="DpmsFader"/>
/// drives. It goes away with this process.
/// </summary>
// Stryker disable once all : drives a real window and the real display power; the fade is decided and tested in DpmsFader.
internal static class DpmsFadeHost
{
    private const string ClassName = "WinlandDpmsFade";
    private const int ClassAlreadyExists = 1410;
    private const uint WorkMessage = 0x8000 + 0x4A;
    private const uint QuitMessage = 0x0012;
    private const uint TimerMessage = 0x0113;
    private const uint MouseActivateMessage = 0x0021;
    private const int NoActivate = 3;
    private const nuint TimerId = 1;

    private static readonly WNDPROC Proc = OnWindow;
    private static readonly Lock Gate = new();
    private static readonly ConcurrentQueue<Func<DpmsFader, long, DpmsFadeStep>> Pending = new();
    private static readonly DpmsFader Fader = new();
    private static Thread? _worker;
    private static uint _thread;
    private static HWND _layer;
    private static uint _offInput;

    /// <summary>True once a fade was asked for, so later requests go through the fade too.</summary>
    internal static bool Started
    {
        get
        {
            lock (Gate)
                return _worker is not null;
        }
    }

    /// <summary>Turns the displays off or on, fading when the request says so.</summary>
    internal static void Request(DisplayPowerRequest request)
    {
        var fade = request.Fade;
        Post(request.On ? (fader, now) => fader.TurnOn(fade, now) : (fader, now) => fader.TurnOff(fade, now));
    }

    /// <summary>Windows says the displays show again.</summary>
    internal static void DisplaysShown()
    {
        if (Started)
            Post((fader, now) => fader.DisplaysShown(now));
    }

    /// <summary>Removes the black and ends the thread.</summary>
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
        }

        if (worker is null)
            return;

        if (thread != 0)
            _ = PInvoke.PostThreadMessage(thread, QuitMessage, default, default);

        _ = worker.Join(TimeSpan.FromSeconds(2));
    }

    private static void Post(Func<DpmsFader, long, DpmsFadeStep> work)
    {
        Pending.Enqueue(work);
        uint thread;
        lock (Gate)
        {
            if (_worker is null)
            {
                _worker = new Thread(Run) { IsBackground = true, Name = "Winland dpms fade" };
                _worker.Start();
                return;
            }

            thread = _thread;
        }

        if (thread != 0)
            _ = PInvoke.PostThreadMessage(thread, WorkMessage, default, default);
    }

    private static void Run()
    {
        _ = PInvoke.PeekMessage(out _, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        try
        {
            Register();
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return;
        }

        _layer = Create();
        lock (Gate)
        {
            if (!ReferenceEquals(_worker, Thread.CurrentThread))
                return;

            _thread = PInvoke.GetCurrentThreadId();
        }

        Work();
        while (PInvoke.GetMessage(out var message, default, 0, 0) > 0)
        {
            try
            {
                if (message.hwnd.IsNull && message.message == WorkMessage)
                {
                    Work();
                    continue;
                }

                if (message.message == TimerMessage && message.hwnd == _layer)
                {
                    Watch();
                    Show(Fader.Tick(Environment.TickCount64));
                    continue;
                }

                PInvoke.TranslateMessage(in message);
                PInvoke.DispatchMessage(in message);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Console.Error.WriteLine(exception.Message);
            }
        }

        if (!_layer.IsNull)
            _ = PInvoke.DestroyWindow(_layer);

        _layer = HWND.Null;
    }

    private static void Work()
    {
        while (Pending.TryDequeue(out var work))
            Show(work(Fader, Environment.TickCount64));
    }

    // Input after the turn-off woke the displays: Windows turns them on for any input.
    private static void Watch()
    {
        if (Fader.Off && LastInput() != _offInput)
            Show(Fader.InputSeen(Environment.TickCount64));
    }

    private static void Show(DpmsFadeStep step)
    {
        if (step.PowerOn)
            DisplayPowerAdapter.TryApplyToSystem(new DisplayPowerRequest("", true));

        if (step.Alpha is double alpha && !_layer.IsNull)
        {
            Cover(alpha);
        }
        else if (!_layer.IsNull)
        {
            _ = PInvoke.ShowWindow(_layer, SHOW_WINDOW_CMD.SW_HIDE);
        }

        if (step.PowerOff)
        {
            _offInput = LastInput();
            DisplayPowerAdapter.TryApplyToSystem(new DisplayPowerRequest("", false));
        }

        if (step.NextTick is int next)
            _ = PInvoke.SetTimer(_layer, TimerId, (uint)next, null);
        else
            _ = PInvoke.KillTimer(_layer, TimerId);
    }

    // Over every display and every other window, at the opacity asked for.
    private static void Cover(double alpha)
    {
        _ = PInvoke.SetLayeredWindowAttributes(_layer, new COLORREF(0), (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
        var x = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        var y = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
        var width = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN);
        var height = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN);
        _ = PInvoke.SetWindowPos(_layer, HWND.HWND_TOPMOST, x, y, width, height, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
    }

    private static uint LastInput()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return PInvoke.GetLastInputInfo(ref info) ? info.dwTime : 0;
    }

    private static LRESULT OnWindow(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam) =>
        message == MouseActivateMessage ? new LRESULT(NoActivate) : PInvoke.DefWindowProc(hwnd, message, wParam, lParam);

    private static unsafe void Register()
    {
        var module = PInvoke.GetModuleHandle((string?)null);
        var instance = (HINSTANCE)module.DangerousGetHandle();
        module.SetHandleAsInvalid();
        fixed (char* name = ClassName)
        {
            var info = new WNDCLASSEXW
            {
                cbSize = 80,
                lpfnWndProc = Proc,
                hInstance = instance,
                hbrBackground = new HBRUSH((nint)PInvoke.GetStockObject(GET_STOCK_OBJECT_FLAGS.BLACK_BRUSH).Value),
                lpszClassName = name,
            };
            if (PInvoke.RegisterClassEx(in info) != 0)
                return;

            if (Marshal.GetLastPInvokeError() != ClassAlreadyExists)
                throw new InvalidOperationException($"The fade window class was not registered ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private static unsafe HWND Create() =>
        PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE
                | WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_TOPMOST,
            ClassName,
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
}
