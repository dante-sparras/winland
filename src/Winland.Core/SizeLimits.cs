namespace Winland;

/// <summary>A Client's minimum and maximum size. Either may be missing, and a zero or unbounded side is no limit.</summary>
internal readonly record struct SizeLimits(Size? Min, Size? Max)
{
    /// <summary>
    /// The limits a tiled Client keeps when they differ from <see cref="Min"/> and <see cref="Max"/>, which a floating
    /// Client keeps. Without <c>misc:size_limits_tiled</c> a tiled Client keeps only the window's own limits, and the
    /// minimum and maximum size window rules apply to it only while it floats.
    /// </summary>
    public TiledLimits? Tiled { get; init; }

    /// <summary>The limits a tiled Client keeps.</summary>
    public SizeLimits ForTiles => Tiled is { } tiled ? new SizeLimits(tiled.Min, tiled.Max) : this;

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

    /// <summary>
    /// A tiled window's rectangle. A window cannot be smaller than its minimum, so a tile below it grows
    /// to the minimum around the tile's middle, then moves to stay inside the area as far as it fits, as
    /// the pinned release places a window with tiled size limits. It overlaps its neighbours instead of
    /// pushing them off the Monitor.
    /// </summary>
    public Rectangle Raise(Rectangle tile, Rectangle area)
    {
        if (Min is not { } lo)
            return tile;

        var width = Math.Max(tile.Width, lo.Width);
        var height = Math.Max(tile.Height, lo.Height);
        if (width == tile.Width && height == tile.Height)
            return tile;

        var x = tile.X + ((tile.Width - width) / 2);
        var y = tile.Y + ((tile.Height - height) / 2);
        x = Math.Clamp(x, area.X, Math.Max(area.X, area.X + area.Width - width));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - height));
        return new Rectangle(x, y, width, height);
    }
}

/// <summary>A tiled Client's minimum and maximum size, when they differ from its floating ones.</summary>
internal readonly record struct TiledLimits(Size? Min, Size? Max);
