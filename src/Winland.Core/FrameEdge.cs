namespace Winland.Core;

/// <summary>How far a tiled window extends past its tile to cover a border Windows leaves unpainted.</summary>
public static class FrameEdge
{
    /// <summary>
    /// Physical pixels to place above the tile.
    /// A client that starts below the frame leaves the whole border unpainted, so the window
    /// extends by <paramref name="thickness"/>. A client that already meets the frame still
    /// leaves the outer pixel clear, and the wallpaper shows through that pixel under the bar.
    /// </summary>
    /// <param name="clientBelowFrame">True when the client area starts below the visible frame.</param>
    /// <param name="thickness">Visible border thickness in physical pixels. Zero when Windows does not say.</param>
    /// <returns>Pixels to extend the window upward.</returns>
    public static int Top(bool clientBelowFrame, int thickness)
    {
        if (thickness <= 0)
        {
            return 0;
        }

        return clientBelowFrame ? thickness : 1;
    }
}
