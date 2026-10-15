namespace Winland;

public sealed partial class Core
{
    /// <summary>True when the window is a Client the core manages.</summary>
    public bool Manages(WindowId window)
    {
        lock (_gate)
            return WorkspaceContaining(window)?.ClientOf(window) is not null;
    }

    // Hyprland answers an application's maximize request with maximize mode 1, whichever window has focus.
    // Asking again, which is what the maximize button of a maximized window does, leaves it.
    private Outcome MaximizeRequest(WindowId window)
    {
        if (WorkspaceContaining(window) is not LiveWorkspace desk || desk.ClientOf(window) is not Client client)
            return View([]);

        var next = client.Fullscreen == FullscreenMode.Maximized ? FullscreenMode.None : FullscreenMode.Maximized;
        return CommitFullscreen(window, next, next, dispatcher: null, allowsElevation: false);
    }
}
