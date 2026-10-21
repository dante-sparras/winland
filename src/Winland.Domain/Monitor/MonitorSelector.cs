using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// Resolves a Monitor argument: a name, a <c>desc:</c> prefix of its identity, a direction,
/// <c>current</c>, a relative <c>+N</c> or <c>-N</c>, or an id.
/// </summary>
internal static class MonitorSelector
{
    private static readonly (string Word, MonitorDirection Direction)[] Words =
    [
        ("l", MonitorDirection.Left),
        ("left", MonitorDirection.Left),
        ("r", MonitorDirection.Right),
        ("right", MonitorDirection.Right),
        ("u", MonitorDirection.Up),
        ("up", MonitorDirection.Up),
        ("t", MonitorDirection.Up),
        ("top", MonitorDirection.Up),
        ("d", MonitorDirection.Down),
        ("down", MonitorDirection.Down),
        ("b", MonitorDirection.Down),
        ("bottom", MonitorDirection.Down),
    ];

    public static bool TryDirection(string text, out MonitorDirection direction)
    {
        foreach (var (word, parsed) in Words)
        {
            if (text.Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                direction = parsed;
                return true;
            }
        }

        direction = MonitorDirection.Left;
        return false;
    }

    public static MonitorTarget? Resolve(string? text, IReadOnlyList<MonitorTarget> monitors, MonitorTarget? current)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (string.IsNullOrWhiteSpace(text) || monitors.Count == 0)
            return null;

        text = text.Trim();
        if (text.Equals("current", StringComparison.OrdinalIgnoreCase))
            return current;

        if (TryDirection(text, out var direction))
            return current is MonitorTarget origin ? InDirection(monitors, origin, direction) : null;

        if (text[0] is '+' or '-')
            return Relative(text, monitors, current);

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return ById(monitors, id);

        if (text.StartsWith("desc:", StringComparison.OrdinalIgnoreCase))
        {
            var description = text[5..].Trim();
            foreach (var monitor in monitors)
            {
                if (monitor.Description.StartsWith(description, StringComparison.Ordinal))
                    return monitor;
            }

            return null;
        }

        foreach (var monitor in monitors)
        {
            if (monitor.Name.Equals(text, StringComparison.Ordinal))
                return monitor;
        }

        return null;
    }

    public static MonitorTarget? InDirection(IReadOnlyList<MonitorTarget> monitors, MonitorTarget origin, MonitorDirection direction)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        var best = -1.0;
        MonitorTarget? chosen = null;
        foreach (var monitor in monitors)
        {
            if (monitor.Id == origin.Id || !Touches(origin, monitor, direction))
                continue;

            var length = direction is MonitorDirection.Left or MonitorDirection.Right
                ? Shared(origin.Y, origin.Height, monitor.Y, monitor.Height)
                : Shared(origin.X, origin.Width, monitor.X, monitor.Width);
            if (length <= best)
                continue;

            best = length;
            chosen = monitor;
        }

        return chosen;
    }

    public static MonitorTarget? At(IReadOnlyList<MonitorTarget> monitors, Point point)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        MonitorTarget? closest = null;
        var best = long.MaxValue;
        foreach (var monitor in monitors)
        {
            if (Contains(monitor, point))
                return monitor;

            var distance = DistanceSquared(monitor, point);
            if (distance >= best)
                continue;

            best = distance;
            closest = monitor;
        }

        return closest;
    }

    public static Point Focal(Rectangle box, MonitorDirection direction) => direction switch
    {
        MonitorDirection.Left => new Point(box.X - 1, box.Y + (box.Height / 2)),
        MonitorDirection.Right => new Point(box.X + box.Width + 1, box.Y + (box.Height / 2)),
        MonitorDirection.Up => new Point(box.X + (box.Width / 2), box.Y - 1),
        MonitorDirection.Down => new Point(box.X + (box.Width / 2), box.Y + box.Height + 1),
        _ => new Point(box.X, box.Y),
    };

    public static bool Overlaps(Rectangle origin, Rectangle other, MonitorDirection direction, out double overlap)
    {
        var adjacent = direction switch
        {
            MonitorDirection.Left => Adjacent(origin.X, origin.X + origin.Width, other.X, other.X + other.Width),
            MonitorDirection.Right => Adjacent(other.X, other.X + other.Width, origin.X, origin.X + origin.Width),
            MonitorDirection.Up => Adjacent(origin.Y, origin.Y + origin.Height, other.Y, other.Y + other.Height),
            MonitorDirection.Down => Adjacent(other.Y, other.Y + other.Height, origin.Y, origin.Y + origin.Height),
            _ => false,
        };

        overlap = adjacent
            ? direction is MonitorDirection.Left or MonitorDirection.Right
                ? Shared(origin.Y, origin.Height, other.Y, other.Height)
                : Shared(origin.X, origin.Width, other.X, other.Width)
            : 0;
        return overlap > 1;
    }

    private static MonitorTarget? Relative(string text, IReadOnlyList<MonitorTarget> monitors, MonitorTarget? current)
    {
        if (monitors.Count == 1)
            return monitors[0];

        var offsetText = text[0] == '-' ? text : text[1..];
        if (!int.TryParse(offsetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset))
            return null;

        var size = monitors.Count;
        offset = offset < 0 ? -((-offset) % size) : offset % size;
        var place = 0;
        if (current is MonitorTarget origin)
        {
            for (var i = 0; i < monitors.Count; i++)
            {
                if (monitors[i].Id == origin.Id)
                {
                    place = i;
                    break;
                }
            }
        }

        place += offset;
        if (place < 0)
            place += size;
        else
            place %= size;

        if (place < 0 || place >= size)
            place = Math.Clamp(place, 0, size - 1);

        return monitors[place];
    }

    private static MonitorTarget? ById(IReadOnlyList<MonitorTarget> monitors, int id)
    {
        if (id < 0 || id >= monitors.Count)
            return null;

        foreach (var monitor in monitors)
        {
            if (monitor.Id.Value == id)
                return monitor;
        }

        return null;
    }

    private static bool Touches(MonitorTarget origin, MonitorTarget monitor, MonitorDirection direction) => direction switch
    {
        MonitorDirection.Left => Sticks(origin.X, monitor.X + monitor.Width),
        MonitorDirection.Right => Sticks(origin.X + origin.Width, monitor.X),
        MonitorDirection.Up => Sticks(origin.Y, monitor.Y + monitor.Height),
        MonitorDirection.Down => Sticks(origin.Y + origin.Height, monitor.Y),
        _ => false,
    };

    private static bool Sticks(int edge, int other) => Math.Abs(edge - other) < 2;

    private static bool Contains(MonitorTarget monitor, Point point) =>
        point.X >= monitor.X
        && point.Y >= monitor.Y
        && point.X < monitor.X + monitor.Width
        && point.Y < monitor.Y + monitor.Height;

    private static long DistanceSquared(MonitorTarget monitor, Point point)
    {
        var x = Math.Clamp(point.X, monitor.X, monitor.X + monitor.Width);
        var y = Math.Clamp(point.Y, monitor.Y, monitor.Y + monitor.Height);
        var dx = (long)x - point.X;
        var dy = (long)y - point.Y;
        return (dx * dx) + (dy * dy);
    }

    private static bool Adjacent(int aMin, int aMax, int bMin, int bMax)
    {
        var delta = aMin - bMax;
        if (Math.Abs(delta) <= 2)
            return true;
        if (delta >= 0)
            return false;

        var overlap = -delta;
        var sizeA = aMax - aMin;
        var sizeB = bMax - bMin;
        if ((bMin <= aMin && bMax >= aMax) || (aMin <= bMin && aMax >= bMax))
            return false;

        return overlap <= Math.Min(sizeA, sizeB) * 0.5;
    }

    private static double Shared(int start, int size, int otherStart, int otherSize) =>
        Math.Max(0, Math.Min(start + size, otherStart + otherSize) - Math.Max(start, otherStart));
}
