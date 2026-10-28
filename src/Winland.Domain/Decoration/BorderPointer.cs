namespace Winland.Domain;

/// <summary>
/// Hit testing and resizing from a window border. The pointer positions are supplied by the caller.
/// </summary>
internal static class BorderPointer
{
    public static bool Contains(Rectangle box, Point point) =>
        box.Width > 0
        && box.Height > 0
        && point.X >= box.X
        && point.Y >= box.Y
        && point.X < box.X + box.Width
        && point.Y < box.Y + box.Height;

    public static int Reach(LayoutOptions options) =>
        options.ResizeOnBorder ? Math.Max(0, options.BorderSize) + options.GrabArea : 0;

    /// <summary>
    /// How far around a window a wheel turn still counts as meant for it: the border, and the grab area around it when
    /// <c>resize_on_border</c> is on, as Winland extends a window's input past its surface by its decorations.
    /// </summary>
    public static int AxisReach(LayoutOptions options) =>
        Math.Max(0, options.BorderSize) + (options.ResizeOnBorder ? options.GrabArea : 0);

    /// <summary>The point of <paramref name="box"/> nearest <paramref name="point"/>, one pixel in from the far edges.</summary>
    public static Point Clamp(Rectangle box, Point point) => new(
        Math.Clamp(point.X, box.X, Math.Max(box.X, box.X + box.Width - 1)),
        Math.Clamp(point.Y, box.Y, Math.Max(box.Y, box.Y + box.Height - 1)));

    /// <summary>Corner icon zone. The configured corner radius is not applied, so only the border and a 10 pixel leeway count.</summary>
    public static int Corner(LayoutOptions options) => Math.Max(0, options.BorderSize) + 10;

    public static bool InGrab(Rectangle window, Point pointer, int reach) =>
        reach > 0 && Contains(Expand(window, reach), pointer) && !Contains(window, pointer);

    public static BorderSide Side(Rectangle window, Point pointer, int corner)
    {
        var top = pointer.Y < window.Y + corner;
        var bottom = pointer.Y >= window.Y + window.Height - corner;
        var left = pointer.X < window.X + corner;
        var right = pointer.X >= window.X + window.Width - corner;
        if (top)
        {
            if (left)
                return BorderSide.TopLeft;

            if (right)
                return BorderSide.TopRight;

            return BorderSide.Top;
        }

        if (bottom)
        {
            if (left)
                return BorderSide.BottomLeft;

            if (right)
                return BorderSide.BottomRight;

            return BorderSide.Bottom;
        }

        if (left)
            return BorderSide.Left;

        if (right)
            return BorderSide.Right;

        return BorderSide.None;
    }

    public static string? Cursor(BorderSide side) => side switch
    {
        BorderSide.Top => "top_side",
        BorderSide.Bottom => "bottom_side",
        BorderSide.Left => "left_side",
        BorderSide.Right => "right_side",
        BorderSide.TopLeft => "top_left_corner",
        BorderSide.TopRight => "top_right_corner",
        BorderSide.BottomLeft => "bottom_left_corner",
        BorderSide.BottomRight => "bottom_right_corner",
        _ => null,
    };

    public static BorderSide GrabbedSide(bool left, bool top) =>
        (left, top) switch
        {
            (true, true) => BorderSide.TopLeft,
            (false, true) => BorderSide.TopRight,
            (false, false) => BorderSide.BottomRight,
            _ => BorderSide.BottomLeft,
        };

    public static (bool Left, bool Top) GrabbedCorner(Point pointer, Rectangle bounds) =>
        (
            pointer.X < bounds.X + (bounds.Width / 2.0),
            pointer.Y < bounds.Y + (bounds.Height / 2.0));

    /// <summary>
    /// Resizes from the grabbed corner. With <paramref name="keepRatio"/> the larger of the two
    /// proposed sides sets the size and the other follows the starting aspect ratio.
    /// </summary>
    public static Rectangle Resize(Rectangle start, Point origin, Point pointer, bool left, bool top, bool keepRatio = false)
    {
        var dx = pointer.X - origin.X;
        var dy = pointer.Y - origin.Y;
        double proposedWidth = left ? start.Width - dx : start.Width + dx;
        double proposedHeight = top ? start.Height - dy : start.Height + dy;
        if (keepRatio && start.Width >= 1 && start.Height >= 1)
        {
            var ratio = start.Height / (double)start.Width;
            if (proposedWidth * ratio > proposedHeight)
                proposedHeight = proposedWidth * ratio;
            else
                proposedWidth = proposedHeight / ratio;
        }

        var width = Math.Max(1, (int)Math.Round(proposedWidth, MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(proposedHeight, MidpointRounding.AwayFromZero));
        var x = left ? start.X + start.Width - width : start.X;
        var y = top ? start.Y + start.Height - height : start.Y;
        return new Rectangle(x, y, width, height);
    }

    private static Rectangle Expand(Rectangle box, int grab) =>
        new(box.X - grab, box.Y - grab, box.Width + (grab * 2), box.Height + (grab * 2));
}

internal enum BorderSide
{
    None,
    Top,
    Bottom,
    Left,
    Right,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}
