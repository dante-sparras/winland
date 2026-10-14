namespace Winland.Core;

/// <summary>
/// What each monitor keeps for its tiles once its Bar has its strip (ADR 0080). Every monitor has a Bar of its own,
/// and a Bar is as thick as its own monitor's scale makes it, so each monitor is inset by the Bar that lies on it, in that monitor's pixels.
/// </summary>
public static class BarReserve
{
    /// <summary>The work area of each monitor in <paramref name="screens"/>, in the same order.</summary>
    /// <param name="screens">The monitors, whole, in physical pixels.</param>
    /// <param name="bars">The outer rectangle of each Bar on the glass. Empty while the Bar is hidden.</param>
    /// <param name="edge">The edge every Bar is on.</param>
    /// <returns>
    /// Each monitor less the strip of the Bar that lies on it: the Bar that covers the most of it. A monitor no Bar lies on is whole.
    /// </returns>
    public static TileRect[] Areas(IReadOnlyList<TileRect> screens, IReadOnlyList<TileRect> bars, ScreenEdge edge)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(bars);
        var areas = new TileRect[screens.Count];
        for (var i = 0; i < areas.Length; i++)
        {
            areas[i] = BarFootprint.Of(screens[i], edge, Thickness(screens[i], bars, edge)).Open;
        }

        return areas;
    }

    /// <summary>How thick the Bar on <paramref name="screen"/> is, across <paramref name="edge"/>. Zero when no Bar lies on that monitor.</summary>
    /// <param name="screen">One monitor, whole.</param>
    /// <param name="bars">The outer rectangle of each Bar on the glass.</param>
    /// <param name="edge">The edge every Bar is on.</param>
    /// <returns>Physical pixels.</returns>
    public static int Thickness(TileRect screen, IReadOnlyList<TileRect> bars, ScreenEdge edge)
    {
        ArgumentNullException.ThrowIfNull(bars);
        long most = 0;
        var thickness = 0;
        foreach (var bar in bars)
        {
            var shared = Shared(screen, bar);
            if (shared > most)
            {
                most = shared;
                thickness = edge is ScreenEdge.Left or ScreenEdge.Right ? bar.Width : bar.Height;
            }
        }

        return thickness;
    }

    private static long Shared(TileRect screen, TileRect bar)
    {
        var width = Math.Min((long)screen.X + screen.Width, (long)bar.X + bar.Width) - Math.Max(screen.X, bar.X);
        var height = Math.Min((long)screen.Y + screen.Height, (long)bar.Y + bar.Height) - Math.Max(screen.Y, bar.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }
}
