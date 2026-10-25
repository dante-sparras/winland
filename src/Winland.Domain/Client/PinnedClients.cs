namespace Winland.Domain;

/// <summary>
/// Which Clients are Pinned. Only a floating Client is, so the caller forgets a Client that becomes tiled or closes.
/// </summary>
internal sealed class PinnedClients
{
    private readonly HashSet<WindowId> _pinned = [];

    /// <summary>True for a Pinned Client.</summary>
    public bool IsPinned(WindowId window) => _pinned.Contains(window);

    public void Pin(WindowId window)
    {
        _pinned.Add(window);
    }

    public void Unpin(WindowId window) => _pinned.Remove(window);

    /// <summary>Forgets a Client that closed or became tiled.</summary>
    public void Forget(WindowId window) => _pinned.Remove(window);

    /// <summary>
    /// Pins a Client that opens when a rule pins it or its owner is Pinned, as long as it is floating.
    /// The owner's pin is read at open and never again.
    /// </summary>
    public void PinAtOpen(WindowId window, bool floating, bool ruled, WindowId? owner)
    {
        if (floating && (ruled || (owner is WindowId owning && IsPinned(owning))))
            Pin(window);
    }

    /// <summary>
    /// True for a Pinned Client that is shown on a Workspace that is not shown: it is not minimized and its
    /// Monitor shows a Workspace.
    /// </summary>
    public bool StaysVisible(WindowId window, bool minimized, bool monitorShowsAWorkspace) =>
        IsPinned(window) && !minimized && monitorShowsAWorkspace;
}
