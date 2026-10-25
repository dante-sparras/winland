namespace Winland.Domain;

/// <summary>
/// Turns the wheel turns of a high resolution wheel, which Windows sends in parts of a notch, into whole notches, as
/// Winland does for a wheel's smooth events. Turns that are already whole notches are left alone.
/// </summary>
/// <remarks>
/// The first turn of a run, a run being the same axis and direction with under <see cref="IdleMs"/> between turns, sends
/// a notch at once so the wheel answers. After that a notch is sent for every 120 units the turns add
/// up to. Used on the hook thread.
/// </remarks>
public sealed class DiscreteScroll
{
    /// <summary>The pause after which the sum starts again, which is libinput's own.</summary>
    public const long IdleMs = 500;

    private int _sum;
    private int _sign;
    private bool _horizontal;
    private long _timeMs = long.MinValue;
    private bool _started;

    /// <summary>Whether <paramref name="delta"/> is a turn that is not whole notches.</summary>
    public static bool Emulates(int delta) => delta % TouchpadPointer.Notch != 0;

    /// <summary>
    /// The notches, as a wheel delta, to send instead of the turn <paramref name="delta"/>: a signed multiple of 120, or
    /// 0 to send none for now. Call it only for a turn <see cref="Emulates"/> accepted.
    /// </summary>
    public int Emulate(int delta, bool horizontal, long timeMs)
    {
        var sign = Math.Sign(delta);
        var notches = 0;
        var fresh = !_started || sign != _sign || horizontal != _horizontal || timeMs - _timeMs > IdleMs;
        if (fresh)
        {
            _sum = 0;
            notches = 1;
        }

        while (_sum >= TouchpadPointer.Notch)
        {
            notches++;
            _sum -= TouchpadPointer.Notch;
        }

        _started = true;
        _sign = sign;
        _horizontal = horizontal;
        _timeMs = timeMs;
        _sum += Math.Abs(delta);
        return Math.Clamp(notches, 0, short.MaxValue / TouchpadPointer.Notch) * TouchpadPointer.Notch * sign;
    }
}
