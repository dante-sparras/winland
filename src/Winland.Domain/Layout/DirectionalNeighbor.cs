namespace Winland.Domain;

/// <summary>
/// The Client next to a rectangle in a direction: one that touches or slightly overlaps that side and
/// shares more than a pixel of its length with it. Among those, <c>binds:focus_preferred_method</c> 0 picks
/// the one focused most recently and 1 the one that shares the most length. Dwindle and master move focus
/// and windows this way. A floating Client looks by angle instead.
/// </summary>
internal static class DirectionalNeighbor
{
    public delegate bool BoxOf(WindowId window, out Rectangle box);

    /// <summary>
    /// <paramref name="recency"/> ranks a window by how recently it was focused, higher being more recent. With it the
    /// most recent candidate wins, and the first one wins a tie. Without it the longest shared length wins.
    /// </summary>
    public static WindowId? Find(
        IReadOnlyList<Client> clients,
        Rectangle origin,
        Side side,
        WindowId except,
        bool floating,
        BoxOf boxOf,
        Func<WindowId, int>? recency = null)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(boxOf);
        WindowId? best = null;
        var leader = recency is null ? 1.0 : -1.0;
        foreach (var client in clients)
        {
            if (client.Window == except || client.Minimized || client.Floating != floating)
                continue;
            if (!boxOf(client.Window, out var box))
                continue;
            if (!Overlap(origin, box, side, out var overlap))
                continue;

            var value = recency is null ? overlap : recency(client.Window);
            if (value <= leader)
                continue;

            leader = value;
            best = client.Window;
        }

        return best;
    }

    /// <summary>
    /// The floating or covering Client a floating Client looks at in a direction. Each candidate's center must lie within
    /// 90 degrees of the direction. One within 54 degrees beats a wider one, and among those the nearest wins.
    /// Before any is that close, the smallest angle wins. A tile is never the answer.
    /// </summary>
    public static WindowId? FindByAngle(IReadOnlyList<Client> clients, Rectangle origin, Side side, WindowId except, BoxOf boxOf)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(boxOf);
        const double Threshold = 0.3 * Math.PI;
        var (dirX, dirY) = side switch
        {
            Side.Left => (-1, 0),
            Side.Right => (1, 0),
            Side.Up => (0, -1),
            _ => (0, 1),
        };

        WindowId? best = null;
        var bestAngle = 2 * Math.PI;
        var leader = -1;
        foreach (var client in clients)
        {
            if (client.Window == except || client.Minimized || (!client.Floating && client.Fullscreen == FullscreenMode.None))
                continue;
            if (!boxOf(client.Window, out var box))
                continue;

            var x = Middle(box.X, box.Width) - Middle(origin.X, origin.Width);
            var y = Middle(box.Y, box.Height) - Middle(origin.Y, origin.Height);
            var distance = Math.Sqrt((x * x) + (y * y));
            var angle = Math.Acos(((x * dirX) + (y * dirY)) / distance);
            if (angle > Math.PI / 2)
                continue;

            if ((bestAngle < Threshold && distance < leader && angle < Threshold) || (angle < bestAngle && bestAngle > Threshold) || leader == -1)
            {
                // Winland keeps the distance as a whole number.
                leader = (int)distance;
                bestAngle = angle;
                best = client.Window;
            }
        }

        return best;
    }

    private static double Middle(int start, int size) => start + (size / 2.0);

    private static bool Overlap(Rectangle origin, Rectangle other, Side side, out double overlap)
    {
        var adjacent = side switch
        {
            Side.Left => Adjacent(origin.X, origin.X + origin.Width, other.X, other.X + other.Width),
            Side.Right => Adjacent(other.X, other.X + other.Width, origin.X, origin.X + origin.Width),
            Side.Up => Adjacent(origin.Y, origin.Y + origin.Height, other.Y, other.Y + other.Height),
            Side.Down => Adjacent(other.Y, other.Y + other.Height, origin.Y, origin.Y + origin.Height),
            _ => false,
        };

        overlap = adjacent
            ? side is Side.Left or Side.Right
                ? Shared(origin.Y, origin.Height, other.Y, other.Height)
                : Shared(origin.X, origin.Width, other.X, other.Width)
            : 0;
        return overlap > 1;
    }

    private static bool Adjacent(int aMin, int aMax, int bMin, int bMax)
    {
        var delta = aMin - bMax;
        if (Math.Abs(delta) < 2)
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
