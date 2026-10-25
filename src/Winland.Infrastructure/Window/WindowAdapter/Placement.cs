using Windows.Win32.Foundation;

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

    // A Client that resizes itself just after it opens is reported once, so the core can put it back.
    private void ReportResize(HWND hwnd, WindowId window)
    {
        if (!_core.AwaitsFirstResize(window) || !TryVisible(window, out var visible))
            return;

        Apply(_core.Handle(new WindowResized(window, visible)));
    }
}
