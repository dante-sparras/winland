namespace Winland.Platform;

/// <summary>
/// Tags every window the calling thread creates, before <c>CreateWindow</c> returns.
/// A window watcher can see the window while it is still being created. The tag is already on it,
/// so the window is never seated as a tile. The shell's UI thread uses it, and so does the thread that draws group strips.
/// </summary>
public static class ShellWindows
{
    private static Native.HookProc? _proc;
    private static nint _hook;

    /// <summary>Installs the hook on the calling thread. A second call does nothing. Call it before the first window.</summary>
    public static void Install()
    {
        if (_hook != 0)
        {
            return;
        }

        _proc = OnCreate;
        _hook = Native.SetWindowsHookEx(Native.WhCbt, _proc, 0, Native.GetCurrentThreadId());
    }

    private static nint OnCreate(int code, nint wParam, nint lParam)
    {
        if (code == Native.HcbtCreateWnd && wParam != 0)
        {
            ShellProperty.Tag(wParam);
        }

        return Native.CallNextHookEx(_hook, code, wParam, lParam);
    }
}
