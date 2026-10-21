namespace Winland.Domain;

/// <summary>
/// Visible minimum and maximum from the window's outer track sizes.
/// The layout sizes the visible frame. The outer track size includes the invisible frame.
/// </summary>
public static class TrackLimits
{
    // An outer maximum below this on one axis is no limit on that axis.
    private const int UnlimitedBelow = 5;

    public static (Size? Min, Size? Max) Visible(Size? outerMin, Size? outerMax, FrameInsets insets) =>
        (Minimum(outerMin, insets), Maximum(outerMax, insets));

    private static Size? Minimum(Size? outer, FrameInsets insets)
    {
        if (outer is not { } size)
            return null;

        var width = AxisMinimum(size.Width, insets.Left + insets.Right);
        var height = AxisMinimum(size.Height, insets.Top + insets.Bottom);
        if (width < 1 && height < 1)
            return null;

        return new Size(width, height);
    }

    private static Size? Maximum(Size? outer, FrameInsets insets)
    {
        if (outer is not { } size)
            return null;

        var width = AxisMaximum(size.Width, insets.Left + insets.Right);
        var height = AxisMaximum(size.Height, insets.Top + insets.Bottom);
        if (width is null && height is null)
            return null;

        return new Size(width ?? int.MaxValue, height ?? int.MaxValue);
    }

    private static int AxisMinimum(int outer, int inset)
    {
        if (outer < 1)
            return 0;

        var visible = (long)outer - inset;
        if (visible < 1)
            return 1;

        return visible > int.MaxValue ? int.MaxValue : (int)visible;
    }

    private static int? AxisMaximum(int outer, int inset)
    {
        if (outer < UnlimitedBelow)
            return null;

        var visible = (long)outer - inset;
        if (visible < 1)
            return 1;

        return visible > int.MaxValue ? int.MaxValue : (int)visible;
    }
}
