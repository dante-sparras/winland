namespace Winland.Core.Input;

/// <summary>
/// Turns wheel deltas into whole notches. A mouse wheel sends a notch at a time. A touchpad sends a stream of small parts of one,
/// and each of those taken as a notch would run a volume or a brightness to its end in one swipe.
/// </summary>
public sealed class WheelNotches
{
    /// <summary>One notch of a mouse wheel.</summary>
    public const int Notch = 120;

    /// <summary>How long a part of a notch waits for the rest. A move after that belongs to another swipe.</summary>
    public static TimeSpan Swipe { get; } = TimeSpan.FromMilliseconds(400);

    private int _rest;
    private long? _turnedAt;

    /// <summary>Adds <paramref name="delta"/> and returns the whole notches it completes.</summary>
    /// <param name="delta">The wheel's delta. Positive is away from the person.</param>
    /// <param name="now">A millisecond clock.</param>
    /// <returns>Notches to act on: positive up, negative down, zero while a notch is still being made up.</returns>
    public int Turn(int delta, long now)
    {
        // A turn the other way starts again, so the first notch back is not owed what the last way left over.
        // So does a move long after the last, or a graze minutes later would finish the notch an old swipe began.
        var stale = _turnedAt is long at && now - at > (long)Swipe.TotalMilliseconds;
        if (stale || (delta != 0 && _rest != 0 && Math.Sign(delta) != Math.Sign(_rest)))
        {
            _rest = 0;
        }

        _turnedAt = now;

        _rest += delta;
        var notches = _rest / Notch;
        _rest -= notches * Notch;
        return notches;
    }
}
