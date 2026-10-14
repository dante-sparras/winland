namespace Winland.Core.Tiling;

/// <summary>
/// Where a window that is made and not yet shown is put, so it is first drawn on the monitor of the Workspace it joins (ADR 0038).
/// Windows makes a program's first window on the primary monitor, and a tile placed after the window is shown leaves it
/// there for a moment first.
/// </summary>
public static class OpeningSeat
{
    /// <summary>The rectangle to move <paramref name="window"/> to: as far from the corner of <paramref name="target"/> as it is from its own monitor's, and inside it.</summary>
    /// <param name="window">The rectangle the window was made with, in physical pixels.</param>
    /// <param name="screens">Every monitor, whole.</param>
    /// <param name="target">The monitor of the Workspace the window joins, whole.</param>
    /// <returns>Null when the window is on that monitor already, or either rectangle is empty.</returns>
    public static TileRect? On(TileRect window, IReadOnlyList<TileRect> screens, TileRect target)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (window.Width <= 0 || window.Height <= 0 || target.Width <= 0 || target.Height <= 0)
        {
            return null;
        }

        var x = window.X + (window.Width / 2);
        var y = window.Y + (window.Height / 2);
        if (Holds(target, x, y))
        {
            return null;
        }

        var width = Math.Min(window.Width, target.Width);
        var height = Math.Min(window.Height, target.Height);

        // A window on no monitor has no corner to measure from, and takes the middle.
        var left = target.X + ((target.Width - width) / 2);
        var top = target.Y + ((target.Height - height) / 2);
        foreach (var screen in screens)
        {
            if (Holds(screen, x, y))
            {
                left = target.X + (window.X - screen.X);
                top = target.Y + (window.Y - screen.Y);
                break;
            }
        }

        return new TileRect(
            Math.Clamp(left, target.X, target.X + target.Width - width),
            Math.Clamp(top, target.Y, target.Y + target.Height - height),
            width,
            height);
    }

    private static bool Holds(TileRect screen, int x, int y) =>
        x >= screen.X && x < screen.X + screen.Width && y >= screen.Y && y < screen.Y + screen.Height;
}
