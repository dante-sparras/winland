namespace Winland;

/// <summary>
/// The shadow Winland draws under one Client, as Winland draws it, in screen pixels: it reaches
/// <see cref="Range"/> pixels past the window and its <see cref="Border"/>, fades out at <see cref="Power"/>, follows the
/// border's outer corners of <see cref="Rounding"/> pixels, and is scaled about its middle by <see cref="Scale"/> and
/// moved by the offset. <see cref="Alpha"/> is the window's opacity.
/// </summary>
public sealed record DropShadow(BorderGradient Gradient, int Range, int Power, byte Alpha) : IGradientLayer
{
    /// <summary>The width of the border the shadow surrounds.</summary>
    public int Border { get; init; }

    /// <summary>The corner radius of the shadow's box, the border's outer corners.</summary>
    public int Rounding { get; init; }

    /// <summary>The corner curve, as <c>decoration:rounding_power</c>.</summary>
    public float RoundingPower { get; init; } = 2;

    /// <summary>The window's own corner radius, which the cut under the window follows.</summary>
    public int WindowRounding { get; init; }

    /// <summary>The scale of the shadow about its middle, from 0 to 1.</summary>
    public double Scale { get; init; } = 1;

    public double OffsetX { get; init; }

    public double OffsetY { get; init; }

    /// <summary>A flat shadow in the first color, with no falloff.</summary>
    public bool Sharp { get; init; }

    public bool Focused { get; init; }

    public Timing? Spin { get; init; }

    public bool SpinLoops { get; init; }

    public Timing? Fade { get; init; }

    public bool Equals(DropShadow? other) =>
        other is not null
        && Range == other.Range
        && Power == other.Power
        && Alpha == other.Alpha
        && Border == other.Border
        && Rounding == other.Rounding
        && RoundingPower == other.RoundingPower
        && WindowRounding == other.WindowRounding
        && Scale == other.Scale
        && OffsetX == other.OffsetX
        && OffsetY == other.OffsetY
        && Sharp == other.Sharp
        && Focused == other.Focused
        && Spin == other.Spin
        && SpinLoops == other.SpinLoops
        && Fade == other.Fade
        && GradientMotion.Same(Gradient, other.Gradient);

    public override int GetHashCode() => HashCode.Combine(Range, Power, Alpha, Border, Rounding, Scale, Gradient.AngleDegrees, Gradient.Colors.Count);
}

/// <summary>Places and paints a <see cref="DropShadow"/> as premultiplied 32-bit pixels, a piece of its box at a time.</summary>
public static class ShadowPainter
{
    /// <summary>
    /// Where the shadow of a window whose visible frame is <paramref name="frame"/> goes, or null when it has no area. The
    /// box is the window and its border grown by the range, scaled about its middle and moved by the offset, as the pinned
    /// release places it. The pieces leave out the part of the box under the window, inside its corners, which the window
    /// covers.
    /// </summary>
    public static LayerLayout? Layout(Rectangle frame, DropShadow shadow)
    {
        ArgumentNullException.ThrowIfNull(shadow);
        if (frame.Width <= 0 || frame.Height <= 0)
            return null;

        var grow = shadow.Border + shadow.Range;
        double width = frame.Width + (2 * grow);
        double height = frame.Height + (2 * grow);
        var scale = Math.Clamp(shadow.Scale, 0, 1);
        var x = frame.X - grow + ((width - (width * scale)) / 2) + shadow.OffsetX;
        var y = frame.Y - grow + ((height - (height * scale)) / 2) + shadow.OffsetY;
        var box = new Rectangle(
            (int)Math.Round(x),
            (int)Math.Round(y),
            (int)Math.Round(width * scale),
            (int)Math.Round(height * scale));
        if (box.Width < 1 || box.Height < 1)
            return null;

        var inBox = new Rectangle(frame.X - box.X, frame.Y - box.Y, frame.Width, frame.Height);
        var corner = Math.Max(0, shadow.WindowRounding);
        var hole = Intersect(
            new Rectangle(inBox.X + corner, inBox.Y + corner, inBox.Width - (2 * corner), inBox.Height - (2 * corner)),
            new Rectangle(0, 0, box.Width, box.Height));
        return new LayerLayout(box, inBox, Around(box.Width, box.Height, hole));
    }

    /// <summary>
    /// Paints the part of a <paramref name="boxWidth"/> by <paramref name="boxHeight"/> shadow box that
    /// <paramref name="piece"/> covers into <paramref name="pixels"/>, row by row, as premultiplied BGRA.
    /// <paramref name="frame"/> is the window's frame in the box, where a soft shadow is cut away.
    /// </summary>
    public static void Paint(
        Span<uint> pixels,
        DropShadow shadow,
        int boxWidth,
        int boxHeight,
        Rectangle frame,
        Rectangle piece,
        GradientMotion motion)
    {
        ArgumentNullException.ThrowIfNull(shadow);
        if (piece.Width <= 0 || piece.Height <= 0 || pixels.Length < piece.Width * piece.Height)
            return;

        var flat = shadow.Sharp ? FlatColor(shadow.Gradient, motion) : default;
        var sampler = shadow.Sharp ? null : new GradientSampler(shadow.Gradient, motion, shadow.Spin is not null);
        for (var row = 0; row < piece.Height; row++)
        {
            for (var column = 0; column < piece.Width; column++)
            {
                var px = piece.X + column + 0.5;
                var py = piece.Y + row + 0.5;
                var index = (row * piece.Width) + column;
                double cover;
                BorderColor color;
                if (sampler is null)
                {
                    cover = BorderPainter.Inside(
                        Math.Abs(px - (boxWidth / 2.0)),
                        Math.Abs(py - (boxHeight / 2.0)),
                        boxWidth / 2.0,
                        boxHeight / 2.0,
                        shadow.Rounding,
                        shadow.RoundingPower);
                    color = flat;
                }
                else
                {
                    cover = UnderWindow(px, py, frame, shadow.WindowRounding, shadow.RoundingPower)
                        ? 0
                        : Falloff(px, py, boxWidth, boxHeight, shadow);
                    color = cover > 0 ? sampler.At(px / boxWidth, py / boxHeight) : default;
                }

                pixels[index] = cover <= 0 ? 0 : BorderPainter.Premultiply(color, cover * shadow.Alpha / 255.0);
            }
        }
    }

    /// <summary>
    /// How much of the shadow's color shows at <paramref name="px"/>, <paramref name="py"/> in its box, from 0 to 1, as the
    /// shadow shader works it out: full inside, falling to nothing over the range at the edges and
    /// round the corners at the render power.
    /// </summary>
    public static double Falloff(double px, double py, int boxWidth, int boxHeight, DropShadow shadow)
    {
        ArgumentNullException.ThrowIfNull(shadow);
        double range = Math.Max(0, shadow.Range);
        var power = Math.Clamp(shadow.Power, 1, 4);
        var inset = range + Math.Max(0, shadow.Rounding);
        var radius = inset;
        var left = inset;
        var top = inset;
        var right = boxWidth - inset;
        var bottom = boxHeight - inset;
        var roundingPower = float.IsFinite(shadow.RoundingPower) ? Math.Clamp(shadow.RoundingPower, 1f, 10f) : 2f;

        double? cornerX = px < left ? left : px > right ? right : null;
        double? cornerY = py < top ? top : py > bottom ? bottom : null;
        if (cornerX is double cx && cornerY is double cy)
        {
            var distance = Math.Pow(Math.Pow(Math.Abs(px - cx), roundingPower) + Math.Pow(Math.Abs(py - cy), roundingPower), 1 / roundingPower);
            if (distance > radius)
                return 0;

            if (range > 0 && distance > radius - range)
                return Math.Pow((radius - distance) / range, power);

            return 1;
        }

        var smallest = Math.Min(Math.Min(py, boxHeight - py), Math.Min(px, boxWidth - px));
        if (smallest < 0)
            return 0;

        return range > 0 && smallest < range ? Math.Pow(smallest / range, power) : 1;
    }

    /// <summary>Whether a point lies under the window, inside its rounded corners, where Winland cuts the shadow away.</summary>
    public static bool UnderWindow(double px, double py, Rectangle frame, int radius, float roundingPower)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
            return false;

        double left = frame.X;
        double top = frame.Y;
        double right = frame.X + frame.Width;
        double bottom = frame.Y + frame.Height;
        if (px < left || px > right || py < top || py > bottom)
            return false;

        if (radius <= 0)
            return true;

        var r = Math.Min(radius, Math.Min(frame.Width / 2.0, frame.Height / 2.0));
        var innerLeft = left + r;
        var innerTop = top + r;
        var innerRight = right - r;
        var innerBottom = bottom - r;
        if (px >= innerLeft && px <= innerRight || py >= innerTop && py <= innerBottom)
            return true;

        var dx = px < innerLeft ? innerLeft - px : px - innerRight;
        var dy = py < innerTop ? innerTop - py : py - innerBottom;
        var p = float.IsFinite(roundingPower) ? Math.Clamp(roundingPower, 1f, 10f) : 2f;
        return Math.Pow(Math.Pow(dx, p) + Math.Pow(dy, p), 1 / p) <= r;
    }

    // The four strips of a box around a hole, top and bottom across the whole width. The whole box when there is no hole.
    internal static Rectangle[] Around(int width, int height, Rectangle hole)
    {
        if (hole.Width <= 0 || hole.Height <= 0)
            return [new Rectangle(0, 0, width, height), default, default, default];

        return
        [
            new Rectangle(0, 0, width, hole.Y),
            new Rectangle(0, hole.Y + hole.Height, width, height - (hole.Y + hole.Height)),
            new Rectangle(0, hole.Y, hole.X, hole.Height),
            new Rectangle(hole.X + hole.Width, hole.Y, width - (hole.X + hole.Width), hole.Height),
        ];
    }

    private static Rectangle Intersect(Rectangle a, Rectangle b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return right <= left || bottom <= top ? default : new Rectangle(left, top, right - left, bottom - top);
    }

    // A sharp shadow is its first color, the old first color blended toward it in plain RGB while it fades.
    private static BorderColor FlatColor(BorderGradient gradient, GradientMotion motion)
    {
        var to = gradient.First ?? default;
        if (motion.From is not BorderGradient from || motion.Progress >= 1)
            return to;

        var start = from.First ?? to;
        var t = Math.Clamp(motion.Progress, 0, 1);
        return new BorderColor(Lerp(start.A, to.A, t), Lerp(start.R, to.R, t), Lerp(start.G, to.G, t), Lerp(start.B, to.B, t));
    }

    private static byte Lerp(byte from, byte to, double t) => (byte)Math.Round(from + ((to - from) * t));
}
