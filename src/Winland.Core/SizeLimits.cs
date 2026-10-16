namespace Winland;

/// <summary>A Client's minimum and maximum size. Either may be missing, and a zero or unbounded side is no limit.</summary>
internal readonly record struct SizeLimits(Size? Min, Size? Max)
{
    /// <summary>The rectangle with its size kept inside the limits. Its top left corner does not move.</summary>
    public Rectangle Clamp(Rectangle bounds)
    {
        var width = bounds.Width;
        var height = bounds.Height;
        if (Max is { } hi)
        {
            if (hi.Width < int.MaxValue && width > hi.Width)
                width = hi.Width;
            if (hi.Height < int.MaxValue && height > hi.Height)
                height = hi.Height;
        }

        if (Min is { } lo)
        {
            if (lo.Width > 0 && width < lo.Width)
                width = lo.Width;
            if (lo.Height > 0 && height < lo.Height)
                height = lo.Height;
        }

        if (width == bounds.Width && height == bounds.Height)
            return bounds;

        return new Rectangle(bounds.X, bounds.Y, width, height);
    }
}
