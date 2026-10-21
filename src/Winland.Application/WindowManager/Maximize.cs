namespace Winland.Application;

public sealed partial class WindowManager
{
    /// <summary>True when the window is a Client the core manages.</summary>
    public bool Manages(WindowId window)
    {
        lock (_gate)
            return WorkspaceContaining(window)?.ClientOf(window) is not null;
    }

    // Windows that showed maximized and have not yet sent the maximize request that comes of it.
    private readonly HashSet<WindowId> _shownMaximized = [];

    private Outcome AppearMaybeMaximized(WindowAppeared appeared)
    {
        var outcome = Appear(appeared);
        if (appeared.ShownMaximized && WorkspaceContaining(appeared.Window)?.ClientOf(appeared.Window) is not null)
            _shownMaximized.Add(appeared.Window);

        return outcome;
    }

    // Winland answers an application's maximize request with maximize mode 1, whichever window has focus.
    // Asking again, which is what the maximize button of a maximized window does, leaves it.
    private Outcome MaximizeRequest(WindowId window)
    {
        if (WorkspaceContaining(window) is not LiveWorkspace desk || desk.ClientOf(window) is not Client client)
            return View([]);

        // The request of a window that showed maximized was made before it mapped, which Winland ignores.
        // The system's maximized state is already undone, so the window goes back where its Layout holds it.
        if (_shownMaximized.Remove(window))
        {
            return IsShown(desk.Id) && !client.Minimized && desk.Layout.TryBounds(window, out var slot)
                ? View([new Placement(client.Id, PlacedBounds(client, desk, slot))])
                : View([]);
        }

        var next = client.Fullscreen == FullscreenMode.Maximized ? FullscreenMode.None : FullscreenMode.Maximized;
        var synced = SyncsFullscreen(window, desk);
        var clientMode = synced ? next : client.FullscreenClient;
        return CommitFullscreen(window, next, clientMode, dispatcher: null, synced);
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
