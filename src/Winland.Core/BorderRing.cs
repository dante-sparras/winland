namespace Winland;

/// <summary>
/// The border Winland draws around one Client, outside its frame, as the pinned release draws it: <see cref="Thickness"/>
/// pixels wide, in <see cref="Gradient"/>'s colors at its angle, with the inner corners at <see cref="Radius"/> pixels to
/// follow the window's own and the outer corners rounded to match, and <see cref="Alpha"/> times the colors' alpha.
/// </summary>
public sealed record BorderRing(BorderGradient Gradient, int Thickness, int Radius, float Power, byte Alpha)
{
    /// <summary>Whether the Client is focused, which starts a turn of the gradient.</summary>
    public bool Focused { get; init; }

    /// <summary>How one full turn of the gradient goes, from <c>animation:borderangle</c>, or null when it does not turn.</summary>
    public Timing? Spin { get; init; }

    /// <summary>The <c>loop</c> style: the gradient keeps turning.</summary>
    public bool SpinLoops { get; init; }

    /// <summary>Whether the gradient turns: it has a turn to make and more than one color to show it.</summary>
    public bool Spins => Spin is { Milliseconds: > 0 } && Gradient.Colors.Count > 1;

    public bool Equals(BorderRing? other) =>
        other is not null
        && Focused == other.Focused
        && Spin == other.Spin
        && SpinLoops == other.SpinLoops
        && Thickness == other.Thickness
        && Radius == other.Radius
        && Power == other.Power
        && Alpha == other.Alpha
        && Gradient.AngleDegrees == other.Gradient.AngleDegrees
        && Gradient.Colors.SequenceEqual(other.Gradient.Colors);

    public override int GetHashCode() => HashCode.Combine(Thickness, Radius, Power, Alpha, Gradient.AngleDegrees, Gradient.Colors.Count);

    /// <summary>
    /// The outer corner radius: the inner radius plus the thickness, less what a corner flatter than a circle gives back,
    /// as the pinned release's <c>outerRound</c>. A square window has square outer corners too, as the pinned release's
    /// shader rounds only around a rounded window.
    /// </summary>
    public double OuterRadius => Radius <= 0 ? 0 : Radius + Thickness - (Thickness * (Math.Sqrt(2) - 1) * Math.Max(2.0 - Power, 0));
}

/// <summary>Paints a <see cref="BorderRing"/> as premultiplied 32-bit pixels, a piece of its box at a time.</summary>
public static class BorderPainter
{
    private const int Steps = 256;

    /// <summary>
    /// Paints the part of a <paramref name="boxWidth"/> by <paramref name="boxHeight"/> ring box that
    /// <paramref name="piece"/> covers into <paramref name="pixels"/>, row by row, as premultiplied BGRA. Pixels off the
    /// ring are left clear.
    /// </summary>
    public static void Paint(Span<uint> pixels, BorderRing ring, int boxWidth, int boxHeight, Rectangle piece, double turnDegrees = 0)
    {
        ArgumentNullException.ThrowIfNull(ring);
        if (piece.Width <= 0 || piece.Height <= 0 || pixels.Length < piece.Width * piece.Height)
            return;

        var colors = Ramp(ring.Gradient);
        var angle = (ring.Gradient.AngleDegrees + turnDegrees) * Math.PI / 180;
        for (var row = 0; row < piece.Height; row++)
        {
            for (var column = 0; column < piece.Width; column++)
            {
                var x = piece.X + column;
                var y = piece.Y + row;
                var cover = Coverage(x, y, boxWidth, boxHeight, ring);
                var index = (row * piece.Width) + column;
                if (cover <= 0)
                {
                    pixels[index] = 0;
                    continue;
                }

                var color = colors[RampIndex((x + 0.5) / boxWidth, (y + 0.5) / boxHeight, angle, colors.Length)];
                pixels[index] = Premultiply(color, cover * ring.Alpha / 255.0);
            }
        }
    }

    /// <summary>
    /// How far a turning gradient has turned, in degrees, <paramref name="elapsed"/> milliseconds into a turn, as the pinned
    /// release adds its <c>borderangle</c> progress times a full turn.
    /// </summary>
    public static double Turn(Timing spin, double elapsed) =>
        spin.Milliseconds <= 0 ? 0 : spin.Curve.Ease(Math.Clamp(elapsed / spin.Milliseconds, 0, 1)) * 360;

    /// <summary>How much of the pixel at <paramref name="x"/>, <paramref name="y"/> of the ring box the ring covers, from 0 to 1.</summary>
    public static double Coverage(int x, int y, int boxWidth, int boxHeight, BorderRing ring)
    {
        ArgumentNullException.ThrowIfNull(ring);
        var px = Math.Abs(x + 0.5 - (boxWidth / 2.0));
        var py = Math.Abs(y + 0.5 - (boxHeight / 2.0));
        var outer = Inside(px, py, boxWidth / 2.0, boxHeight / 2.0, ring.OuterRadius, ring.Power);
        var inner = Inside(px, py, (boxWidth / 2.0) - ring.Thickness, (boxHeight / 2.0) - ring.Thickness, ring.Radius, ring.Power);
        return Math.Clamp(outer * (1 - inner), 0, 1);
    }

    /// <summary>The color of the gradient at the place a pixel's box-relative position puts it, as the pinned release's shader.</summary>
    public static BorderColor ColorAt(BorderGradient gradient, double nx, double ny)
    {
        ArgumentNullException.ThrowIfNull(gradient);
        var colors = Ramp(gradient);
        return colors[RampIndex(nx, ny, gradient.AngleDegrees * Math.PI / 180, colors.Length)];
    }

    // How much of a pixel at px, py from the middle lies inside a rounded rectangle of these half sizes.
    internal static double Inside(double px, double py, double halfWidth, double halfHeight, double radius, float power)
    {
        if (halfWidth <= 0 || halfHeight <= 0)
            return 0;

        var r = Math.Clamp(radius, 0, Math.Min(halfWidth, halfHeight));
        double distance;
        if (r > 0 && px > halfWidth - r && py > halfHeight - r)
        {
            var dx = px - (halfWidth - r);
            var dy = py - (halfHeight - r);
            var p = float.IsFinite(power) ? Math.Clamp(power, 1f, 10f) : 2f;
            distance = Math.Pow(Math.Pow(dx, p) + Math.Pow(dy, p), 1 / p) - r;
        }
        else
        {
            distance = Math.Max(px - halfWidth, py - halfHeight);
        }

        return Math.Clamp(0.5 - distance, 0, 1);
    }

    // The gradient line's position of a pixel, folded into one quadrant as the shader folds the angle.
    internal static int RampIndex(double nx, double ny, double angle, int steps)
    {
        angle %= 2 * Math.PI;
        if (angle < 0)
            angle += 2 * Math.PI;

        double final;
        if (angle > 4.71)
        {
            ny = 1 - ny;
            final = 6.28 - angle;
        }
        else if (angle > 3.14)
        {
            nx = 1 - nx;
            ny = 1 - ny;
            final = angle - 3.14;
        }
        else if (angle > 1.57)
        {
            nx = 1 - nx;
            final = 3.14 - angle;
        }
        else
        {
            final = angle;
        }

        var sine = Math.Sin(final);
        var progress = Math.Clamp((ny * sine) + (nx * (1 - sine)), 0, 1);
        return (int)Math.Round(progress * (steps - 1));
    }

    // The gradient's colors, blended in OkLab, at evenly spaced places along its line.
    internal static BorderColor[] Ramp(BorderGradient gradient)
    {
        var stops = gradient.Colors;
        if (stops.Count == 0)
            return [default];

        if (stops.Count == 1)
            return [stops[0]];

        var labs = stops.Select(OkLab.From).ToArray();
        var ramp = new BorderColor[Steps];
        for (var i = 0; i < Steps; i++)
        {
            var progress = i / (double)(Steps - 1) * (stops.Count - 1);
            var bottom = Math.Min((int)Math.Floor(progress), stops.Count - 2);
            var t = progress - bottom;
            var mixed = new double[4];
            for (var c = 0; c < 4; c++)
                mixed[c] = (labs[bottom][c] * (1 - t)) + (labs[bottom + 1][c] * t);

            ramp[i] = OkLab.To(mixed);
        }

        return ramp;
    }

    internal static uint Premultiply(BorderColor color, double alpha)
    {
        var a = Math.Clamp(color.A * alpha, 0, 255);
        var factor = a / 255.0;
        return ((uint)Math.Round(a) << 24)
            | ((uint)Math.Round(color.R * factor) << 16)
            | ((uint)Math.Round(color.G * factor) << 8)
            | (uint)Math.Round(color.B * factor);
    }
}
