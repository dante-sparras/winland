namespace Winland.Core.Input;

/// <summary>
/// Hover-focus yields when a key other than Super goes down, and stays yielded until the pointer moves with every such key up.
/// Which keys are down is the chord ledger's account. This class keeps no count of its own, so a key-up the hook
/// was not called for cannot leave the pointer yielded.
/// </summary>
public sealed class PointerYield
{
    private bool _yielded;

    /// <summary>True while the pointer must not change the active window.</summary>
    public bool Yielded => _yielded;

    /// <summary>A key other than Super went down. An auto-repeat may report again.</summary>
    /// <returns>True the first time the pointer should stop choosing the active window.</returns>
    public bool Pressed()
    {
        if (_yielded)
        {
            return false;
        }

        _yielded = true;
        return true;
    }

    /// <summary>The pointer moved. True when hover-focus should come back.</summary>
    /// <param name="keyHeld">True while a key other than Super is down.</param>
    /// <returns>True when no such key is held and hover-focus was on hold.</returns>
    public bool Moved(bool keyHeld)
    {
        if (!_yielded || keyHeld)
        {
            return false;
        }

        _yielded = false;
        return true;
    }
}
