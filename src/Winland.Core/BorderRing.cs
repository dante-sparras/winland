namespace Winland.Core;

/// <summary>
/// The border Winland draws around one Client, outside its frame: <see cref="Thickness"/> pixels wide,
/// in one <see cref="Color"/>, with the inner corners at <see cref="Radius"/> pixels to follow the window's own
/// and the outer corners rounded to match, and <see cref="Alpha"/> times the color's alpha.
/// </summary>
public sealed record BorderRing(BorderColor Color, int Thickness, int Radius, float Power, byte Alpha)
{
    public bool Equals(BorderRing? other) =>
        other is not null
        && Color == other.Color
        && Thickness == other.Thickness
        && Radius == other.Radius
        && Power == other.Power
        && Alpha == other.Alpha;

    public override int GetHashCode() => HashCode.Combine(Color, Thickness, Radius, Power, Alpha);

    /// <summary>
    /// The outer corner radius: the inner radius plus the thickness, less what a corner flatter than a circle gives back.
    /// A square window has square outer corners too.
    /// </summary>
    public double OuterRadius => Radius <= 0 ? 0 : Radius + Thickness - (Thickness * (Math.Sqrt(2) - 1) * Math.Max(2.0 - Power, 0));
}

/// <summary>Paints a <see cref="BorderRing"/> as premultiplied 32-bit pixels, a piece of its box at a time.</summary>
public static class BorderPainter
{
    /// <summary>The four pieces of a ring box: the top and bottom strips hold the corners, and the sides fill between them.</summary>
    public static Rectangle[] Pieces(int boxWidth, int boxHeight, BorderRing ring)
    {
        ArgumentNullException.ThrowIfNull(ring);
        var corner = (int)Math.Ceiling(Math.Max(ring.OuterRadius, ring.Thickness + ring.Radius));
        corner = Math.Clamp(corner, ring.Thickness, Math.Max(ring.Thickness, boxHeight / 2));
        var side = Math.Max(0, boxHeight - (2 * corner));
        return
        [
            new Rectangle(0, 0, boxWidth, corner),
            new Rectangle(0, boxHeight - corner, boxWidth, corner),
            new Rectangle(0, corner, ring.Thickness, side),
            new Rectangle(boxWidth - ring.Thickness, corner, ring.Thickness, side),
        ];
    }

    /// <summary>
    /// Paints the part of a <paramref name="boxWidth"/> by <paramref name="boxHeight"/> ring box that
    /// <paramref name="piece"/> covers into <paramref name="pixels"/>, row by row, as premultiplied BGRA. Pixels off the
    /// ring are left clear. Every pixel of the ring is <see cref="BorderRing.Color"/>.
    /// </summary>
    public static void Paint(Span<uint> pixels, BorderRing ring, int boxWidth, int boxHeight, Rectangle piece)
    {
        ArgumentNullException.ThrowIfNull(ring);
        if (piece.Width <= 0 || piece.Height <= 0 || pixels.Length < piece.Width * piece.Height)
            return;

        for (var row = 0; row < piece.Height; row++)
        {
            for (var column = 0; column < piece.Width; column++)
            {
                var x = piece.X + column;
                var y = piece.Y + row;
                var cover = Coverage(x, y, boxWidth, boxHeight, ring);
                var index = (row * piece.Width) + column;
                pixels[index] = cover <= 0 ? 0 : Premultiply(ring.Color, cover * ring.Alpha / 255.0);
            }
        }
    }

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
