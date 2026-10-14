namespace Winland.Core.Shell;

/// <summary>
/// The tiling settings the window manager reads, and the two toggles a chord writes.
/// The shell owns the rest of the person's settings and pushes what the window manager does not read here.
/// </summary>
public interface ITileSettings
{
    /// <summary>Pixels between tiles when <see cref="SpacingEnabled"/> is true.</summary>
    int SpacingPixels { get; }

    /// <summary>True when gutters are drawn between tiles.</summary>
    bool SpacingEnabled { get; set; }

    /// <summary>True when a workspace's one tiled window is drawn as a square.</summary>
    bool SquareTileEnabled { get; set; }

    /// <summary>True when the bar is shown. The shell hides and shows the bar. The window manager only reads this.</summary>
    bool BarVisible { get; }

    /// <summary>True when idle lock is armed. The shell owns the lock. The window manager only reads this.</summary>
    bool IdleLockEnabled { get; }

    /// <summary>The browser program the shell named, or empty when it named none.</summary>
    string Browser { get; }

    /// <summary>The face the bar draws with.</summary>
    string FontFace { get; }
}
