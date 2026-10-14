namespace Winland.Core.Shell;

/// <summary>
/// Decides when the keyboard leaves a hidden workspace.
/// The shell is not focused while Super is held. A window that opens here takes the keyboard instead.
/// </summary>
public sealed class KeyboardHandoff
{
    private bool _shellWaiting;
    private bool _anywhere;
    private long? _windowWaiting;

    /// <summary>
    /// True when the keyboard should move to the shell now.
    /// While <paramref name="superHeld"/> is true the request waits.
    /// </summary>
    /// <param name="superHeld">True while the chord ledger records a Super finger.</param>
    /// <returns>True when the caller should focus the shell.</returns>
    public bool ReleaseShell(bool superHeld)
    {
        _anywhere = false;
        if (superHeld)
        {
            _shellWaiting = true;
            return false;
        }

        _shellWaiting = false;
        return true;
    }

    /// <summary>
    /// True when the keyboard should leave a window that was just hidden.
    /// While <paramref name="superHeld"/> is true the request waits, and it completes even when the foreground is still on this workspace.
    /// Focusing the shell while Super is down leaves the later keys as Windows chords, because the hook swallows that key-up.
    /// </summary>
    /// <param name="superHeld">True while the chord ledger records a Super finger.</param>
    /// <returns>True when the caller should focus the shell.</returns>
    public bool ReleaseHidden(bool superHeld)
    {
        if (!superHeld)
        {
            _anywhere = false;
            _shellWaiting = false;
            return true;
        }

        _anywhere = true;
        _shellWaiting = true;
        return false;
    }

    /// <summary>
    /// The window that should become foreground, or null when the keyboard should stay.
    /// A claim made while Super is held waits for <see cref="TakeWaitingWindow"/>.
    /// </summary>
    /// <param name="window">The window that just opened on this workspace.</param>
    /// <param name="keyboardHere">True when the keyboard is already on a window of this workspace.</param>
    /// <param name="superHeld">True while the chord ledger records a Super finger.</param>
    /// <returns>The window to focus now, or null.</returns>
    public long? Claim(long? window, bool keyboardHere, bool superHeld)
    {
        if (keyboardHere || window is not long id)
        {
            _windowWaiting = null;
            return null;
        }

        if (superHeld)
        {
            _windowWaiting = id;
            return null;
        }

        _windowWaiting = null;
        return id;
    }

    /// <summary>The window whose claim waited for Super, once Super is up. Null while Super is held or nothing is waiting.</summary>
    /// <param name="superHeld">True while the chord ledger records a Super finger.</param>
    /// <returns>The window to focus, or null.</returns>
    public long? TakeWaitingWindow(bool superHeld)
    {
        if (superHeld || _windowWaiting is not long id)
        {
            return null;
        }

        _windowWaiting = null;
        return id;
    }

    /// <summary>True once, when Super has gone up and the shell release is still waiting.</summary>
    /// <param name="superHeld">True while the chord ledger records a Super finger.</param>
    /// <returns>True when the caller should focus the shell.</returns>
    public bool TakeWaitingShell(bool superHeld)
    {
        if (superHeld || !_shellWaiting || _anywhere)
        {
            return false;
        }

        _shellWaiting = false;
        return true;
    }

    /// <summary>
    /// True once, when Super has gone up and a hidden window's shell release is still waiting.
    /// Unlike <see cref="TakeWaitingShell"/>, the foreground may still be on this workspace.
    /// </summary>
    /// <param name="superHeld">True while the chord ledger records a Super finger.</param>
    /// <returns>True when the caller should focus the shell.</returns>
    public bool TakeWaitingHidden(bool superHeld)
    {
        if (superHeld || !_shellWaiting || !_anywhere)
        {
            return false;
        }

        _shellWaiting = false;
        _anywhere = false;
        return true;
    }

    /// <summary>Forgets a waiting shell release. A window on this workspace took the keyboard.</summary>
    public void DropShell()
    {
        _shellWaiting = false;
        _anywhere = false;
    }

    /// <summary>Forgets a waiting shell release and a waiting window.</summary>
    public void Clear()
    {
        _shellWaiting = false;
        _anywhere = false;
        _windowWaiting = null;
    }
}
