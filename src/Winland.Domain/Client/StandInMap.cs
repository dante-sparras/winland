namespace Winland.Domain;

/// <summary>
/// How a stand-in draws a window during a motion that changes the window's size. The window has the size it rests at
/// during the motion from the first frame. Its picture is scaled by the same amount across and down, so it is never
/// distorted, and as little as covers the frame. What then does not fit is cut off on the right and bottom, the way a
/// window shows when its edge is dragged. A window that has not painted its new size yet shows what it painted
/// before at the same scale, and the area it gained repeats a column and a row just inside its border, so no gap shows.
/// </summary>
public static class StandInMap
{
    // How far in from a window's edge its picture is repeated: its outermost pixels are its border.
    private const int EdgeInset = 4;

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
        var frameWidth = Math.Max(1, frame.Width);
        var frameHeight = Math.Max(1, frame.Height);

        // The scale is taken from the size the window was given, or the size it painted when that is larger, so a
        // window that has not painted its new size yet is not zoomed.
        var scale = Math.Max((double)frameWidth / Math.Max(width, content.Width), (double)frameHeight / Math.Max(height, content.Height));
        var shownWidth = Math.Clamp((int)Math.Round(frameWidth / scale), 1, width);
        var shownHeight = Math.Clamp((int)Math.Round(frameHeight / scale), 1, height);

        // A picture that does not reach the frame's edge leaves its border off, so the border does not show inside.
        if (shownWidth * scale < frameWidth - 0.5)
            shownWidth = Math.Max(1, shownWidth - EdgeInset);
        if (shownHeight * scale < frameHeight - 0.5)
            shownHeight = Math.Max(1, shownHeight - EdgeInset);

        var coveredWidth = Math.Clamp((int)Math.Round(shownWidth * scale), 1, frameWidth);
        var coveredHeight = Math.Clamp((int)Math.Round(shownHeight * scale), 1, frameHeight);

        var picture = new StandInPiece(
            new Rectangle(insets.Left, insets.Top, shownWidth, shownHeight),
            new Rectangle(0, 0, coveredWidth, coveredHeight));

        // The right strip runs the full height, so it fills the corner too.
        StandInPiece? right = coveredWidth < frameWidth
            ? new StandInPiece(
                new Rectangle(insets.Left + shownWidth - 1, insets.Top, 1, shownHeight),
                new Rectangle(coveredWidth, 0, frameWidth - coveredWidth, frameHeight))
            : null;
        StandInPiece? bottom = coveredHeight < frameHeight
            ? new StandInPiece(
                new Rectangle(insets.Left, insets.Top + shownHeight - 1, shownWidth, 1),
                new Rectangle(0, coveredHeight, coveredWidth, frameHeight - coveredHeight))
            : null;

        return new StandInMapping(picture, right, bottom);
    }
}

/// <summary>
/// Which parts of a window's picture are drawn where, in pixels of the stand-in's own coordinates. <see cref="Right"/>
/// and <see cref="Bottom"/> fill the area a window gained before it has painted it.
/// </summary>
public readonly record struct StandInMapping(StandInPiece Picture, StandInPiece? Right = null, StandInPiece? Bottom = null)
{
    /// <summary>The part of the picture that is drawn.</summary>
    public Rectangle Source => Picture.Source;

    /// <summary>Where the picture is drawn.</summary>
    public Rectangle Destination => Picture.Destination;
}

/// <summary>One part of a window's picture, <see cref="Source"/>, drawn stretched over <see cref="Destination"/>.</summary>
public readonly record struct StandInPiece(Rectangle Source, Rectangle Destination);
