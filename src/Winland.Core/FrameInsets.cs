namespace Winland;

/// <summary>
/// How far a window's outer rectangle extends past its visible frame, in physical pixels.
/// Placement moves the outer rectangle. The Layout sizes the visible frame.
/// </summary>
public readonly record struct FrameInsets(int Left, int Top, int Right, int Bottom)
{
    public static FrameInsets None { get; } = new(0, 0, 0, 0);

    public static FrameInsets Between(Rectangle outer, Rectangle visible) =>
        new(
            visible.X - outer.X,
            visible.Y - outer.Y,
            outer.X + outer.Width - (visible.X + visible.Width),
            outer.Y + outer.Height - (visible.Y + visible.Height));

    public Rectangle OuterFor(Rectangle visible) =>
        new(
            visible.X - Left,
            visible.Y - Top,
            visible.Width + Left + Right,
            visible.Height + Top + Bottom);

    public static bool AppliesAt(int measuredAt, int dpi) => measuredAt == dpi;

    /// <summary>
    /// One follow-up after a placement. Each axis is corrected on its own: an axis the window refused keeps
    /// the size it landed at, and an axis it accepted grows by the inset the visible frame is short.
    /// </summary>
    public static Correction Correct(Rectangle target, Rectangle requested, Rectangle landedOuter, Rectangle landedVisible)
    {
        if (landedVisible == target)
            return new Correction(landedOuter, false);

        var (x, width, refusedWidth) = Axis(target.X, target.Width, requested.X, requested.Width, landedOuter.X, landedOuter.Width, landedVisible.X, landedVisible.Width);
        var (y, height, refusedHeight) = Axis(target.Y, target.Height, requested.Y, requested.Height, landedOuter.Y, landedOuter.Height, landedVisible.Y, landedVisible.Height);
        return new Correction(new Rectangle(x, y, width, height), refusedWidth || refusedHeight);
    }

    private static (int Start, int Size, bool Refused) Axis(
        int target,
        int targetSize,
        int requested,
        int requestedSize,
        int landed,
        int landedSize,
        int visible,
        int visibleSize)
    {
        if (landedSize != requestedSize)
            return (landed + target - visible, landedSize, true);

        return (requested + target - visible, requestedSize + targetSize - visibleSize, false);
    }

    public readonly record struct Correction(Rectangle Outer, bool Clamped);
}
