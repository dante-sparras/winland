using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

public sealed partial class WindowAdapter
{
    // HWND_TOP raises a window above every window that is not topmost. HWND_BOTTOM lowers it below all of them.
    private static readonly HWND Top = new(0);
    private static readonly HWND Bottom = new(1);

    private static void Restack(IReadOnlyList<ZOrderRequest> requests)
    {
        foreach (var request in requests)
        {
            _ = PInvoke.SetWindowPos(
                new HWND((nint)request.Window.Value),
                request.Top ? Top : Bottom,
                0,
                0,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

            // A change of z-order moves nothing, so the window's border and shadow are told to follow it there.
            BorderHost.Follow(request.Window);
            ShadowHost.Follow(request.Window);
        }
    }

    // A Client that resizes itself just after it opens is reported once, so the core can put it back.
    private void ReportResize(HWND hwnd, WindowId window)
    {
        if (!_core.AwaitsFirstResize(window) || !TryVisible(hwnd, out var visible))
            return;

        Apply(_core.Handle(new WindowResized(window, visible)));
    }
}
