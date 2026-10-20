namespace Winland;

/// <summary>How Windows fits a wallpaper picture to the desktop (<c>DESKTOP_WALLPAPER_POSITION</c>).</summary>
public enum WallpaperPosition
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5,
}

/// <summary>Draws a wallpaper picture on a canvas the way Windows fits it, for the part of the canvas one monitor shows.</summary>
public static class WallpaperFit
{
    /// <summary>
    /// Renders the part of a <paramref name="canvasWidth"/> by <paramref name="canvasHeight"/> desktop that starts at
    /// <paramref name="cropX"/>, <paramref name="cropY"/> and is <paramref name="width"/> by <paramref name="height"/>. The
    /// canvas is one monitor, or all of them for <see cref="WallpaperPosition.Span"/>. What the picture leaves bare is
    /// <paramref name="background"/>. A null picture is the background alone. The result is opaque 0xAARRGGBB.
    /// </summary>
    public static uint[] Render(
        ReadOnlySpan<uint> image,
        int imageWidth,
        int imageHeight,
        WallpaperPosition position,
        uint background,
        int canvasWidth,
        int canvasHeight,
        int cropX,
        int cropY,
        int width,
        int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "The monitor has no pixels.");

        var result = new uint[width * height];
        var solid = 0xFF000000u | background;
        if (image.IsEmpty || imageWidth <= 0 || imageHeight <= 0 || image.Length < (long)imageWidth * imageHeight || canvasWidth <= 0 || canvasHeight <= 0)
        {
            Array.Fill(result, solid);
            return result;
        }

        // The picture's place on the canvas: its top left corner and how many canvas pixels one picture pixel covers.
        double scaleX;
        double scaleY;
        switch (position)
        {
            case WallpaperPosition.Stretch:
                scaleX = (double)canvasWidth / imageWidth;
                scaleY = (double)canvasHeight / imageHeight;
                break;
            case WallpaperPosition.Fit:
                scaleX = scaleY = Math.Min((double)canvasWidth / imageWidth, (double)canvasHeight / imageHeight);
                break;
            case WallpaperPosition.Fill:
            case WallpaperPosition.Span:
                scaleX = scaleY = Math.Max((double)canvasWidth / imageWidth, (double)canvasHeight / imageHeight);
                break;
            default:
                scaleX = scaleY = 1;
                break;
        }

        var tile = position == WallpaperPosition.Tile;
        var left = tile ? 0 : (canvasWidth - (imageWidth * scaleX)) / 2;
        var top = tile ? 0 : (canvasHeight - (imageHeight * scaleY)) / 2;
        for (var row = 0; row < height; row++)
        {
            var py = cropY + row + 0.5 - top;
            for (var column = 0; column < width; column++)
            {
                var px = cropX + column + 0.5 - left;
                var u = px / scaleX;
                var v = py / scaleY;
                if (tile)
                {
                    u = Mod(u, imageWidth);
                    v = Mod(v, imageHeight);
                }
                else if (u < 0 || v < 0 || u >= imageWidth || v >= imageHeight)
                {
                    result[(row * width) + column] = solid;
                    continue;
                }

                result[(row * width) + column] = scaleX == 1 && scaleY == 1
                    ? image[((int)v * imageWidth) + (int)u] | 0xFF000000u
                    : Sample(image, imageWidth, imageHeight, u, v);
            }
        }

        return result;
    }

    private static double Mod(double value, int length)
    {
        var rest = value % length;
        return rest < 0 ? rest + length : rest;
    }

    // Linear filtering at (u, v) in picture pixels, with pixel centers at .5.
    private static uint Sample(ReadOnlySpan<uint> image, int width, int height, double u, double v)
    {
        var fx = u - 0.5;
        var fy = v - 0.5;
        var x0 = (int)Math.Floor(fx);
        var y0 = (int)Math.Floor(fy);
        var tx = fx - x0;
        var ty = fy - y0;
        var xa = Math.Clamp(x0, 0, width - 1);
        var xb = Math.Clamp(x0 + 1, 0, width - 1);
        var ya = Math.Clamp(y0, 0, height - 1);
        var yb = Math.Clamp(y0 + 1, 0, height - 1);
        var p00 = image[(ya * width) + xa];
        var p10 = image[(ya * width) + xb];
        var p01 = image[(yb * width) + xa];
        var p11 = image[(yb * width) + xb];
        uint packed = 0xFF000000u;
        for (var shift = 0; shift <= 16; shift += 8)
        {
            var top = (((p00 >> shift) & 0xFF) * (1 - tx)) + (((p10 >> shift) & 0xFF) * tx);
            var bottom = (((p01 >> shift) & 0xFF) * (1 - tx)) + (((p11 >> shift) & 0xFF) * tx);
            packed |= (uint)Math.Clamp(Math.Round((top * (1 - ty)) + (bottom * ty)), 0, 255) << shift;
        }

        return packed;
    }
}
