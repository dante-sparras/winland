namespace Winland;

/// <summary>
/// The blur options Winland's dual Kawase blur reads: <c>decoration:blur:size</c>, <c>passes</c>, <c>noise</c>,
/// <c>contrast</c>, <c>brightness</c>, <c>vibrancy</c> and <c>vibrancy_darkness</c>. The defaults are the release's.
/// </summary>
public sealed record BlurTuning
{
    public static BlurTuning Documented { get; } = new();

    /// <summary>The distance of the filter taps, in pixels. 0 to 100.</summary>
    public int Size { get; init; } = 8;

    /// <summary>How many times the picture is halved and doubled again. 0 to 10, used as 1 to 8.</summary>
    public int Passes { get; init; } = 1;

    public float Noise { get; init; } = 0.0117f;

    public float Contrast { get; init; } = 0.8916f;

    public float Brightness { get; init; } = 1f;

    public float Vibrancy { get; init; } = 0.1696f;

    public float VibrancyDarkness { get; init; }
}

/// <summary>
/// Winland's blur on the CPU, for a picture that does not change often: the contrast and brightness
/// preparation (<c>blurprepare</c>), the dual Kawase down passes (<c>blur1</c>, with vibrancy) and up passes
/// (<c>blur2</c>), and the noise and brightness finish (<c>blurFinish</c>). The picture is opaque 0xAARRGGBB pixels. The
/// intermediate pictures are 8 bits a channel in the release's framebuffers, so the prepared picture is rounded to 8 bits.
/// </summary>
public static class BlurPipeline
{
    // See http://alienryderflex.com/hsp.html
    private const float Pr = 0.299f;
    private const float Pg = 0.587f;
    private const float Pb = 0.114f;

    // Determines if high brightness or high saturation is more important.
    private const float A = 0.93f;
    private const float B = 0.11f;
    private const float C = 0.66f;

    private static readonly float CosA = MathF.Cos(A);
    private static readonly float SinA = MathF.Sin(A);

    /// <summary>The most passes the release runs.</summary>
    public const int MaxPasses = 8;

    /// <summary>Blurs a <paramref name="width"/> by <paramref name="height"/> picture. The result has the same size and is opaque.</summary>
    public static uint[] Blur(ReadOnlySpan<uint> source, int width, int height, BlurTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        if (width <= 0 || height <= 0 || source.Length < (long)width * height)
            throw new ArgumentException("The picture has no pixels.", nameof(source));

        var passes = Math.Clamp(tuning.Passes, 1, MaxPasses);
        var radius = Math.Max(0, tuning.Size);
        var contrast = float.IsFinite(tuning.Contrast) ? tuning.Contrast : 1f;
        var brightness = float.IsFinite(tuning.Brightness) ? tuning.Brightness : 1f;

        var levels = new Level[passes + 1];
        levels[0] = Prepare(source, width, height, contrast, brightness);
        for (var i = 1; i <= passes; i++)
            levels[i] = Down(levels[i - 1], radius, passes, tuning.Vibrancy, tuning.VibrancyDarkness);

        for (var i = passes - 1; i >= 0; i--)
        {
            levels[i] = Up(levels[i + 1], levels[i].Width, levels[i].Height, radius);
            levels[i + 1] = default;
        }

        return Finish(levels[0], tuning.Noise, brightness);
    }

    /// <summary>The release's <c>gain</c>: a curve through 0, 0.5 and 1 that lowers contrast below 1 and raises it above.</summary>
    public static float Gain(float value, float k)
    {
        var x = Math.Clamp(value, 0f, 1f);
        var t = x >= 0.5f ? 1f : 0f;
        var y = t > 0 ? 1f - x : x;
        var a = 0.5f * MathF.Pow(2f * y, k);
        return t > 0 ? 1f - a : a;
    }

    // Contrast, then brightness above 1, of each of the 256 values a channel can have.
    private static Level Prepare(ReadOnlySpan<uint> source, int width, int height, float contrast, float brightness)
    {
        var table = new float[256];
        var lift = Math.Max(1f, brightness);
        for (var i = 0; i < table.Length; i++)
        {
            var value = i / 255f;
            if (contrast != 1f)
                value = Gain(value, contrast);

            value *= lift;
            table[i] = MathF.Round(Math.Clamp(value, 0f, 1f) * 255f) / 255f;
        }

        var data = new float[width * height * 3];
        for (var i = 0; i < width * height; i++)
        {
            var pixel = source[i];
            data[i * 3] = table[(pixel >> 16) & 0xFF];
            data[(i * 3) + 1] = table[(pixel >> 8) & 0xFF];
            data[(i * 3) + 2] = table[pixel & 0xFF];
        }

        return new Level(data, width, height);
    }

    // blur1: each pixel takes the middle of its two by two block four times and the four corners of a square
    // `radius` pixels out once each, then its saturation is raised.
    private static Level Down(Level source, int radius, int passes, float vibrancy, float darkness)
    {
        var width = (source.Width + 1) / 2;
        var height = (source.Height + 1) / 2;
        var data = new float[width * height * 3];
        Parallel.For(0, height, row =>
        {
            Span<float> sum = stackalloc float[3];
            for (var column = 0; column < width; column++)
            {
                var x = (2 * column) + 1f;
                var y = (2 * row) + 1f;
                sum.Clear();
                source.Add(sum, x, y, 4f);
                source.Add(sum, x - radius, y - radius, 1f);
                source.Add(sum, x + radius, y + radius, 1f);
                source.Add(sum, x + radius, y - radius, 1f);
                source.Add(sum, x - radius, y + radius, 1f);

                var r = sum[0] / 8f;
                var g = sum[1] / 8f;
                var b = sum[2] / 8f;
                if (vibrancy != 0f)
                    (r, g, b) = Vibrance(r, g, b, vibrancy, darkness, passes);

                var at = ((row * width) + column) * 3;
                data[at] = r;
                data[at + 1] = g;
                data[at + 2] = b;
            }
        });

        return new Level(data, width, height);
    }

    // blur2: eight taps round the pixel, weighted 1 on the axes `radius / 2` out and 2 on the diagonals `radius / 4` out.
    private static Level Up(Level source, int width, int height, int radius)
    {
        var data = new float[width * height * 3];
        var near = radius / 4f;
        var far = radius / 2f;
        Parallel.For(0, height, row =>
        {
            Span<float> sum = stackalloc float[3];
            for (var column = 0; column < width; column++)
            {
                var x = (column + 0.5f) / 2f;
                var y = (row + 0.5f) / 2f;
                sum.Clear();
                source.Add(sum, x - far, y, 1f);
                source.Add(sum, x - near, y + near, 2f);
                source.Add(sum, x, y + far, 1f);
                source.Add(sum, x + near, y + near, 2f);
                source.Add(sum, x + far, y, 1f);
                source.Add(sum, x + near, y - near, 2f);
                source.Add(sum, x, y - far, 1f);
                source.Add(sum, x - near, y - near, 2f);

                var at = ((row * width) + column) * 3;
                data[at] = sum[0] / 12f;
                data[at + 1] = sum[1] / 12f;
                data[at + 2] = sum[2] / 12f;
            }
        });

        return new Level(data, width, height);
    }

    // blurFinish: noise from a hash of the pixel's place in the picture, then brightness below 1.
    private static uint[] Finish(Level level, float noise, float brightness)
    {
        var width = level.Width;
        var height = level.Height;
        var scale = Math.Min(1f, brightness);
        var result = new uint[width * height];
        Parallel.For(0, height, row =>
        {
            for (var column = 0; column < width; column++)
            {
                var amount = (Hash((column + 0.5f) / width, (row + 0.5f) / height) - 0.5f) * noise;
                var at = ((row * width) + column) * 3;
                result[(row * width) + column] = 0xFF000000u
                    | (Byte((level.Data[at] + amount) * scale) << 16)
                    | (Byte((level.Data[at + 1] + amount) * scale) << 8)
                    | Byte((level.Data[at + 2] + amount) * scale);
            }
        });

        return result;
    }

    /// <summary>The release's <c>hash</c>: a value from 0 to 1 for a place in the picture, in 0 to 1 on each axis.</summary>
    public static float Hash(float x, float y)
    {
        var p0 = Fract(x * 1689.1984f);
        var p1 = Fract(y * 1689.1984f);
        var p2 = p0;

        // p3 += dot(p3, p3.yzx + 33.33)
        var dot = (p0 * (p1 + 33.33f)) + (p1 * (p2 + 33.33f)) + (p2 * (p0 + 33.33f));
        p0 += dot;
        p1 += dot;
        p2 += dot;
        return Fract((p0 + p1) * p2);
    }

    private static float Fract(float value) => value - MathF.Floor(value);

    private static uint Byte(float value) => (uint)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);

    // The release's vibrancy: the saturation is raised by an amount that depends on how saturated and how bright the
    // color is, shared out over the passes.
    internal static (float R, float G, float B) Vibrance(float r, float g, float b, float vibrancy, float darkness, int passes)
    {
        var inverted = 1f - darkness;
        var (hue, saturation, lightness) = ToHsl(r, g, b);
        var brightness = DoubleCircleSigmoid(MathF.Sqrt((r * r * Pr) + (g * g * Pg) + (b * b * Pb)), 0.8f * inverted);
        var b1 = B * inverted;
        var boost = saturation > 0f
            ? SmoothStep(
                b1 - (C * 0.5f),
                b1 + (C * 0.5f),
                1f - (MathF.Pow(1f - (saturation * CosA), 2f) + MathF.Pow(1f - (brightness * SinA), 2f)))
            : 0f;
        var boosted = Math.Clamp(saturation + (boost * vibrancy / passes), 0f, 1f);
        return FromHsl(hue, boosted, lightness);
    }

    // http://www.flong.com/archive/texts/code/shapers_circ/
    private static float DoubleCircleSigmoid(float x, float a)
    {
        a = Math.Clamp(a, 0f, 1f);
        return x <= a
            ? a - MathF.Sqrt(Math.Max(0f, (a * a) - (x * x)))
            : a + MathF.Sqrt(Math.Max(0f, MathF.Pow(1f - a, 2f) - MathF.Pow(x - 1f, 2f)));
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static (float Hue, float Saturation, float Lightness) ToHsl(float r, float g, float b)
    {
        var min = Math.Min(r, Math.Min(g, b));
        var max = Math.Max(r, Math.Max(g, b));
        var delta = max - min;
        var lightness = (min + max) * 0.5f;
        var saturation = 0f;
        var hue = 0f;
        if (lightness > 0f && lightness < 1f)
        {
            var mul = lightness < 0.5f ? lightness : 1f - lightness;
            saturation = delta / (mul * 2f);
        }

        if (delta > 0f)
        {
            // A component counts when it is the largest and the next one (green, blue, red) is not as large.
            var sum = 0f;
            if (r == max && max != g)
                sum += (g - b) / delta;
            if (g == max && max != b)
                sum += 2f + ((b - r) / delta);
            if (b == max && max != r)
                sum += 4f + ((r - g) / delta);

            hue = sum / 6f;
            if (hue < 0f)
                hue += 1f;
        }

        return (hue, saturation, lightness);
    }

    private static (float R, float G, float B) FromHsl(float hue, float saturation, float lightness)
    {
        const float third = 1f / 3f;
        const float twoThirds = 2f / 3f;
        float r;
        float g;
        float b;
        if (hue < third)
        {
            r = 6f * (third - hue);
            g = 6f * hue;
            b = 0f;
        }
        else if (hue < twoThirds)
        {
            r = 0f;
            g = 6f * (twoThirds - hue);
            b = 6f * (hue - third);
        }
        else
        {
            r = 6f * (hue - twoThirds);
            g = 0f;
            b = 6f * (1f - hue);
        }

        r = Math.Min(r, 1f);
        g = Math.Min(g, 1f);
        b = Math.Min(b, 1f);

        var twiceSaturation = 2f * saturation;
        var saturationInverse = 1f - saturation;
        var lightnessInverse = 1f - lightness;
        var lightnessTwice = (2f * lightness) - 1f;
        float Channel(float x)
        {
            var ct = (twiceSaturation * x) + saturationInverse;
            return lightness >= 0.5f ? (lightnessInverse * ct) + lightnessTwice : lightness * ct;
        }

        return (Channel(r), Channel(g), Channel(b));
    }

    // One picture, three floats a pixel, read with linear filtering and the edge pixels repeated beyond the edge.
    private readonly record struct Level(float[] Data, int Width, int Height)
    {
        // Adds `weight` times the color at (x, y), in pixels from the top left corner, with pixel centers at .5.
        internal void Add(Span<float> sum, float x, float y, float weight)
        {
            var fx = x - 0.5f;
            var fy = y - 0.5f;
            var x0 = (int)MathF.Floor(fx);
            var y0 = (int)MathF.Floor(fy);
            var tx = fx - x0;
            var ty = fy - y0;
            var xa = Math.Clamp(x0, 0, Width - 1);
            var xb = Math.Clamp(x0 + 1, 0, Width - 1);
            var ya = Math.Clamp(y0, 0, Height - 1);
            var yb = Math.Clamp(y0 + 1, 0, Height - 1);
            var w00 = (1f - tx) * (1f - ty) * weight;
            var w10 = tx * (1f - ty) * weight;
            var w01 = (1f - tx) * ty * weight;
            var w11 = tx * ty * weight;
            var i00 = ((ya * Width) + xa) * 3;
            var i10 = ((ya * Width) + xb) * 3;
            var i01 = ((yb * Width) + xa) * 3;
            var i11 = ((yb * Width) + xb) * 3;
            for (var c = 0; c < 3; c++)
                sum[c] += (Data[i00 + c] * w00) + (Data[i10 + c] * w10) + (Data[i01 + c] * w01) + (Data[i11 + c] * w11);
        }
    }
}
