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

    public bool AppliesAt(int measuredAt, int dpi) => measuredAt == dpi;

    /// <summary>
    /// One follow-up after a placement. A window that refused the requested size keeps that size.
    /// </summary>
    public static Correction Correct(Rectangle target, Rectangle requested, Rectangle landedOuter, Rectangle landedVisible)
    {
        if (landedVisible == target)
            return new Correction(landedOuter, false);

        var refused = landedOuter.Width != requested.Width || landedOuter.Height != requested.Height;
        if (refused)
        {
            return new Correction(
                new Rectangle(
                    landedOuter.X + target.X - landedVisible.X,
                    landedOuter.Y + target.Y - landedVisible.Y,
                    landedOuter.Width,
                    landedOuter.Height),
                true);
        }

        return new Correction(
            new Rectangle(
                requested.X + target.X - landedVisible.X,
                requested.Y + target.Y - landedVisible.Y,
                requested.Width + target.Width - landedVisible.Width,
                requested.Height + target.Height - landedVisible.Height),
            false);
    }

    public readonly record struct Correction(Rectangle Outer, bool Clamped);
}
