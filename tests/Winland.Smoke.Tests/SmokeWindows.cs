using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Smoke.Tests;

internal sealed class SmokeWindows : IDisposable
{
    public const string ClassName = "WinlandSmokeWindow";
    public const string FirstTitle = "WinlandSmoke 1";
    public const string SecondTitle = "WinlandSmoke 2";

    private const uint CloseMessage = 0x8000;
    private const uint FocusMessage = 0x8001;
    private const int ClassAlreadyExists = 1410;

    private static readonly WNDPROC Proc = WindowProc;

    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _thread;
    private uint _threadId;
    private Exception? _error;
    private bool _opened;
    private bool _closed;

    public SmokeWindows()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "WinlandSmokeWindows",
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public HWND First { get; private set; }

    public HWND Second { get; private set; }

    public void Open()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_opened)
            return;

        _opened = true;
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The test windows did not open.");

        if (_error is not null)
            throw new InvalidOperationException("The test windows did not open.", _error);
    }

    public void FocusFirst()
    {
        if (_threadId != 0)
            _ = PInvoke.PostThreadMessage(_threadId, FocusMessage, default, default);
    }

    public void Close()
    {
        if (_closed)
            return;

        _closed = true;
        if (_threadId != 0)
            _ = PInvoke.PostThreadMessage(_threadId, CloseMessage, default, default);

        if (_opened)
            _thread.Join(TimeSpan.FromSeconds(5));

        _ready.Dispose();
    }

    public void Dispose() => Close();

    private void Run()
    {
        try
        {
            _threadId = PInvoke.GetCurrentThreadId();
            var origin = WorkArea();
            Register();
            First = Create(FirstTitle, origin.left + 20, origin.top + 20);
            Second = Create(SecondTitle, origin.left + 20, origin.top + 20);
            _ = PInvoke.ShowWindow(First, SHOW_WINDOW_CMD.SW_SHOW);
            _ = PInvoke.ShowWindow(Second, SHOW_WINDOW_CMD.SW_SHOW);
            _ready.Set();
            Pump();
        }
        catch (Exception exception)
        {
            _error = exception;
            _ready.Set();
        }
    }

    private void Pump()
    {
        while (PInvoke.GetMessage(out var message, default, 0, 0) > 0)
        {
            if (message.message == FocusMessage)
            {
                _ = PInvoke.SetForegroundWindow(First);
                continue;
            }

            if (message.message == CloseMessage)
            {
                if (!First.IsNull)
                    _ = PInvoke.DestroyWindow(First);
                if (!Second.IsNull)
                    _ = PInvoke.DestroyWindow(Second);
                PInvoke.PostQuitMessage(0);
                continue;
            }

            PInvoke.TranslateMessage(in message);
            PInvoke.DispatchMessage(in message);
        }
    }

    private static unsafe void Register()
    {
        var module = PInvoke.GetModuleHandle((string?)null);
        fixed (char* name = ClassName)
        {
            var info = new WNDCLASSEXW
            {
                cbSize = IntPtr.Size == 8 ? 80u : 48u,
                lpfnWndProc = Proc,
                hInstance = (HINSTANCE)module.DangerousGetHandle(),
                hbrBackground = (HBRUSH)(nint)6,
                lpszClassName = name,
            };
            if (PInvoke.RegisterClassEx(in info) != 0)
                return;

            if (Marshal.GetLastPInvokeError() != ClassAlreadyExists)
                throw new InvalidOperationException($"The test window class was not registered ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private static unsafe HWND Create(string title, int x, int y)
    {
        var hwnd = PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_APPWINDOW,
            ClassName,
            title,
            WINDOW_STYLE.WS_OVERLAPPEDWINDOW | WINDOW_STYLE.WS_VISIBLE,
            x,
            y,
            200,
            150,
            HWND.Null,
            null,
            null,
            null);
        if (hwnd.IsNull)
            throw new InvalidOperationException($"The test window was not created ({Marshal.GetLastPInvokeError()}).");

        return hwnd;
    }

    private static unsafe RECT WorkArea()
    {
        var area = default(RECT);
        if (!PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETWORKAREA, 0, &area, 0))
            area = new RECT { left = 40, top = 40, right = 800, bottom = 600 };

        return area;
    }

    private static LRESULT WindowProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam) =>
        PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
}
