using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Winland.Windows;

internal static class ShellBars
{
    private const uint ChangeStateMessage = 0x0000000A;
    private const int AutoHide = 0x1;

    internal static bool Present() => !Primary().IsNull;

    // The shell can recreate the tray after sleep or a reconnect. Autohide has
    // to be set again or the work area keeps the strip the new bar reserved.
    internal static void Rehide()
    {
        var primary = Primary();
        if (!primary.IsNull)
        {
            var data = new APPBARDATA
            {
                cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
                hWnd = primary,
                lParam = new LPARAM(AutoHide),
            };
            SetAutoHide(ref data);
        }

        Taskbar.Conceal();
    }

    private static unsafe void SetAutoHide(ref APPBARDATA data)
    {
        fixed (APPBARDATA* pointer = &data)
            _ = PInvoke.SHAppBarMessage(ChangeStateMessage, pointer);
    }

    private static HWND Primary() => Find("Shell_TrayWnd");

    private static unsafe HWND Find(string className)
    {
        fixed (char* name = className)
            return PInvoke.FindWindow(name, null);
    }
}
