namespace Winland;

/// <summary>The corners of a rectangle that are round.</summary>
[Flags]
public enum Corners
{
    None = 0,
    TopLeft = 1,
    TopRight = 2,
    BottomLeft = 4,
    BottomRight = 8,
    All = TopLeft | TopRight | BottomLeft | BottomRight,
}

/// <summary>The shapes and gradient of the tab bar, as the pinned release draws them.</summary>
public static class BarShapes
{
    private const int Samples = 4;

    /// <summary>
    /// The round corners of tab <paramref name="index"/> of <paramref name="count"/>. Every corner is round, unless only the
    /// edges are and there are several tabs: then the first tab's outer corners and the last tab's are, and none between.
    /// </summary>
    public static Corners Rounded(int index, int count, int radius, bool onlyEdges)
    {
        if (radius <= 0)
            return Corners.None;

        if (!onlyEdges || count <= 1)
            return Corners.All;

        if (index == 0)
            return Corners.TopLeft | Corners.BottomLeft;

        return index == count - 1 ? Corners.TopRight | Corners.BottomRight : Corners.None;
    }

    /// <summary>
    /// How much of the pixel at <paramref name="x"/>, <paramref name="y"/> lies inside <paramref name="box"/> with its
    /// <paramref name="corners"/> rounded, from 0 to 1. A corner is the curve |x|^power + |y|^power = radius^power, so a
    /// power of 2 is a circle. The radius is at most half the box's shorter side.
    /// </summary>
    public static double Coverage(int x, int y, Rectangle box, int radius, double power, Corners corners)
    {
        if (x < box.X || y < box.Y || x >= box.X + box.Width || y >= box.Y + box.Height)
            return 0;

        var r = Math.Min(radius, Math.Min(box.Width, box.Height) / 2.0);
        if (r <= 0 || corners == Corners.None)
            return 1;

        var left = x < box.X + r;
        var right = x >= box.X + box.Width - r;
        var top = y < box.Y + r;
        var bottom = y >= box.Y + box.Height - r;
        if (!(left || right) || !(top || bottom))
            return 1;

        var corner = top ? (left ? Corners.TopLeft : Corners.TopRight) : (left ? Corners.BottomLeft : Corners.BottomRight);
        if ((corners & corner) == 0)
            return 1;

        var centerX = left ? box.X + r : box.X + box.Width - r;
        var centerY = top ? box.Y + r : box.Y + box.Height - r;
        var inside = 0;
        for (var sy = 0; sy < Samples; sy++)
        {
            for (var sx = 0; sx < Samples; sx++)
            {
                var px = x + ((sx + 0.5) / Samples);
                var py = y + ((sy + 0.5) / Samples);
                var dx = left ? Math.Max(0, centerX - px) : Math.Max(0, px - centerX);
                var dy = top ? Math.Max(0, centerY - py) : Math.Max(0, py - centerY);
                if (Math.Pow(dx / r, power) + Math.Pow(dy / r, power) <= 1)
                    inside++;
            }
        }

        return inside / (double)(Samples * Samples);
    }

    /// <summary>
    /// The color of a vertical gradient at <paramref name="t"/>, from 0 at the top to 1 at the bottom. Color i of n stops at
    /// 1 - (i + 1) / (n + 1), so the first color is nearest the bottom, and one color fills it all.
    /// </summary>
    public static BorderColor GradientAt(IReadOnlyList<BorderColor> colors, double t)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Count == 0)
            return default;

        var n = colors.Count;
        double Stop(int i) => 1 - ((i + 1) / (double)(n + 1));

        // The stops run from the last color at the top to the first at the bottom.
        if (t <= Stop(n - 1))
            return colors[n - 1];

        if (t >= Stop(0))
            return colors[0];

        for (var i = n - 1; i > 0; i--)
        {
            var from = Stop(i);
            var to = Stop(i - 1);
            if (t > to)
                continue;

            var f = (t - from) / (to - from);
            return Mix(colors[i], colors[i - 1], f);
        }

        return colors[0];
    }

    private static BorderColor Mix(BorderColor a, BorderColor b, double f)
    {
        byte Channel(byte x, byte y) => (byte)Math.Round(x + ((y - x) * f));
        return new BorderColor(Channel(a.A, b.A), Channel(a.R, b.R), Channel(a.G, b.G), Channel(a.B, b.B));
    }
}
