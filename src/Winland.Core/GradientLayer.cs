namespace Winland;

/// <summary>
/// A layer Winland draws beside a Client in a gradient that can fade to new colors when focus moves and turn, as the
/// pinned release's animated decoration gradient does for the shadow and the glow.
/// </summary>
public interface IGradientLayer
{
    /// <summary>The colors and angle the layer is drawn in now.</summary>
    BorderGradient Gradient { get; }

    /// <summary>Whether the Client is focused, which starts a turn of the gradient.</summary>
    bool Focused { get; }

    /// <summary>How one full turn of the gradient goes, or null when it does not turn.</summary>
    Timing? Spin { get; }

    /// <summary>The <c>loop</c> style: the gradient keeps turning.</summary>
    bool SpinLoops { get; }

    /// <summary>How a change of colors fades in, or null when the new colors show at once.</summary>
    Timing? Fade { get; }
}

/// <summary>
/// Where a gradient layer is in its animations: <paramref name="TurnDegrees"/> added to its angle, and the colors it
/// fades from with how far the fade has gone, from 0 to 1. A null <paramref name="From"/> is no fade.
/// </summary>
public readonly record struct GradientMotion(double TurnDegrees, BorderGradient? From, double Progress)
{
    public static GradientMotion Still { get; } = new(0, null, 1);

    /// <summary>How far a turn or a fade has gone <paramref name="elapsed"/> milliseconds in, from 0 to 1.</summary>
    public static double Eased(Timing timing, double elapsed) =>
        timing.Milliseconds <= 0 ? 1 : timing.Curve.Ease(Math.Clamp(elapsed / timing.Milliseconds, 0, 1));

    /// <summary>Whether two gradients have the same colors and angle.</summary>
    public static bool Same(BorderGradient? a, BorderGradient? b) =>
        ReferenceEquals(a, b) || a is not null && b is not null && a.AngleDegrees == b.AngleDegrees && a.Colors.SequenceEqual(b.Colors);
}

/// <summary>
/// Where a gradient layer goes on the screen: <see cref="Box"/> in screen pixels, the Client's frame inside it, and the
/// pieces of the box that are drawn, each relative to the box. A piece with no area is not drawn.
/// </summary>
public sealed record LayerLayout(Rectangle Box, Rectangle Frame, IReadOnlyList<Rectangle> Pieces);

/// <summary>
/// The color of a gradient layer at each place in its box, as the pinned release's shader picks it: the gradient at
/// its angle plus the turn, mixed in OkLab with the colors it fades from.
/// </summary>
internal sealed class GradientSampler
{
    private readonly BorderColor[] _ramp;
    private readonly double _angle;
    private readonly BorderColor[]? _fromRamp;
    private readonly double _fromAngle;
    private readonly double _progress;
    private readonly BorderColor? _flat;

    internal GradientSampler(BorderGradient gradient, GradientMotion motion, bool turning)
    {
        _ramp = BorderPainter.Ramp(gradient);
        _angle = (gradient.AngleDegrees + motion.TurnDegrees) * Math.PI / 180;
        if (motion.From is BorderGradient from && motion.Progress < 1)
        {
            _fromRamp = BorderPainter.Ramp(from);

            // A turning gradient fades its colors without also turning from the old angle.
            _fromAngle = turning ? _angle : from.AngleDegrees * Math.PI / 180;
            _progress = Math.Clamp(motion.Progress, 0, 1);
            if (_ramp.Length == 1 && _fromRamp.Length == 1)
                _flat = Mix(_fromRamp[0], _ramp[0], _progress);
        }
        else if (_ramp.Length == 1)
        {
            _flat = _ramp[0];
        }
    }

    internal BorderColor At(double nx, double ny)
    {
        if (_flat is BorderColor flat)
            return flat;

        var color = _ramp[BorderPainter.RampIndex(nx, ny, _angle, _ramp.Length)];
        if (_fromRamp is null)
            return color;

        var from = _fromRamp[BorderPainter.RampIndex(nx, ny, _fromAngle, _fromRamp.Length)];
        return Mix(from, color, _progress);
    }

    internal static BorderColor Mix(BorderColor from, BorderColor to, double t)
    {
        if (t <= 0)
            return from;

        if (t >= 1)
            return to;

        var a = OkLab.From(from);
        var b = OkLab.From(to);
        var mixed = new double[4];
        for (var c = 0; c < 4; c++)
            mixed[c] = (a[c] * (1 - t)) + (b[c] * t);

        return OkLab.To(mixed);
    }
}
