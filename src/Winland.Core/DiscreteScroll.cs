namespace Winland;

/// <summary>
/// <c>input:emulate_discrete_scroll</c>: turns the wheel turns of a high resolution wheel, which Windows sends in
/// parts of a notch, into whole notches, as Winland does for a wheel's smooth events. 0 leaves every turn
/// alone. 1 emulates only the turns that are not whole notches. 2 emulates every turn, whole notches too.
/// </summary>
/// <remarks>
/// The first turn of a run, a run being the same axis and direction with under <see cref="IdleMs"/> between turns, sends
/// a notch at once so the wheel answers. After that a notch is sent for every <c>120 / factor</c> units the turns add
/// up to. The mouse hook passes a factor of 1, so a notch is 120 units. Used on the hook thread.
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

    /// <summary>Whether <paramref name="delta"/> is a turn <paramref name="mode"/> emulates: not whole notches for 1, any for 2.</summary>
    public static bool Emulates(int mode, int delta) =>
        delta != 0 && mode switch
        {
            >= 2 => true,
            1 => delta % TouchpadPointer.Notch != 0,
            _ => false,
        };

    /// <summary>
    /// The notches, as a wheel delta, to send instead of the turn <paramref name="delta"/>: a signed multiple of 120, or
    /// 0 to send none for now. Call it only for a turn <see cref="Emulates"/> accepted.
    /// </summary>
    public int Emulate(int delta, bool horizontal, double factor, long timeMs)
    {
        var sign = Math.Sign(delta);
        var notches = 0;
        var fresh = !_started || sign != _sign || horizontal != _horizontal || timeMs - _timeMs > IdleMs;
        if (fresh)
        {
            _sum = 0;
            notches = 1;
        }

        var interval = factor > 0 ? (int)Math.Round(TouchpadPointer.Notch / factor, MidpointRounding.ToEven) : TouchpadPointer.Notch;
        interval = Math.Max(interval, 1);
        while (_sum >= interval)
        {
            notches++;
            _sum -= interval;
        }

        _started = true;
        _sign = sign;
        _horizontal = horizontal;
        _timeMs = timeMs;
        _sum += Math.Abs(delta);
        return Math.Clamp(notches, 0, short.MaxValue / TouchpadPointer.Notch) * TouchpadPointer.Notch * sign;
    }
}
