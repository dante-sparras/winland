namespace Winland.Domain;

/// <summary>
/// How far a floating Client is kept on screen, as Winland keeps it: in <see cref="Work"/>, the floating work
/// area, with <see cref="Border"/> drawn around the frame. A Client is moved, not resized, to lie wholly inside with its
/// border, so one larger than the area lines up with the right and bottom edges.
/// </summary>
internal readonly record struct OnScreen(Rectangle Work, int Border)
{
    public Rectangle Keep(Rectangle frame)
    {
        if (Work.Width <= 0 || Work.Height <= 0)
            return frame;

        var x = frame.X - Border;
        var y = frame.Y - Border;
        var width = frame.Width + (2 * Border);
        var height = frame.Height + (2 * Border);
        var right = Work.X + Work.Width;
        var bottom = Work.Y + Work.Height;
        x = Math.Max(x, Work.X);
        y = Math.Max(y, Work.Y);
        if (x + width > right)
            x = right - width;
        if (y + height > bottom)
            y = bottom - height;

        return frame with { X = x + Border, Y = y + Border };
    }
}
