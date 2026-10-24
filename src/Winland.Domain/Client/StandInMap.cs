namespace Winland.Domain;

/// <summary>
/// How a stand-in draws a window during a motion that changes the window's size. The window has its final size
/// from the first frame, and what it has painted is stretched over each frame's rectangle. A window that has not
/// painted its new size yet shows what it painted before, a little stretched, rather than a box it does not fill.
/// </summary>
public static class StandInMap
{
    /// <summary>
    /// The mapping for one frame.
    /// </summary>
    /// <param name="frame">The rectangle the motion shows now, the visible frame.</param>
    /// <param name="content">The size the window was given for the whole motion.</param>
    /// <param name="insets">How far the window's outer rectangle extends past its visible frame.</param>
    /// <param name="painted">The size of the window's outer rectangle as it is painted now. Null is the size it was given.</param>
    public static StandInMapping For(Rectangle frame, Size content, FrameInsets insets, Size? painted)
    {
        var width = painted is Size outer ? Math.Max(1, outer.Width - insets.Left - insets.Right) : Math.Max(1, content.Width);
        var height = painted is Size drawn ? Math.Max(1, drawn.Height - insets.Top - insets.Bottom) : Math.Max(1, content.Height);
        var source = new Rectangle(insets.Left, insets.Top, width, height);
        return new StandInMapping(source, new Rectangle(0, 0, Math.Max(1, frame.Width), Math.Max(1, frame.Height)));
    }
}

/// <summary>Which part of a window's picture is drawn where, both in pixels of the stand-in's own coordinates.</summary>
public readonly record struct StandInMapping(Rectangle Source, Rectangle Destination);
