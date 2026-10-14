namespace Winland.Core.Tiling;

/// <summary>Picks a neighboring monitor from screen rectangles.</summary>
public static class MonitorPick
{
    /// <summary>
    /// The monitor in <paramref name="direction"/> from <paramref name="current"/>.
    /// The same index when no monitor lies that way.
    /// </summary>
    /// <param name="monitors">Work areas, in any order.</param>
    /// <param name="current">Index into <paramref name="monitors"/>.</param>
    /// <param name="direction">The arrow.</param>
    public static int Neighbor(IReadOnlyList<TileRect> monitors, int current, Direction direction)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            return 0;
        }

        current = Math.Clamp(current, 0, monitors.Count - 1);
        var origin = Center(monitors[current]);
        var best = current;
        var bestDistance = long.MaxValue;
        for (var i = 0; i < monitors.Count; i++)
        {
            if (i == current)
            {
                continue;
            }

            var center = Center(monitors[i]);
            var dx = center.X - origin.X;
            var dy = center.Y - origin.Y;
            if (!Toward(direction, dx, dy))
            {
                continue;
            }

            var distance = ((long)dx * dx) + ((long)dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private static bool Toward(Direction direction, int dx, int dy) => direction switch
    {
        Direction.Left => dx < 0 && Math.Abs(dx) >= Math.Abs(dy),
        Direction.Right => dx > 0 && Math.Abs(dx) >= Math.Abs(dy),
        Direction.Up => dy < 0 && Math.Abs(dy) >= Math.Abs(dx),
        Direction.Down => dy > 0 && Math.Abs(dy) >= Math.Abs(dx),
        _ => false,
    };

    private static (int X, int Y) Center(TileRect rect) => (rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
}
