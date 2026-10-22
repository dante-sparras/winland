namespace Winland.Application;

/// <summary>Keeps a Workspace's floating Clients above its tiled ones in the z-order.</summary>
public sealed partial class WindowManager
{
    // The window that was active when the floating Clients were last put back above the tiles.
    private WindowId? _layeredFor;

    // Winland draws a Workspace's floating Clients above its tiled ones, whichever is focused. Windows brings
    // a window it focuses to the top, so when a tiled Client becomes the active window the floating Clients shown go back
    // above it, bottom first, in their stacking order. One a fullscreen Client keeps under itself stays there.
    private List<ZOrderRequest> FloatingAboveTiles()
    {
        var requests = new List<ZOrderRequest>();
        var active = ActiveWindow();
        if (active == _layeredFor)
            return requests;

        _layeredFor = active;
        if (active is not WindowId focused || Find(focused) is not Client client)
            return requests;

        // A floating Client that is focused comes to the top of the floating ones, as Windows and Winland
        // both raise it, so the order kept here matches the screen.
        if (client.Floating)
        {
            Stack(focused, top: true);
            return requests;
        }

        foreach (var window in _stacking)
        {
            if (window != focused && Find(window) is { Floating: true, Minimized: false } && IsShownClient(window) && !_belowFullscreen.Contains(window))
                requests.Add(new ZOrderRequest(window, Top: true));
        }

        return requests;
    }
}
