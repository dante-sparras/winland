namespace Winland;

public sealed partial class Core
{
    /// <summary>True when the window is a Client the core manages.</summary>
    public bool Manages(WindowId window)
    {
        lock (_gate)
            return WorkspaceContaining(window)?.ClientOf(window) is not null;
    }

    // The pinned release answers an application's maximize request with maximize mode 1, whichever window has focus.
    // Asking again, which is what the maximize button of a maximized window does, leaves it.
    private Outcome MaximizeRequest(WindowId window)
    {
        if (WorkspaceContaining(window) is not LiveWorkspace desk || desk.ClientOf(window) is not Client client)
            return View([]);

        var next = client.Fullscreen == FullscreenMode.Maximized ? FullscreenMode.None : FullscreenMode.Maximized;
        return CommitFullscreen(window, next, next, dispatcher: null, allowsElevation: false);
    }

    // A window that left the system's maximized state may restore a size of its own afterwards. It is put back
    // where its Layout holds it, with no motion: the window is already somewhere it should not be.
    private Outcome PutBack(WindowDrifted drifted)
    {
        if (WorkspaceContaining(drifted.Window) is not LiveWorkspace desk
            || desk.ClientOf(drifted.Window) is not { Minimized: false } client
            || !IsShown(desk.Id)
            || !desk.Layout.TryBounds(drifted.Window, out var slot))
            return View([]);

        var bounds = PlacedBounds(client, desk, slot);
        return bounds == drifted.Bounds ? View([]) : View([new Placement(client.Id, bounds)]);
    }
}
