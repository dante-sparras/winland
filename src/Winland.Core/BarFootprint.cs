namespace Winland.Core;

/// <summary>
/// The strip a bar occupies on one screen, and the remainder tiling keeps.
/// Thickness is physical pixels. Zero occupies nothing.
/// </summary>
/// <param name="Occupied">The strip. A hidden bar has no thickness.</param>
/// <param name="Open">The remainder. At least one pixel on the axis the bar takes.</param>
public readonly record struct BarFootprint(TileRect Occupied, TileRect Open)
{
    /// <summary>
    /// The occupied strip and the open remainder of <paramref name="screen"/> for <paramref name="edge"/>.
    /// A thickness past the screen leaves the remainder one pixel. A thickness of zero leaves the screen whole.
    /// </summary>
    /// <param name="screen">One monitor, in physical pixels.</param>
    /// <param name="edge">The edge the bar occupies.</param>
    /// <param name="thickness">Bar thickness in physical pixels. Zero is a hidden bar.</param>
    /// <returns>The pair. The two rectangles share an edge and cover the screen.</returns>
    public static BarFootprint Of(TileRect screen, ScreenEdge edge, int thickness)
    {
        var width = Math.Max(1, screen.Width);
        var height = Math.Max(1, screen.Height);
        var x = screen.X;
        var y = screen.Y;
        var horizontal = edge is ScreenEdge.Left or ScreenEdge.Right;
        var room = horizontal ? width : height;
        var taken = thickness <= 0 ? 0 : Math.Min(thickness, room - 1);
        return edge switch
        {
            ScreenEdge.Left => new BarFootprint(
                new TileRect(x, y, taken, height),
                new TileRect(x + taken, y, width - taken, height)),
            ScreenEdge.Right => new BarFootprint(
                new TileRect(x + width - taken, y, taken, height),
                new TileRect(x, y, width - taken, height)),
            ScreenEdge.Bottom => new BarFootprint(
                new TileRect(x, y + height - taken, width, taken),
                new TileRect(x, y, width, height - taken)),
            _ => new BarFootprint(
                new TileRect(x, y, width, taken),
                new TileRect(x, y + taken, width, height - taken)),
        };
    }
}
