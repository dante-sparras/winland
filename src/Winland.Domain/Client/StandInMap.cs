namespace Winland.Domain;

/// <summary>
/// How a stand-in draws a window during a motion that changes the window's size. The window has its final size
/// from the first frame, and its picture is scaled onto each frame's rectangle: stretched to it when the window
/// has painted at that size, and, while it is still smaller, scaled by the same factor the rectangle has over
/// the final size, so the picture does not stretch to fill a box it does not fill yet. A picture larger than
/// the final size is squeezed into the rectangle.
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
        var givenWidth = Math.Max(1, content.Width);
        var givenHeight = Math.Max(1, content.Height);
        var width = painted is Size outer ? Math.Max(1, outer.Width - insets.Left - insets.Right) : givenWidth;
        var height = painted is Size drawn ? Math.Max(1, drawn.Height - insets.Top - insets.Bottom) : givenHeight;

        var source = new Rectangle(insets.Left, insets.Top, width, height);
        var boxWidth = Math.Max(1, frame.Width);
        var boxHeight = Math.Max(1, frame.Height);
        var destinationWidth = boxWidth;
        var destinationHeight = boxHeight;

        // Smaller than the size it was given by more than a pixel: it has not laid itself out yet.
        if (givenWidth > width + 1 || givenHeight > height + 1)
        {
            destinationWidth = Math.Clamp(ScaleBy(width, boxWidth, givenWidth), 1, boxWidth);
            destinationHeight = Math.Clamp(ScaleBy(height, boxHeight, givenHeight), 1, boxHeight);
        }

        return new StandInMapping(source, new Rectangle(0, 0, destinationWidth, destinationHeight));
    }

    private static int ScaleBy(int size, int numerator, int denominator) =>
        (int)Math.Round((double)size * numerator / denominator, MidpointRounding.AwayFromZero);
}

/// <summary>Which part of a window's picture is drawn where, both in pixels of the stand-in's own coordinates.</summary>
public readonly record struct StandInMapping(Rectangle Source, Rectangle Destination);
