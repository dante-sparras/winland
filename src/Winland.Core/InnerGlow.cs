namespace Winland;

/// <summary>
/// The glow Winland draws inside one Client's edge, as the pinned release draws it, in screen pixels: strongest at the
/// window's edge and falling to nothing <see cref="Range"/> pixels in at <see cref="Power"/>, inside corners of
/// <see cref="Rounding"/> pixels. <see cref="Alpha"/> is the window's opacity.
/// </summary>
public sealed record InnerGlow(BorderGradient Gradient, int Range, int Power, byte Alpha) : IGradientLayer
{
    /// <summary>The corner radius the glow is cut to, a pixel inside the window's own.</summary>
    public int Rounding { get; init; }

    /// <summary>The corner curve, as <c>decoration:rounding_power</c>.</summary>
    public float RoundingPower { get; init; } = 2;

    public bool Focused { get; init; }

    public Timing? Spin { get; init; }

    public bool SpinLoops { get; init; }

    public Timing? Fade { get; init; }

    public bool Equals(InnerGlow? other) =>
        other is not null
        && Range == other.Range
        && Power == other.Power
        && Alpha == other.Alpha
        && Rounding == other.Rounding
        && RoundingPower == other.RoundingPower
        && Focused == other.Focused
        && Spin == other.Spin
        && SpinLoops == other.SpinLoops
        && Fade == other.Fade
        && GradientMotion.Same(Gradient, other.Gradient);

    public override int GetHashCode() => HashCode.Combine(Range, Power, Alpha, Rounding, Gradient.AngleDegrees, Gradient.Colors.Count);
}

/// <summary>Places and paints an <see cref="InnerGlow"/> as premultiplied 32-bit pixels, a piece of its box at a time.</summary>
public static class GlowPainter
{
    /// <summary>
    /// Where the glow of a window whose visible frame is <paramref name="frame"/> goes, or null when it has no area. The box
    /// is the frame. The pieces are the four strips along its edges the glow reaches, top and bottom across the whole
    /// width, so the middle of the window is not covered.
    /// </summary>
    public static LayerLayout? Layout(Rectangle frame, InnerGlow glow)
    {
        ArgumentNullException.ThrowIfNull(glow);
        if (frame.Width <= 0 || frame.Height <= 0 || glow.Range <= 0)
            return null;

        // The smoothed distance to the edges is at most a third of the range short of the nearest edge, so nothing is
        // drawn further in than this.
        var reach = (int)Math.Ceiling(glow.Range * 4 / 3.0) + 1;
        var width = frame.Width;
        var height = frame.Height;
        var top = Math.Min(reach, height);
        var bottom = Math.Min(reach, height - top);
        var left = Math.Min(reach, width);
        var right = Math.Min(reach, width - left);
        var side = height - top - bottom;
        return new LayerLayout(
            frame,
            new Rectangle(0, 0, width, height),
            [
                new Rectangle(0, 0, width, top),
                new Rectangle(0, height - bottom, width, bottom),
                new Rectangle(0, top, left, side),
                new Rectangle(width - right, top, right, side),
            ]);
    }

    /// <summary>
    /// Paints the part of a <paramref name="boxWidth"/> by <paramref name="boxHeight"/> glow box that
    /// <paramref name="piece"/> covers into <paramref name="pixels"/>, row by row, as premultiplied BGRA.
    /// </summary>
    public static void Paint(
        Span<uint> pixels,
        InnerGlow glow,
        int boxWidth,
        int boxHeight,
        Rectangle frame,
        Rectangle piece,
        GradientMotion motion)
    {
        ArgumentNullException.ThrowIfNull(glow);
        if (piece.Width <= 0 || piece.Height <= 0 || pixels.Length < piece.Width * piece.Height)
            return;

        var sampler = new GradientSampler(glow.Gradient, motion, glow.Spin is not null);
        for (var row = 0; row < piece.Height; row++)
        {
            for (var column = 0; column < piece.Width; column++)
            {
                var px = piece.X + column + 0.5;
                var py = piece.Y + row + 0.5;
                var index = (row * piece.Width) + column;
                var strength = Strength(px, py, boxWidth, boxHeight, glow);
                pixels[index] = strength <= 0
                    ? 0
                    : BorderPainter.Premultiply(sampler.At(px / boxWidth, py / boxHeight), strength * glow.Alpha / 255.0);
            }
        }
    }

    /// <summary>
    /// How much of the glow's color shows at <paramref name="px"/>, <paramref name="py"/> in the window, from 0 to 1, as the
    /// pinned release's inner glow shader works it out: nothing outside the rounded window, and otherwise
    /// <c>(1 - distance / range) ^ render_power</c> of a distance to the edges smoothed round the corners.
    /// </summary>
    public static double Strength(double px, double py, int boxWidth, int boxHeight, InnerGlow glow)
    {
        ArgumentNullException.ThrowIfNull(glow);
        double range = glow.Range;
        if (range <= 0)
            return 0;

        var roundingPower = float.IsFinite(glow.RoundingPower) ? Math.Clamp(glow.RoundingPower, 1f, 10f) : 2f;
        double radius = Math.Max(0, glow.Rounding);
        var centerX = boxWidth / 2.0;
        var centerY = boxHeight / 2.0;
        var qx = Math.Abs(px - centerX) - (centerX - radius);
        var qy = Math.Abs(py - centerY) - (centerY - radius);
        var cx = Math.Max(qx, 0);
        var cy = Math.Max(qy, 0);
        var corner = cx > 0 || cy > 0 ? Math.Pow(Math.Pow(cx, roundingPower) + Math.Pow(cy, roundingPower), 1 / roundingPower) : 0;
        if (corner + Math.Min(Math.Max(qx, qy), 0) - radius > 0)
            return 0;

        var vertical = SmoothMin(py, boxHeight - py, range);
        var horizontal = SmoothMin(px, boxWidth - px, range);
        var distance = SmoothMin(vertical, horizontal, range);
        if (distance >= range)
            return 0;

        if (distance <= 0)
            return 1;

        return Math.Pow(1 - (distance / range), Math.Clamp(glow.Power, 1, 4));
    }

    // The shader's polynomial smooth minimum, which rounds the glow's contour where two edges meet.
    private static double SmoothMin(double a, double b, double k)
    {
        var h = Math.Max(k - Math.Abs(a - b), 0) / k;
        return Math.Min(a, b) - (h * h * h * k / 6);
    }
}
