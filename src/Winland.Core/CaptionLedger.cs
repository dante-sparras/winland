namespace Winland.Core;

/// <summary>
/// The style bits Winoma has taken off a window, and how they go back (ADR 0074). The record is one number kept on the window
/// itself, so it outlives the desktop that wrote it and goes with the window. This is what is written there and what is done with it.
/// </summary>
public static class CaptionLedger
{
    /// <summary>The name of the window property that holds the bits.</summary>
    public const string Property = "Winland.Caption";

    private const long Caption = 0x00C00000;

    /// <summary>What a window carries after a style change.</summary>
    /// <param name="known">The bits the window already carries, zero for none.</param>
    /// <param name="before">The style before the change.</param>
    /// <param name="after">The style after it.</param>
    /// <returns><paramref name="known"/> with every bit the change took off. Zero when nothing has been taken off this window.</returns>
    public static long Marked(long known, long before, long after) => known | (before & ~after);

    /// <summary>The style a window gets back.</summary>
    /// <param name="style">The window's style now.</param>
    /// <param name="bits">The bits Winoma took off it.</param>
    /// <returns><paramref name="style"/> with those bits set again.</returns>
    public static long Restored(long style, long bits) => style | bits;

    /// <summary>Whether a window gets its bits back now.</summary>
    /// <param name="style">The window's style now.</param>
    /// <param name="bits">The bits the window carries.</param>
    /// <param name="next">The style to set.</param>
    /// <returns>
    /// False when the window carries nothing, when it already has every bit, and when it has no caption at all:
    /// that window has gone fullscreen in its own app since, and a frame and buttons put on it now would show around the video.
    /// A tile with half a caption, a thin border and no title, was stripped like any other and gets its frame back.
    /// </returns>
    public static bool Returns(long style, long bits, out long next)
    {
        next = Restored(style, bits);
        return bits != 0 && (style & Caption) != 0 && next != style;
    }
}
