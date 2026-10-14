namespace Winland.Core.Tiling;

/// <summary>
/// Reads a tile tree back from the rectangles its windows already cover.
/// A desktop that starts finds the windows where the last one left them, so the layout on the glass is the one to keep.
/// </summary>
internal static class TileRecall
{
    /// <summary>Overlap, in pixels, that two neighbors may show and still count as beside each other.</summary>
    private const int Overlap = 8;

    /// <summary>
    /// The tree whose tiles are <paramref name="seen"/>. A straight line no window crosses becomes a split,
    /// with the ratio its two sides show. Windows that overlap are split in half, in the order of their centers.
    /// </summary>
    /// <param name="seen">The rectangle each window covers. Not empty, and each window once.</param>
    public static TileNode Read(IReadOnlyList<Placement> seen)
    {
        if (seen.Count == 1)
        {
            var leaf = new TileLeaf();
            leaf.Members.Add(seen[0].Id);
            return leaf;
        }

        var box = Box(seen);
        var wide = box.Width >= box.Height;
        return Cut(seen, wide) ?? Cut(seen, !wide) ?? Halve(seen, wide);
    }

    private static TileSplit? Cut(IReadOnlyList<Placement> seen, bool sideBySide)
    {
        var ordered = seen.OrderBy(item => Start(item, sideBySide)).ToList();
        var reach = End(ordered[0], sideBySide);
        for (var i = 1; i < ordered.Count; i++)
        {
            if (reach <= Start(ordered[i], sideBySide) + Overlap)
            {
                var first = ordered.GetRange(0, i);
                var second = ordered.GetRange(i, ordered.Count - i);
                return new TileSplit(Read(first), Read(second), sideBySide)
                {
                    Ratio = Ratio(Span(Box(first), sideBySide), Span(Box(second), sideBySide)),
                };
            }

            reach = Math.Max(reach, End(ordered[i], sideBySide));
        }

        return null;
    }

    private static TileSplit Halve(IReadOnlyList<Placement> seen, bool wide)
    {
        var across = Spread(seen, sideBySide: true);
        var down = Spread(seen, sideBySide: false);
        var sideBySide = across == down ? wide : across > down;
        var ordered = seen.OrderBy(item => Center(item, sideBySide)).ToList();
        var half = ordered.Count / 2;
        return new TileSplit(Read(ordered.GetRange(0, half)), Read(ordered.GetRange(half, ordered.Count - half)), sideBySide);
    }

    /// <summary>
    /// The ratio that gives the first side <paramref name="first"/> pixels again.
    /// The arranger rounds down, so the ratio aims at the middle of that pixel.
    /// </summary>
    private static double Ratio(int first, int second)
    {
        var usable = first + second;
        if (usable <= 1 || first == usable / 2)
        {
            return 0.5;
        }

        return Math.Clamp((first + 0.5) / usable, 0.05, 0.95);
    }

    private static int Spread(IReadOnlyList<Placement> seen, bool sideBySide) =>
        seen.Max(item => Center(item, sideBySide)) - seen.Min(item => Center(item, sideBySide));

    private static int Start(Placement item, bool sideBySide) => sideBySide ? item.X : item.Y;

    private static int End(Placement item, bool sideBySide) => sideBySide ? item.X + item.Width : item.Y + item.Height;

    private static int Center(Placement item, bool sideBySide) => sideBySide ? item.X + (item.Width / 2) : item.Y + (item.Height / 2);

    private static int Span(TileRect box, bool sideBySide) => sideBySide ? box.Width : box.Height;

    private static TileRect Box(IReadOnlyList<Placement> seen)
    {
        var left = seen.Min(item => item.X);
        var top = seen.Min(item => item.Y);
        return new TileRect(left, top, seen.Max(item => item.X + item.Width) - left, seen.Max(item => item.Y + item.Height) - top);
    }
}
