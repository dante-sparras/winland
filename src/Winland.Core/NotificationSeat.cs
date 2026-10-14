namespace Winland.Core;

/// <summary>Where a Windows notification window sits against a bar, in physical pixels.</summary>
public static class NotificationSeat
{
    /// <summary>
    /// A toast or the notification center is a narrow shell window.
    /// A full-width shell surface, such as Start, is not.
    /// </summary>
    /// <param name="className">Window class.</param>
    /// <param name="width">Window width, in physical pixels.</param>
    /// <param name="height">Window height, in physical pixels.</param>
    /// <param name="screenWidth">Width of the monitor that holds the window.</param>
    /// <returns>True when the window is a notification surface.</returns>
    public static bool IsNotification(string className, int width, int height, int screenWidth)
    {
        if (!string.Equals(className, "Windows.UI.Core.CoreWindow", StringComparison.Ordinal))
        {
            return false;
        }

        if (screenWidth <= 0 || width < 160 || height < 48)
        {
            return false;
        }

        return width <= screenWidth / 2;
    }

    /// <summary>
    /// Sits the window on the bar's outer edge, at the screen's right.
    /// <paramref name="occupied"/> is how far a previous notification in the same stack already reaches.
    /// A window that would pass the screen is shortened.
    /// </summary>
    /// <param name="screen">The monitor that holds the bar.</param>
    /// <param name="bar">The bar window.</param>
    /// <param name="width">Window width.</param>
    /// <param name="height">Window height.</param>
    /// <param name="occupied">Pixels already taken along the bar's outer edge.</param>
    /// <returns>The window's place.</returns>
    public static TileRect Fit(TileRect screen, TileRect bar, int width, int height, int occupied = 0)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        occupied = Math.Max(0, occupied);
        if (screen.Width <= 0 || screen.Height <= 0)
        {
            return new TileRect(screen.X, screen.Y, width, height);
        }

        if (width > screen.Width)
        {
            width = screen.Width;
        }

        var x = screen.X + screen.Width - width;
        var horizontal = bar.Width >= bar.Height && bar.Height > 0;
        var alongTop = horizontal && bar.Y - screen.Y <= bar.Height;
        var alongBottom = horizontal && screen.Y + screen.Height - (bar.Y + bar.Height) <= bar.Height && !alongTop;
        if (alongBottom)
        {
            var floor = bar.Y - occupied;
            var room = Math.Max(1, floor - screen.Y);
            var fitted = Math.Min(height, room);
            return new TileRect(x, floor - fitted, width, fitted);
        }

        var y = (alongTop ? bar.Y + bar.Height : screen.Y) + occupied;
        var limit = screen.Y + screen.Height;
        if (y >= limit)
        {
            y = limit - 1;
        }

        var below = Math.Max(1, limit - y);
        return new TileRect(x, y, width, Math.Min(height, below));
    }

    /// <summary>
    /// True when a Windows notification window is a toast banner, not notification center.
    /// A banner is at most half the screen's height. Notification center runs the screen's height.
    /// </summary>
    /// <param name="height">Window height, in physical pixels.</param>
    /// <param name="screenHeight">Height of the monitor that holds the window.</param>
    /// <returns>True for a banner.</returns>
    public static bool IsBanner(int height, int screenHeight) => screenHeight > 0 && height > 0 && height <= screenHeight / 2;

    /// <summary>Where a Windows banner goes while the shell draws its own toasts: past every monitor, where Windows parks a minimized window.</summary>
    /// <param name="width">Window width.</param>
    /// <param name="height">Window height.</param>
    /// <returns>The window's place.</returns>
    public static TileRect Away(int width, int height) => new(-32000, -32000, Math.Max(1, width), Math.Max(1, height));
}
