using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

internal sealed class SessionWindow : IDisposable
{
    internal const string ClassName = "WinlandSessionHost";

    private const int ClassAlreadyExists = 1410;
    private const uint CloseMessage = 0x0010;
    private const uint TimerMessage = 0x0113;
    private const nuint ShellTimer = 1;

    // Docking, waking, a resolution change or a remote session sends a burst of display changes. One reflow follows
    // the burst, a quarter of a second after the last of them.
    private const nuint DisplayTimer = 2;
    private const uint DisplaySettle = 250;

    // SPI_SETWORKAREA, which a WM_SETTINGCHANGE names when the work area changed alone, as the taskbar starting or
    // stopping to hide itself does.
    private const nuint WorkAreaSetting = 0x2F;
    private const uint ShellTimerMilliseconds = 250;

    // HSHELL_FLASH: HSHELL_REDRAW with the high bit set, sent when a window flashes for attention.
    private const nuint ShellFlash = 0x8006;

    private static readonly WNDPROC Proc = OnWindow;
    private static SessionWindow? Active;

    private readonly Action<SessionSignal> _onSignal;
    private readonly Action<WindowId>? _onFlash;
    private readonly Action? _onEnd;
    private readonly Action? _onAccent;
    private readonly Action? _onSettings;
    private readonly List<HPOWERNOTIFY> _power = [];
    private HWND _hwnd;
    private uint _shellMessage;
    private bool _shellHook;
    private bool _sessionNotifications;
    private bool _disposed;

    internal SessionWindow(Action<SessionSignal> onSignal, Action<WindowId>? onFlash = null, Action? onEnd = null, Action? onAccent = null, Action? onSettings = null)
    {
        ArgumentNullException.ThrowIfNull(onSignal);
        _onSignal = onSignal;
        _onFlash = onFlash;
        _onEnd = onEnd;
        _onAccent = onAccent;
        _onSettings = onSettings;
        Active = this;
        try
        {
            Register();
            _hwnd = Create();
            Listen();
        }
        catch
        {
            if (ReferenceEquals(Active, this))
                Active = null;

            Dispose();
            throw;
        }
    }

    internal void WatchForShell()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hwnd.IsNull)
            return;

        if (PInvoke.SetTimer(_hwnd, ShellTimer, ShellTimerMilliseconds, null) == 0)
            Console.Error.WriteLine($"The shell watch was not started ({Marshal.GetLastPInvokeError()}).");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (ReferenceEquals(Active, this))
            Active = null;

        if (_hwnd.IsNull)
            return;

        _ = PInvoke.KillTimer(_hwnd, ShellTimer);
        if (_shellHook)
        {
            _ = PInvoke.DeregisterShellHookWindow(_hwnd);
            _shellHook = false;
        }

        foreach (var notify in _power)
            _ = PInvoke.UnregisterPowerSettingNotification(notify);

        _power.Clear();
        if (_sessionNotifications)
        {
            _ = PInvoke.WTSUnRegisterSessionNotification(_hwnd);
            _sessionNotifications = false;
        }

        _ = PInvoke.DestroyWindow(_hwnd);
        _hwnd = default;
    }

    private static LRESULT OnWindow(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        var active = Active;
        if (active is null || active._disposed)
            return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);

        return active.Handle(hwnd, message, wParam, lParam);
    }

    private LRESULT Handle(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (message == CloseMessage)
            {
                PInvoke.PostQuitMessage(0);
                return default;
            }

            // The process can be ended as soon as this returns, so the cleanup runs here, on the loop thread.
            // The Restart Manager can end the session for this program alone, and the session goes on. With the
            // windows put back, Winland stops instead of running on with nothing hidden.
            if (SessionNotices.EndsSession(message, (nuint)wParam))
            {
                _onEnd?.Invoke();
                PInvoke.PostQuitMessage(0);
                return default;
            }

            if (message == TimerMessage && (nuint)wParam == ShellTimer)
            {
                if (ShellBars.Present())
                {
                    _ = PInvoke.KillTimer(_hwnd, ShellTimer);
                    _onSignal(SessionSignal.ShellReady);
                }

                return default;
            }

            if (message == TimerMessage && (nuint)wParam == DisplayTimer)
            {
                _ = PInvoke.KillTimer(_hwnd, DisplayTimer);
                _onSignal(SessionSignal.DisplayChanged);
                return default;
            }

            if (message == SessionNotices.DisplayMessage
                || (message == AccentNotices.SettingChange && (nuint)wParam == WorkAreaSetting))
            {
                _ = PInvoke.SetTimer(_hwnd, DisplayTimer, DisplaySettle, null);
                if (message == SessionNotices.DisplayMessage)
                    return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
            }

            if (message == SessionNotices.PowerMessage && (nuint)wParam == SessionNotices.PowerSettingChange)
            {
                if (TryReadDisplay(lParam, out var display))
                    _onSignal(display);

                return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
            }

            if (_shellHook && message == _shellMessage)
            {
                if ((nuint)wParam == ShellFlash && (nint)lParam != 0)
                    _onFlash?.Invoke(new WindowId(((nint)lParam).ToInt64()));

                return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
            }

            if (message == AccentNotices.SettingChange)
                _onSettings?.Invoke();

            if (AccentNotices.Changed(message, (nint)lParam))
                _onAccent?.Invoke();

            if (SessionNotices.TryFromMessage(message, (nuint)wParam, out var signal))
                _onSignal(signal);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static unsafe bool TryReadDisplay(LPARAM lParam, out SessionSignal signal)
    {
        signal = default;
        var address = (nint)lParam;
        if (address == 0)
            return false;

        var setting = *(Guid*)address;
        var length = *(int*)(address + 16);
        if (length < sizeof(int))
            return false;

        var value = *(int*)(address + 20);
        return SessionNotices.TryDisplayPower(setting, value, out signal);
    }

    private unsafe void Listen()
    {
        if (PInvoke.WTSRegisterSessionNotification(_hwnd, 0))
            _sessionNotifications = true;
        else
            Console.Error.WriteLine($"Session notifications were not registered ({Marshal.GetLastPInvokeError()}).");

        RegisterPower(SessionNotices.ConsoleDisplayState);
        RegisterPower(SessionNotices.MonitorPowerOn);
        if (_onFlash is not null)
            ListenForFlashes();
    }

    // The shell tells a registered top-level window when another window flashes for attention.
    private void ListenForFlashes()
    {
        _shellMessage = PInvoke.RegisterWindowMessage("SHELLHOOK");
        if (_shellMessage != 0 && PInvoke.RegisterShellHookWindow(_hwnd))
            _shellHook = true;
        else
            Console.Error.WriteLine($"Window flashes are not observed ({Marshal.GetLastPInvokeError()}).");
    }

    private unsafe void RegisterPower(Guid setting)
    {
        var notify = PInvoke.RegisterPowerSettingNotification(_hwnd, &setting, 0);
        if (notify.IsNull)
        {
            Console.Error.WriteLine($"Display power was not registered ({Marshal.GetLastPInvokeError()}).");
            return;
        }

        _power.Add(notify);
    }

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
                lpszClassName = name,
            };
            if (PInvoke.RegisterClassEx(in info) != 0)
                return;

            if (Marshal.GetLastPInvokeError() != ClassAlreadyExists)
                throw new InvalidOperationException($"The session window class was not registered ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private static unsafe HWND Create()
    {
        var hwnd = PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
            ClassName,
            "Winland",
            WINDOW_STYLE.WS_POPUP,
            0,
            0,
            0,
            0,
            HWND.Null,
            null,
            null,
            null);
        if (hwnd.IsNull)
            throw new InvalidOperationException($"The session window was not created ({Marshal.GetLastPInvokeError()}).");

        return hwnd;
    }
}
