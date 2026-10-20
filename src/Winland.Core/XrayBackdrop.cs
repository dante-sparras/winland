namespace Winland;

/// <summary>
/// The blurred wallpaper Winland draws under one translucent Client when <c>decoration:blur:xray</c> is on, as the pinned
/// release draws its precomputed blur under a window: the wallpaper alone, with no window in it. <see cref="Rounding"/>
/// and <see cref="RoundingPower"/> are the window's corners in pixels. <see cref="Alpha"/> is how opaque the picture is: 255
/// when <c>decoration:blur:ignore_opacity</c> is on, the window's own alpha when it is off.
/// </summary>
public sealed record XrayBackdrop(BlurTuning Tuning, int Rounding, float RoundingPower, byte Alpha);

/// <summary>One monitor's blurred wallpaper: opaque 0xAARRGGBB pixels, <see cref="Area"/>.Width pixels to a row, at <see cref="Area"/> on the desktop.</summary>
public sealed record BackdropImage(Rectangle Area, uint[] Pixels);

/// <summary>Cuts a window's rectangle out of the blurred wallpapers of the monitors it covers.</summary>
public static class XrayPainter
{
    /// <summary>
    /// Paints the part of the desktop that <paramref name="box"/> covers into <paramref name="pixels"/> as premultiplied
    /// BGRA, <paramref name="box"/>.Width pixels to a row. What no image covers is left clear, and the box's corners are
    /// rounded and antialiased like the window's.
    /// </summary>
    public static void Paint(Span<uint> pixels, Rectangle box, IReadOnlyList<BackdropImage> images, XrayBackdrop layer)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(layer);
        if (box.Width <= 0 || box.Height <= 0 || pixels.Length < box.Width * box.Height)
            return;

        pixels[..(box.Width * box.Height)].Clear();
        foreach (var image in images)
        {
            var left = Math.Max(box.X, image.Area.X);
            var top = Math.Max(box.Y, image.Area.Y);
            var right = Math.Min(box.X + box.Width, image.Area.X + image.Area.Width);
            var bottom = Math.Min(box.Y + box.Height, image.Area.Y + image.Area.Height);
            if (right <= left || bottom <= top)
                continue;

            for (var y = top; y < bottom; y++)
            {
                var from = ((y - image.Area.Y) * image.Area.Width) + (left - image.Area.X);
                var to = ((y - box.Y) * box.Width) + (left - box.X);
                image.Pixels.AsSpan(from, right - left).CopyTo(pixels.Slice(to, right - left));
            }
        }

        var radius = Math.Clamp(layer.Rounding, 0, Math.Min(box.Width, box.Height) / 2);
        for (var row = 0; row < box.Height; row++)
        {
            var corner = row < radius || row >= box.Height - radius;
            for (var column = 0; column < box.Width; column++)
            {
                var index = (row * box.Width) + column;
                var color = pixels[index];
                if (color == 0)
                    continue;

                var cover = layer.Alpha / 255.0;
                if (corner && (column < radius || column >= box.Width - radius))
                {
                    cover *= BorderPainter.Inside(
                        Math.Abs(column + 0.5 - (box.Width / 2.0)),
                        Math.Abs(row + 0.5 - (box.Height / 2.0)),
                        box.Width / 2.0,
                        box.Height / 2.0,
                        radius,
                        layer.RoundingPower);
                }

                pixels[index] = Scale(color, cover);
            }
        }
    }

    // The opaque pixel at this coverage, premultiplied.
    private static uint Scale(uint color, double cover)
    {
        if (cover >= 1)
            return color | 0xFF000000u;

        if (cover <= 0)
            return 0;

        return ((uint)Math.Round(cover * 255) << 24)
            | ((uint)Math.Round(((color >> 16) & 0xFF) * cover) << 16)
            | ((uint)Math.Round(((color >> 8) & 0xFF) * cover) << 8)
            | (uint)Math.Round((color & 0xFF) * cover);
    }
}
