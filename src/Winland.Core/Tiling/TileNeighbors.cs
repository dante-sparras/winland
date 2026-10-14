namespace Winland.Core.Tiling;

/// <summary>Finds the visible tile that shares an edge in a compass direction.</summary>
internal static class TileNeighbors
{
    private const int EdgeSlop = 2;

    /// <summary>
    /// The most recently focused tile that shares <paramref name="origin"/>'s edge in <paramref name="direction"/>.
    /// A window with no such neighbor returns a default placement.
    /// </summary>
    public static Placement Nearest(
        IReadOnlyList<Placement> placed,
        Placement origin,
        long excludeId,
        Direction direction,
        IReadOnlyList<long> recent)
    {
        Placement best = default;
        var bestRank = int.MaxValue;
        var found = false;
        foreach (var item in placed)
        {
            if (!item.Visible || item.Id == excludeId || !SharesEdge(origin, item, direction))
            {
                continue;
            }

            var rank = Rank(recent, item.Id);
            if (!found || rank < bestRank)
            {
                found = true;
                best = item;
                bestRank = rank;
            }
        }

        return found ? best : default;
    }

    private static int Rank(IReadOnlyList<long> recent, long id)
    {
        for (var i = 0; i < recent.Count; i++)
        {
            if (recent[i] == id)
            {
                return i;
            }
        }

        return int.MaxValue - 1;
    }

    private static bool SharesEdge(Placement origin, Placement candidate, Direction direction)
    {
        if (Contains(origin, candidate) || Contains(candidate, origin))
        {
            return false;
        }

        int facingGap;
        bool onSide;
        int cross;
        var horizontal = direction is Direction.Left or Direction.Right;
        if (direction == Direction.Right)
        {
            facingGap = candidate.X - (origin.X + origin.Width);
            onSide = candidate.X + (candidate.Width / 2) > origin.X + (origin.Width / 2);
            cross = Overlap(origin.Y, origin.Y + origin.Height, candidate.Y, candidate.Y + candidate.Height);
        }
        else if (direction == Direction.Left)
        {
            facingGap = origin.X - (candidate.X + candidate.Width);
            onSide = candidate.X + (candidate.Width / 2) < origin.X + (origin.Width / 2);
            cross = Overlap(origin.Y, origin.Y + origin.Height, candidate.Y, candidate.Y + candidate.Height);
        }
        else if (direction == Direction.Down)
        {
            facingGap = candidate.Y - (origin.Y + origin.Height);
            onSide = candidate.Y + (candidate.Height / 2) > origin.Y + (origin.Height / 2);
            cross = Overlap(origin.X, origin.X + origin.Width, candidate.X, candidate.X + candidate.Width);
        }
        else
        {
            facingGap = origin.Y - (candidate.Y + candidate.Height);
            onSide = candidate.Y + (candidate.Height / 2) < origin.Y + (origin.Height / 2);
            cross = Overlap(origin.X, origin.X + origin.Width, candidate.X, candidate.X + candidate.Width);
        }

        if (!onSide || cross <= 0)
        {
            return false;
        }

        if (facingGap is >= -EdgeSlop and <= EdgeSlop)
        {
            return true;
        }

        var travel = horizontal
            ? Overlap(origin.X, origin.X + origin.Width, candidate.X, candidate.X + candidate.Width)
            : Overlap(origin.Y, origin.Y + origin.Height, candidate.Y, candidate.Y + candidate.Height);
        var smaller = horizontal ? Math.Min(origin.Width, candidate.Width) : Math.Min(origin.Height, candidate.Height);
        return travel > 0 && travel <= smaller / 2;
    }

    private static bool Contains(Placement outer, Placement inner) =>
        inner.X >= outer.X
        && inner.Y >= outer.Y
        && inner.X + inner.Width <= outer.X + outer.Width
        && inner.Y + inner.Height <= outer.Y + outer.Height
        && (inner.Width < outer.Width || inner.Height < outer.Height);

    private static int Overlap(int a0, int a1, int b0, int b1) => Math.Max(0, Math.Min(a1, b1) - Math.Max(a0, b0));
}
