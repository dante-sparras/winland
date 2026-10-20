namespace Winland;

/// <summary>
/// The blur of what is behind one translucent Client: the same tuning as the wallpaper blur, the window's corners in pixels,
/// and how opaque the picture is. <see cref="Sources"/> are the Clients behind it, from the bottom of the stack to the top.
/// <see cref="Alpha"/> is 255 when <c>decoration:blur:ignore_opacity</c> is on, and the window's own alpha when it is off.
/// </summary>
public sealed record BehindBlur(BlurTuning Tuning, int Rounding, float RoundingPower, byte Alpha, IReadOnlyList<WindowId> Sources);

/// <summary>
/// One monitor-sized blur drawn while a special workspace is open. <see cref="Anchor"/> is the lowest visible Client on that
/// workspace, and null when the workspace has none: the layer then sits at the top of the ordinary windows. <see cref="Area"/>
/// is the monitor, in physical pixels.
/// </summary>
public sealed record SpecialBlur(MonitorId Monitor, Rectangle Area, WindowId? Anchor, BehindBlur Blur);

/// <summary>A Client that can be captured into a blur, and the rectangle it occupies.</summary>
public readonly record struct BehindCandidate(WindowId Window, Rectangle Bounds);

/// <summary>
/// Where one captured window is drawn in a blur's picture, in pixels of that picture, and the part of its texture that lands
/// there, from 0 to 1. The whole texture is the whole window.
/// </summary>
public readonly record struct BlurQuad(float X, float Y, float Width, float Height, float U0, float V0, float U1, float V1);

/// <summary>Which windows a blur of what is behind a Client includes, and the rectangle the filter needs around it.</summary>
public static class BehindBlurPlan
{
    /// <summary>The longest side a blur picture is allowed. Past this the margin is dropped and the window's own rectangle is blurred.</summary>
    public const int MaxSide = 4096;

    /// <summary>
    /// The Clients in <paramref name="visible"/> whose bounds meet <paramref name="area"/>, from the bottom of
    /// <paramref name="stacking"/> to the top. A Client the stack has not listed yet was opened on top, so it follows the
    /// others. A window in <paramref name="skip"/> is left out.
    /// </summary>
    public static IReadOnlyList<WindowId> Sources(
        IReadOnlyList<WindowId> stacking,
        IReadOnlyList<BehindCandidate> visible,
        Rectangle area,
        IReadOnlySet<WindowId>? skip = null)
    {
        ArgumentNullException.ThrowIfNull(stacking);
        ArgumentNullException.ThrowIfNull(visible);
        var bounds = new Dictionary<WindowId, Rectangle>(visible.Count);
        foreach (var candidate in visible)
            bounds[candidate.Window] = candidate.Bounds;

        var sources = new List<WindowId>();
        var seen = new HashSet<WindowId>();
        foreach (var window in stacking)
        {
            if (!bounds.TryGetValue(window, out var rectangle))
                continue;

            seen.Add(window);
            if (skip is not null && skip.Contains(window))
                continue;

            if (Hits(rectangle, area))
                sources.Add(window);
        }

        foreach (var candidate in visible)
        {
            if (!seen.Add(candidate.Window))
                continue;

            if (skip is not null && skip.Contains(candidate.Window))
                continue;

            if (Hits(candidate.Bounds, area))
                sources.Add(candidate.Window);
        }

        return sources;
    }

    /// <summary>
    /// <paramref name="area"/> grown by <paramref name="spread"/> on every side, so a filter tap at the edge still sees a
    /// pixel. The original rectangle is kept when the spread is not positive or a grown side would pass <see cref="MaxSide"/>.
    /// </summary>
    public static Rectangle Cover(Rectangle area, int spread)
    {
        if (spread <= 0 || area.Width <= 0 || area.Height <= 0)
            return area;

        var width = area.Width + (spread * 2L);
        var height = area.Height + (spread * 2L);
        if (width > MaxSide || height > MaxSide)
            return area;

        return new Rectangle(area.X - spread, area.Y - spread, (int)width, (int)height);
    }

    /// <summary>
    /// The part of <paramref name="window"/> that lies in <paramref name="target"/>, as a quad in <paramref name="target"/>'s
    /// pixels and as texture coordinates of the whole window. False when the two do not meet.
    /// </summary>
    public static bool TryQuad(Rectangle target, Rectangle window, out BlurQuad quad)
    {
        quad = default;
        if (window.Width <= 0 || window.Height <= 0 || target.Width <= 0 || target.Height <= 0)
            return false;

        var left = Math.Max(target.X, window.X);
        var top = Math.Max(target.Y, window.Y);
        var right = Math.Min(target.X + target.Width, window.X + window.Width);
        var bottom = Math.Min(target.Y + target.Height, window.Y + window.Height);
        if (right <= left || bottom <= top)
            return false;

        quad = new BlurQuad(
            left - target.X,
            top - target.Y,
            right - left,
            bottom - top,
            (float)(left - window.X) / window.Width,
            (float)(top - window.Y) / window.Height,
            (float)(right - window.X) / window.Width,
            (float)(bottom - window.Y) / window.Height);
        return true;
    }

    private static bool Hits(Rectangle one, Rectangle other) =>
        one.Width > 0 && one.Height > 0 && other.Width > 0 && other.Height > 0
        && one.X < other.X + other.Width && other.X < one.X + one.Width
        && one.Y < other.Y + other.Height && other.Y < one.Y + one.Height;
}

/// <summary>
/// Lays pictures over each other, back to front, into the rectangle a blur then reads. A clear pixel is skipped, an opaque
/// pixel replaces what is there, and any other pixel is mixed with straight alpha. The result is opaque.
/// </summary>
public static class BehindBlurComposer
{
    /// <summary>
    /// Clears <paramref name="pixels"/>, <paramref name="box"/>.Width pixels to a row, and paints <paramref name="layers"/>
    /// over it. A layer is in desktop pixels, the same space as <paramref name="box"/>.
    /// </summary>
    public static void Compose(Span<uint> pixels, Rectangle box, IReadOnlyList<BackdropImage> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (box.Width <= 0 || box.Height <= 0 || pixels.Length < (long)box.Width * box.Height)
            return;

        pixels[..(box.Width * box.Height)].Clear();
        foreach (var layer in layers)
        {
            var left = Math.Max(box.X, layer.Area.X);
            var top = Math.Max(box.Y, layer.Area.Y);
            var right = Math.Min(box.X + box.Width, layer.Area.X + layer.Area.Width);
            var bottom = Math.Min(box.Y + box.Height, layer.Area.Y + layer.Area.Height);
            if (right <= left || bottom <= top || layer.Pixels.Length < (long)layer.Area.Width * layer.Area.Height)
                continue;

            for (var y = top; y < bottom; y++)
            {
                var from = ((y - layer.Area.Y) * layer.Area.Width) + (left - layer.Area.X);
                var to = ((y - box.Y) * box.Width) + (left - box.X);
                for (var x = 0; x < right - left; x++)
                    pixels[to + x] = Over(pixels[to + x], layer.Pixels[from + x]);
            }
        }
    }

    /// <summary>One pixel of <paramref name="over"/> laid on <paramref name="under"/> with straight alpha.</summary>
    public static uint Over(uint under, uint over)
    {
        var alpha = (over >> 24) & 0xFF;
        if (alpha == 0)
            return under;

        if (alpha == 255)
            return over | 0xFF000000u;

        var red = Mix((over >> 16) & 0xFF, (under >> 16) & 0xFF, alpha);
        var green = Mix((over >> 8) & 0xFF, (under >> 8) & 0xFF, alpha);
        var blue = Mix(over & 0xFF, under & 0xFF, alpha);
        return 0xFF000000u | (red << 16) | (green << 8) | blue;
    }

    private static uint Mix(uint source, uint destination, uint alpha) =>
        (uint)((source * alpha) + (destination * (255 - alpha)) + 127) / 255;
}
