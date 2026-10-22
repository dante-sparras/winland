namespace Winland.Domain;

/// <summary>
/// Turns wheel deltas into the ones <see cref="ScrollCurve"/> asks for. Speed is the delta over the time since the
/// last turn of the same axis and direction, and the first turn of a run, or one after <see cref="IdleMs"/>, is at speed 0.
/// What rounding leaves goes to the next turn. Used on the hook thread.
/// </summary>
public sealed class ScrollAccelerator
{
    /// <summary>A pause this long starts a new run of turns.</summary>
    public const long IdleMs = 500;

    private readonly Axis _vertical = new();
    private readonly Axis _horizontal = new();

    /// <summary>The wheel delta to send for <paramref name="delta"/> at <paramref name="timeMs"/>. 0 sends none.</summary>
    public int Accelerate(ScrollCurve curve, int delta, bool horizontal, long timeMs)
    {
        ArgumentNullException.ThrowIfNull(curve);
        var axis = horizontal ? _horizontal : _vertical;
        var sign = Math.Sign(delta);
        var speed = 0d;
        if (axis.Seen && axis.Sign == sign && timeMs - axis.TimeMs <= IdleMs)
            speed = Math.Abs(delta) / (double)TouchpadPointer.Notch * ScrollCurve.UnitsPerNotch / Math.Max(timeMs - axis.TimeMs, 1);
        else
            axis.Rest = 0;

        axis.Seen = true;
        axis.Sign = sign;
        axis.TimeMs = timeMs;
        var exact = (delta * curve.Factor(speed)) + axis.Rest;
        var whole = (int)Math.Clamp(Math.Round(exact, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue);
        axis.Rest = exact - whole;
        return whole;
    }

    private sealed class Axis
    {
        public bool Seen;
        public int Sign;
        public long TimeMs;
        public double Rest;
    }
}
