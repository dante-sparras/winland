namespace Winland.Domain;

/// <summary>
/// How far a floating Client is kept on screen, as Winland keeps it: in <see cref="Work"/>, the floating work
/// area, with <see cref="Border"/> drawn around the frame. <see cref="Mode"/> 0 leaves the Client where it is put. 1 moves
/// a Client that would be wholly off the area until its middle is on the nearest edge. 2 moves it, not resizing it, to lie
/// wholly inside with its border, so one larger than the area lines up with the right and bottom edges.
/// </summary>
internal readonly record struct OnScreen(Rectangle Work, int Border, int Mode)
{
    public Rectangle Keep(Rectangle frame)
    {
        if (Mode is not (1 or 2) || Work.Width <= 0 || Work.Height <= 0)
            return frame;

        var x = frame.X - Border;
        var y = frame.Y - Border;
        var width = frame.Width + (2 * Border);
        var height = frame.Height + (2 * Border);
        var right = Work.X + Work.Width;
        var bottom = Work.Y + Work.Height;
        if (Mode == 2)
        {
            x = Math.Max(x, Work.X);
            y = Math.Max(y, Work.Y);
            if (x + width > right)
                x = right - width;
            if (y + height > bottom)
                y = bottom - height;
        }
        else if (x >= right || x + width <= Work.X || y >= bottom || y + height <= Work.Y)
        {
            x = Math.Max(x, Work.X - (width / 2));
            y = Math.Max(y, Work.Y - (height / 2));
            if (x > right)
                x = right - (width / 2);
            if (y > bottom)
                y = bottom - (height / 2);
        }

        return frame with { X = x + Border, Y = y + Border };
    }
}
