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
            Stack(focused);
            return requests;
        }

        foreach (var window in FloatingOver(focused))
            requests.Add(new ZOrderRequest(window, Top: true));

        return requests;
    }

    /// <summary>
    /// The floating Clients shown above <paramref name="window"/> while it is the active tile, bottom first. None when it
    /// floats or is not a Client. A click raises a window that is already active, which Windows does not report as a
    /// focus change, so the adapter asks this when the z-order changes.
    /// </summary>
    public IReadOnlyList<WindowId> FloatingAbove(WindowId window)
    {
        lock (_gate)
            return Find(window) is { Floating: false } ? [.. FloatingOver(window)] : [];
    }

    private IEnumerable<WindowId> FloatingOver(WindowId focused) =>
        _stacking.Where(window =>
            window != focused && Find(window) is { Floating: true, Minimized: false } && IsShownClient(window) && !_cover.IsBelow(window));
}
