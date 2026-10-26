using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

public sealed partial class WindowAdapter
{
    private void Restack(IReadOnlyList<ZOrderRequest> requests)
    {
        foreach (var request in requests)
        {
            // A program that has stopped answering is sent the request without waiting, and takes it when it wakes.
            if (_desktop.Hung(request.Window))
                _desktop.RaiseAsync(request.Window, request.Top);
            else
                _desktop.Raise(request.Window, request.Top);

            // A change of z-order moves nothing, so the window's border is told to follow it there.
            BorderHost.Follow(request.Window);
        }
    }

    // A click raises the window under it even when it is already active, and Windows reports no focus change for that.
    // When the active tile is above a floating Client that belongs over it, the tile goes back below the lowest such
    // Client. Windows does not let a process in the background raise a window over the active one, but it does let
    // it lower the active one. That reorders the windows too, and finds them in place.
    // Stryker disable once all : reads the z-order, which needs real windows.
    private void KeepFloatingAbove()
    {
        var active = PInvoke.GetForegroundWindow();
        if (active.IsNull)
            return;

        var floating = new HashSet<WindowId>(_core.FloatingAbove(new WindowId(active)));
        if (floating.Count == 0)
            return;

        var lowest = HWND.Null;
        for (var window = PInvoke.GetWindow(active, GET_WINDOW_CMD.GW_HWNDNEXT); !window.IsNull; window = PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_HWNDNEXT))
        {
            if (floating.Contains(new WindowId(window)))
                lowest = window;
        }

        if (lowest.IsNull)
            return;

        var flags = SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        if (_desktop.Hung(new WindowId(active)))
            flags |= SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS;

        _ = PInvoke.SetWindowPos(active, lowest, 0, 0, 0, 0, flags);
        BorderHost.Follow(new WindowId(active));
    }

    // A Client that resizes itself just after it opens is reported once, so the core can put it back.
    // Stryker disable once all : handles a window event, which needs a real window.
    private void ReportResize(HWND hwnd, WindowId window)
    {
        if (!_core.AwaitsFirstResize(window) || !TryVisible(window, out var visible))
            return;

        Apply(_core.Handle(new WindowResized(window, visible)));
    }
}
