namespace Winland.Core.Tiling;

/// <summary>
/// F11 on a window that is fullscreen in its own app while it sits on a tile goes through a screen freeze. The app puts its frame back
/// before Winoma can take it off again, and lays its page out again after that, so a still picture of the monitor covers both.
/// F11 that makes a tile fullscreen needs none: the app covers the monitor, which is where the window belongs (ADR 0076).
/// </summary>
public static class FullscreenFreeze
{
    /// <summary>The shortest freeze, in milliseconds. The app gets this long to answer the key.</summary>
    public const long ShortestMs = 150;

    /// <summary>How long the window must stay on its tile without moving before the picture lifts, in milliseconds.</summary>
    public const long SettleMs = 120;

    /// <summary>The longest freeze, in milliseconds, whatever the window does.</summary>
    public const long LongestMs = 800;

    /// <summary>True when F11 should freeze the screen before it reaches <paramref name="foreground"/>.</summary>
    /// <param name="foreground">The window that has the keyboard.</param>
    /// <param name="placed">The tiles last applied.</param>
    /// <param name="inApp">True when that window is fullscreen in its own app, so the key takes it out.</param>
    /// <param name="modified">True while Super, Shift, Control, or Alt is held. Those chords belong to the app or to Winoma.</param>
    /// <returns>True for a plain F11 on a visible tile that is fullscreen in its app and does not cover the monitor.</returns>
    public static bool Takes(long? foreground, IReadOnlyList<Placement> placed, bool inApp, bool modified)
    {
        ArgumentNullException.ThrowIfNull(placed);
        if (modified || !inApp || foreground is not long id)
        {
            return false;
        }

        foreach (var placement in placed)
        {
            if (placement.Id == id)
            {
                return placement.Visible && !placement.Raised && placement.Width > 0 && placement.Height > 0;
            }
        }

        return false;
    }

    /// <summary>True when the picture can lift.</summary>
    /// <param name="sinceKeyMs">Milliseconds since F11 was sent to the app.</param>
    /// <param name="sinceMoveMs">Milliseconds since the window last moved or resized.</param>
    /// <param name="onTile">True when the window sits on its tile.</param>
    /// <returns>True once the window has settled on its tile, or the longest freeze has passed.</returns>
    public static bool Lifts(long sinceKeyMs, long sinceMoveMs, bool onTile) =>
        sinceKeyMs >= LongestMs || (sinceKeyMs >= ShortestMs && onTile && sinceMoveMs >= SettleMs);
}
