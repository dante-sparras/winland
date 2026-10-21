namespace Winland.Core;

/// <summary>
/// <c>cursor:hotspot_padding</c>: the cursor's hotspot keeps that many pixels from the outer edges of the monitors, as
/// Winland keeps it. A point whose box of the padding around it is not inside one monitor is pushed into the
/// layout, towards the nearest monitor, until the corners of that box are on a monitor.
/// </summary>
public static class HotspotPadding
{
    /// <summary>The point nearest <paramref name="point"/> that keeps <paramref name="padding"/> pixels from the layout's edges.</summary>
    public static Point ClosestValid(Point point, IReadOnlyList<Rectangle> monitors, int padding)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        padding = Math.Clamp(padding, 0, 100);
        if (padding == 0 || monitors.Count == 0)
            return point;

        var box = new Rectangle(point.X - padding, point.Y - padding, 2 * padding, 2 * padding);
        foreach (var monitor in monitors)
        {
            if (Inside(box, monitor))
                return point;
        }

        var leader = Nearest(point, monitors);
        var x = leader.X - padding;
        var y = leader.Y - padding;
        foreach (var (dx, dy) in (ReadOnlySpan<(int, int)>)[(1, 1), (-1, -1), (1, -1), (-1, 1)])
        {
            var corner = new Point(x + padding + (dx * padding), y + padding + (dy * padding));
            if (Contains(monitors, corner))
                continue;

            var nearest = Nearest(corner, monitors);
            x += nearest.X - corner.X;
            y += nearest.Y - corner.Y;
        }

        return new Point(x + padding, y + padding);
    }

    // Winland's boxes are half-open on their far edges: the last pixel row or column is not inside.
    private static bool Inside(Rectangle box, Rectangle monitor) =>
        box.X >= monitor.X && box.Y >= monitor.Y
        && box.X + box.Width <= monitor.X + monitor.Width && box.Y + box.Height <= monitor.Y + monitor.Height;

    private static bool Contains(IReadOnlyList<Rectangle> monitors, Point point)
    {
        foreach (var monitor in monitors)
        {
            if (point.X >= monitor.X && point.X < monitor.X + monitor.Width && point.Y >= monitor.Y && point.Y < monitor.Y + monitor.Height)
                return true;
        }

        return false;
    }

    private static Point Nearest(Point point, IReadOnlyList<Rectangle> monitors)
    {
        var best = point;
        var distance = long.MaxValue;
        foreach (var monitor in monitors)
        {
            var closest = new Point(
                Math.Clamp(point.X, monitor.X, monitor.X + monitor.Width - 1),
                Math.Clamp(point.Y, monitor.Y, monitor.Y + monitor.Height - 1));
            var dx = (long)(closest.X - point.X);
            var dy = (long)(closest.Y - point.Y);
            if ((dx * dx) + (dy * dy) < distance)
            {
                distance = (dx * dx) + (dy * dy);
                best = closest;
            }
        }

        return best;
    }
}
