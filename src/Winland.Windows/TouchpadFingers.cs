namespace Winland.Windows;

/// <summary>
/// The fingers the precision touchpad last reported, which the touchpad reader writes and the mouse hook reads, so the
/// hook can tell the touchpad's cursor moves and wheel turns from a mouse's.
/// </summary>
internal static class TouchpadFingers
{
    private const long Never = long.MinValue / 4;

    private static int _count;
    private static long _reportMs = Never;
    private static long _twoFingersMs = Never;

    internal static void Note(int count, long now)
    {
        Volatile.Write(ref _count, count);
        Volatile.Write(ref _reportMs, now);
        if (count >= 2)
            Volatile.Write(ref _twoFingersMs, now);
    }

    /// <summary>Whether a cursor move made now is the touchpad's.</summary>
    internal static bool Moves(long now) => TouchpadPointer.Moves(Volatile.Read(ref _count), now - Volatile.Read(ref _reportMs));

    /// <summary>Whether a wheel turn of <paramref name="delta"/> made now is the touchpad's.</summary>
    internal static bool Scrolls(long now, int delta) =>
        TouchpadPointer.Scrolls(Volatile.Read(ref _count), now - Volatile.Read(ref _reportMs), now - Volatile.Read(ref _twoFingersMs), delta);
}
