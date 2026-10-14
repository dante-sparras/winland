
namespace Winland.Core.Input;

/// <summary>What <see cref="WorkspaceWheel"/> makes of one move of the wheel.</summary>
/// <param name="Taken">True when the move is Winoma's, so no window scrolls by it.</param>
/// <param name="Cycle">The way to cycle the Workspaces, or null when the move is not Winoma's or its notch is not whole yet.</param>
public readonly record struct WheelTurn(bool Taken, WorkspaceCycle? Cycle);

/// <summary>
/// Super and the wheel cycle the Workspaces, as Omarchy's <c>Super + Scroll Wheel</c> does.
/// A notch away from the person is the next Workspace, and one toward the person the previous: Omarchy binds them that way round.
/// With another modifier held, or without Super, the wheel is the window's.
/// </summary>
public sealed class WorkspaceWheel
{
    private readonly WheelNotches _notches = new();

    /// <summary>Takes one move of the wheel.</summary>
    /// <param name="superHeld">True while a Super finger is held.</param>
    /// <param name="otherModifier">True while Shift, Ctrl, or Alt is held.</param>
    /// <param name="delta">The wheel's delta. Positive is away from the person, and 120 is one notch.</param>
    /// <param name="now">A millisecond clock.</param>
    /// <returns>Whether the move is Winoma's, and the cycle once a notch is whole. A move of more than one notch is one cycle.</returns>
    public WheelTurn Turn(bool superHeld, bool otherModifier, int delta, long now)
    {
        if (!superHeld || otherModifier)
        {
            return new WheelTurn(false, null);
        }

        var notches = _notches.Turn(delta, now);
        return new WheelTurn(true, notches > 0 ? WorkspaceCycle.Next : notches < 0 ? WorkspaceCycle.Previous : null);
    }
}
