namespace Winland.Domain;

/// <summary>Where a window that cannot fill the tile it would open in floats.</summary>
internal static class FloatedLimits
{
    /// <summary>The predicted size kept inside the limits, in the middle of the work area.</summary>
    public static Rectangle Centered(SizeLimits limits, Rectangle predicted, Rectangle area)
    {
        var size = limits.Clamp(new Rectangle(0, 0, predicted.Width, predicted.Height));
        return new Rectangle(
            area.X + ((area.Width - size.Width) / 2),
            area.Y + ((area.Height - size.Height) / 2),
            size.Width,
            size.Height);
    }
}
