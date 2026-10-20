namespace Winland;

/// <summary>
/// A window's shadow, border and glow painted into one picture, so that a window drawn with wobble or motion blur bends and
/// smears them with it, as the pinned release draws a window's decorations with the window. <see cref="Box"/> is where the
/// picture goes, from the window's top left corner, and <see cref="Pixels"/> are its rows of premultiplied BGRA.
/// </summary>
public sealed class DecorationCanvas
{
    private DecorationCanvas(Rectangle box, uint[] pixels)
    {
        Box = box;
        Pixels = pixels;
    }

    public Rectangle Box { get; }

    public uint[] Pixels { get; }

    /// <summary>
    /// Paints the decorations of a window <paramref name="width"/> by <paramref name="height"/> pixels: the shadow below,
    /// the glow over the window's edges and the border round it. Each is painted at full alpha, since the picture is shown
    /// with the window's. Null when there are none to paint or the window has no area.
    /// </summary>
    public static DecorationCanvas? Paint(int width, int height, BorderRing? ring, DropShadow? shadow, InnerGlow? glow)
    {
        if (width <= 0 || height <= 0)
            return null;

        var frame = new Rectangle(0, 0, width, height);
        var shadowLayout = shadow is null ? null : ShadowPainter.Layout(frame, shadow);
        var glowLayout = glow is null ? null : GlowPainter.Layout(frame, glow);
        var thickness = ring?.Thickness ?? 0;
        if (shadowLayout is null && glowLayout is null && thickness <= 0)
            return null;

        var box = frame;
        if (thickness > 0)
            box = Union(box, new Rectangle(-thickness, -thickness, width + (2 * thickness), height + (2 * thickness)));
        if (shadowLayout is not null)
            box = Union(box, shadowLayout.Box);

        var pixels = new uint[box.Width * box.Height];
        if (shadowLayout is not null)
        {
            var full = shadow! with { Alpha = 255 };
            foreach (var piece in shadowLayout.Pieces)
                Over(pixels, box, shadowLayout.Box, piece, span => ShadowPainter.Paint(span, full, shadowLayout.Box.Width, shadowLayout.Box.Height, shadowLayout.Frame, piece, GradientMotion.Still));
        }

        if (glowLayout is not null)
        {
            var full = glow! with { Alpha = 255 };
            foreach (var piece in glowLayout.Pieces)
                Over(pixels, box, glowLayout.Box, piece, span => GlowPainter.Paint(span, full, glowLayout.Box.Width, glowLayout.Box.Height, glowLayout.Frame, piece, GradientMotion.Still));
        }

        if (thickness > 0)
        {
            var ringBox = new Rectangle(-thickness, -thickness, width + (2 * thickness), height + (2 * thickness));
            var full = ring! with { Alpha = 255 };
            foreach (var piece in BorderPainter.Pieces(ringBox.Width, ringBox.Height, full))
                Over(pixels, box, ringBox, piece, span => BorderPainter.Paint(span, full, ringBox.Width, ringBox.Height, piece));
        }

        return new DecorationCanvas(box, pixels);
    }

    // Paints one piece of a layer whose box is at layerBox, and lays it over what the picture already holds.
    private static void Over(uint[] pixels, Rectangle box, Rectangle layerBox, Rectangle piece, Action<Span<uint>> paint)
    {
        if (piece.Width <= 0 || piece.Height <= 0)
            return;

        var painted = new uint[piece.Width * piece.Height];
        paint(painted);
        for (var row = 0; row < piece.Height; row++)
        {
            var y = layerBox.Y + piece.Y + row - box.Y;
            if (y < 0 || y >= box.Height)
                continue;

            for (var column = 0; column < piece.Width; column++)
            {
                var x = layerBox.X + piece.X + column - box.X;
                if (x < 0 || x >= box.Width)
                    continue;

                var top = painted[(row * piece.Width) + column];
                if (top == 0)
                    continue;

                var index = (y * box.Width) + x;
                pixels[index] = Blend(top, pixels[index]);
            }
        }
    }

    // Premultiplied "over": the top pixel and what shows of the one under it.
    internal static uint Blend(uint top, uint under)
    {
        var alpha = top >> 24;
        if (alpha == 255 || under == 0)
            return top;

        var keep = 255 - alpha;
        uint Channel(int shift) => Math.Min(255u, ((top >> shift) & 0xFF) + ((((under >> shift) & 0xFF) * keep) + 127) / 255);
        return (Channel(24) << 24) | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }

    private static Rectangle Union(Rectangle a, Rectangle b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new Rectangle(left, top, right - left, bottom - top);
    }
}
