namespace Winland.Platform.Win32;

/// <summary>
/// Keeps the Windows taskbar hidden while Winoma is running.
/// Auto-hide slides it back on screen when the pointer reaches the edge, and Explorer also undoes a one-shot hide, so this reapplies the hide.
/// </summary>
internal sealed class TaskbarGuard
{
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WinEventSkipOwnProcess = 0x0002;

    private readonly Native.WinEventProc _callback;
    private nint _showHook;
    private nint _moveHook;
    private uint _hooked;
    private bool _suppressing;

    public TaskbarGuard() => _callback = OnEvent;

    /// <summary>Hides every taskbar and keeps it hidden, including when the pointer is at the screen edge.</summary>
    public void Suppress()
    {
        _suppressing = true;
        var primary = Native.FindWindow("Shell_TrayWnd", null);
        if (primary != 0)
        {
            _ = Native.GetWindowThreadProcessId(primary, out var pid);
            // The hooks name one process. Explorer starts again after it crashes or is updated, and the taskbar is then another process's.
            if (pid != 0 && (pid != _hooked || (_showHook == 0 && _moveHook == 0)))
            {
                Unhook();
                _showHook = Native.SetWinEventHook(EventObjectShow, EventObjectShow, 0, _callback, pid, 0, WinEventSkipOwnProcess);
                _moveHook = Native.SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, 0, _callback, pid, 0, WinEventSkipOwnProcess);
                _hooked = pid;
            }
        }

        ConcealAll();
    }

    /// <summary>
    /// Shows the Windows taskbar. Safe from any process in the session, so the desktop watcher can call it
    /// when the desktop is gone and is not coming back: no bar and no taskbar leaves nothing to click.
    /// </summary>
    public static void ShowAll() => Each(tray => Native.ShowWindow(tray, Native.SwShow));

    /// <summary>Stops holding the taskbar down and shows it again.</summary>
    public void Restore()
    {
        _suppressing = false;
        Unhook();
        Each(tray => Native.ShowWindow(tray, Native.SwShow));
    }

    private void Unhook()
    {
        if (_showHook != 0)
        {
            Native.UnhookWinEvent(_showHook);
            _showHook = 0;
        }

        if (_moveHook != 0)
        {
            Native.UnhookWinEvent(_moveHook);
            _moveHook = 0;
        }

        _hooked = 0;
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (!_suppressing || hwnd == 0 || idObject != 0 || idChild != 0)
        {
            return;
        }

        if (eventType is not (EventObjectShow or EventObjectLocationChange))
        {
            return;
        }

        if (!IsTray(hwnd) || !Native.IsWindowVisible(hwnd))
        {
            return;
        }

        Native.ShowWindow(hwnd, Native.SwHide);
    }

    private static void ConcealAll() => Each(tray =>
    {
        if (Native.IsWindowVisible(tray))
        {
            Native.ShowWindow(tray, Native.SwHide);
        }
    });

    private static void Each(Action<nint> action)
    {
        var primary = Native.FindWindow("Shell_TrayWnd", null);
        if (primary != 0)
        {
            action(primary);
        }

        var secondary = Native.FindWindowEx(0, 0, "Shell_SecondaryTrayWnd", null);
        while (secondary != 0)
        {
            action(secondary);
            secondary = Native.FindWindowEx(0, secondary, "Shell_SecondaryTrayWnd", null);
        }
    }

    private static unsafe bool IsTray(nint hwnd)
    {
        // Read on the stack. Explorer shows and moves a window object for every row it scrolls.
        const int Most = 32;
        var buffer = stackalloc char[Most];
        var length = Native.GetClassName(hwnd, buffer, Most);
        if (length <= 0)
        {
            return false;
        }

        var name = new ReadOnlySpan<char>(buffer, length);
        return name.SequenceEqual("Shell_TrayWnd") || name.SequenceEqual("Shell_SecondaryTrayWnd");
    }
}
