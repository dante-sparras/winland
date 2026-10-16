namespace Winland;

/// <summary>
/// The Client next to a rectangle in a direction: the one that touches or slightly overlaps that side
/// and shares the most of its length with it. Dwindle and master move focus and windows this way.
/// </summary>
internal static class DirectionalNeighbor
{
    public delegate bool BoxOf(WindowId window, out Rectangle box);

    public static WindowId? Find(IReadOnlyList<Client> clients, Rectangle origin, Side side, WindowId except, bool floating, BoxOf boxOf)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(boxOf);
        WindowId? best = null;
        var bestOverlap = 1.0;
        foreach (var client in clients)
        {
            if (client.Window == except || client.Minimized || client.Floating != floating)
                continue;
            if (!boxOf(client.Window, out var box))
                continue;
            if (!Overlap(origin, box, side, out var overlap) || overlap <= bestOverlap)
                continue;

            bestOverlap = overlap;
            best = client.Window;
        }

        return best;
    }

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
