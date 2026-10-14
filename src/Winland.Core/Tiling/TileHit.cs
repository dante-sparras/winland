namespace Winland.Core.Tiling;

/// <summary>Finds the window whose tile contains a point.</summary>
public static class TileHit
{
    /// <summary>
    /// The visible tile under <paramref name="x"/>, <paramref name="y"/>.
    /// A raised window, such as fullscreen, wins when it also contains the point.
    /// </summary>
    /// <param name="placed">The tiles just applied.</param>
    /// <param name="x">Pointer x, in pixels.</param>
    /// <param name="y">Pointer y, in pixels.</param>
    /// <returns>The window identity, or null when the point is outside every tile.</returns>
    public static long? At(IReadOnlyList<Placement> placed, int x, int y)
    {
        long? tile = null;
        foreach (var placement in placed)
        {
            if (!placement.Visible || placement.Width <= 0 || placement.Height <= 0)
            {
                continue;
            }

            if (x < placement.X || y < placement.Y || x >= placement.X + placement.Width || y >= placement.Y + placement.Height)
            {
                continue;
            }

            if (placement.Raised)
            {
                return placement.Id;
            }

            tile = placement.Id;
        }

        return tile;
    }
}
