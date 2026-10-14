namespace Winland.Core;

/// <summary>Whether the bar is drawn in front of a window that crosses it.</summary>
public static class BarLayer
{
    /// <summary>An overlap of this many pixels or fewer on either axis does not cover the bar.</summary>
    public const int Graze = 12;

    /// <summary>How old the last look may be before the bar loop looks again with nothing covering the bar, in milliseconds.</summary>
    public const long StaleMs = 5000;

    /// <summary>
    /// True when the bar loop should look for a covering window on this turn.
    /// A window that moves, resizes, takes the foreground, or is shown is looked at as it happens, so the loop is only the safety net:
    /// it looks on every turn while the bar is covered or a window is fullscreen on its monitor, and otherwise once per <see cref="StaleMs"/>.
    /// </summary>
    /// <param name="covered">True while the bar is behind a window.</param>
    /// <param name="fullscreenOnBarMonitor">True when a fullscreen window is on the bar's monitor.</param>
    /// <param name="sinceLookMs">Milliseconds since the last look.</param>
    /// <returns>True to look now.</returns>
    public static bool LooksAgain(bool covered, bool fullscreenOnBarMonitor, long sinceLookMs) =>
        covered || fullscreenOnBarMonitor || sinceLookMs >= StaleMs;

    /// <summary>The bar in front, and the panel topmost. Both are false when the bar goes behind.</summary>
    /// <param name="BarInFront">False when the bar goes behind a covering window.</param>
    /// <param name="PanelTopmost">False when the panel goes behind with the bar.</param>
    public readonly record struct Verdict(bool BarInFront, bool PanelTopmost);

    /// <summary>True when <paramref name="window"/> crosses <paramref name="bar"/> by more than a graze on both axes.</summary>
    /// <param name="window">The other window's outer rectangle.</param>
    /// <param name="bar">The bar rectangle.</param>
    /// <returns>True when both axes overlap by more than <see cref="Graze"/> pixels.</returns>
    public static bool Crosses(TileRect window, TileRect bar) =>
        Overlap(window.X, window.Width, bar.X, bar.Width) > Graze
        && Overlap(window.Y, window.Height, bar.Y, bar.Height) > Graze;

    /// <summary>
    /// Decides whether the bar stays in front.
    /// Fullscreen on the bar's monitor, a window that fills that screen, or a topmost window that crosses the bar each put the bar behind.
    /// The panel stays topmost only while the bar is in front.
    /// </summary>
    /// <param name="fullscreenOnBarMonitor">True when a fullscreen window is on the bar's monitor.</param>
    /// <param name="windowFillsBarScreen">True when a window fills the bar's monitor.</param>
    /// <param name="topmostCrossesBar">True when a topmost window crosses the bar rectangle.</param>
    /// <returns>Both false when the bar goes behind. Both true when it stays in front.</returns>
    public static Verdict Decide(bool fullscreenOnBarMonitor, bool windowFillsBarScreen, bool topmostCrossesBar)
    {
        var behind = fullscreenOnBarMonitor || windowFillsBarScreen || topmostCrossesBar;
        return new Verdict(!behind, !behind);
    }

    private static int Overlap(int origin, int size, int otherOrigin, int otherSize)
    {
        var start = Math.Max(origin, otherOrigin);
        var end = Math.Min(origin + size, otherOrigin + otherSize);
        return end - start;
    }
}
