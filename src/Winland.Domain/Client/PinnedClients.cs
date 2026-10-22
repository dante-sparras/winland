namespace Winland.Domain;

/// <summary>
/// Which Clients are Pinned and which hold a pin that a fullscreen or maximized mode took (a Held pin).
/// A Client is one or the other, never both. Only a floating Client has either, so the caller forgets a
/// Client that becomes tiled or closes.
/// </summary>
internal sealed class PinnedClients
{
    private readonly HashSet<WindowId> _pinned = [];
    private readonly HashSet<WindowId> _held = [];

    /// <summary>True for a Pinned Client. A Client with a Held pin is not.</summary>
    public bool IsPinned(WindowId window) => _pinned.Contains(window);

    /// <summary>True for a Client whose pin a fullscreen or maximized mode holds.</summary>
    public bool IsHeld(WindowId window) => _held.Contains(window);

    /// <summary>
    /// True for a Pinned Client or one with a Held pin. A fullscreen request has to leave a Client with
    /// a Held pin alone when binds:allow_pin_fullscreen is off, as it does a Pinned one.
    /// </summary>
    public bool IsPinnedOrHeld(WindowId window) => IsPinned(window) || IsHeld(window);

    public void Pin(WindowId window)
    {
        _held.Remove(window);
        _pinned.Add(window);
    }

    public void Unpin(WindowId window) => _pinned.Remove(window);

    /// <summary>Takes a Pinned Client's pin while it is fullscreen or maximized.</summary>
    public void Hold(WindowId window)
    {
        if (_pinned.Remove(window))
            _held.Add(window);
    }

    /// <summary>Gives back a Held pin when the mode ends.</summary>
    public void Release(WindowId window)
    {
        if (_held.Remove(window))
            _pinned.Add(window);
    }

    /// <summary>Forgets a Client that closed or became tiled, with the pin a mode holds for it.</summary>
    public void Forget(WindowId window)
    {
        _pinned.Remove(window);
        _held.Remove(window);
    }

    /// <summary>
    /// Pins a Client that opens when a rule pins it or its owner is Pinned, as long as it is floating.
    /// The owner's pin is read at open and never again.
    /// </summary>
    public void PinAtOpen(WindowId window, bool floating, bool ruled, WindowId? owner)
    {
        if (floating && (ruled || (owner is WindowId owning && IsPinned(owning))))
            Pin(window);
    }

    /// <summary>Gives a recovered Client the Held pin it had when Winland last ran.</summary>
    public void RestoreHeld(WindowId window)
    {
        Pin(window);
        Hold(window);
    }

    /// <summary>
    /// True for a Pinned Client that is shown on a Workspace that is not shown: it is not minimized and its
    /// Monitor shows a Workspace.
    /// </summary>
    public bool StaysVisible(WindowId window, bool minimized, bool monitorShowsAWorkspace) =>
        IsPinned(window) && !minimized && monitorShowsAWorkspace;
}
