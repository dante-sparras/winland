namespace Winland.Core.Shell;

/// <summary>Whether a window's rectangle fills a monitor.</summary>
public static class ScreenCover
{
    /// <summary>Pixels a window may fall short and still count as filling the monitor.</summary>
    public const int Slack = 8;

    /// <summary>True when <paramref name="window"/> covers <paramref name="screen"/>.</summary>
    /// <param name="window">The window's outer rectangle.</param>
    /// <param name="screen">The monitor, including the strip the bar reserves.</param>
    /// <returns>True when the window reaches each edge of the monitor, within <see cref="Slack"/>.</returns>
    public static bool Fills(TileRect window, TileRect screen)
    {
        if (window.Width <= 0 || window.Height <= 0 || screen.Width <= 0 || screen.Height <= 0)
        {
            return false;
        }

        return window.X <= screen.X + Slack
            && window.Y <= screen.Y + Slack
            && window.X + window.Width >= screen.X + screen.Width - Slack
            && window.Y + window.Height >= screen.Y + screen.Height - Slack;
    }
}
