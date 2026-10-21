namespace Winland.Windows;

/// <summary>
/// Winland's dual Kawase blur on the shared Direct3D device: prepare, down passes with vibrancy, up passes, and
/// the noise finish. The intermediate pictures are 32-bit floats so they follow <see cref="BlurPipeline"/>. The caller holds
/// the device gate, except <see cref="GpuPictureBlur"/>, which takes it.
/// </summary>
internal sealed unsafe class GpuKawase : IDisposable
{
    internal const int FloatFormat = 2;

    private const string Shader = """
        cbuffer C : register(b0)
        {
            float radius;
            float contrast;
            float prepareBrightness;
            float finishBrightness;
            float noise;
            float vibrancy;
            float vibrancyDarkness;
            float passes;
            float sourceW;
            float sourceH;
            float destW;
            float destH;
            float pad0;
            float pad1;
            float pad2;
            float pad3;
        };
        Texture2D tex : register(t0);
        SamplerState lin : register(s0);

        struct VO { float4 p : SV_Position; };
        VO vsFull(uint id : SV_VertexID)
        {
            VO o;
            float2 corner = float2(float((id << 1) & 2), float(id & 2));
            o.p = float4(corner * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }

        float gain(float value, float k)
        {
            float x = saturate(value);
            float t = x >= 0.5 ? 1.0 : 0.0;
            float y = t > 0.0 ? 1.0 - x : x;
            float a = 0.5 * pow(2.0 * y, k);
            return t > 0.0 ? 1.0 - a : a;
        }

        float prepareChannel(float value)
        {
            if (contrast != 1.0)
                value = gain(value, contrast);
            value *= prepareBrightness;
            return round(saturate(value) * 255.0) / 255.0;
        }

        float4 psPrepare(float4 pos : SV_Position) : SV_Target
        {
            float4 pixel = tex.Load(int3((int)pos.x, (int)pos.y, 0));
            return float4(prepareChannel(pixel.r), prepareChannel(pixel.g), prepareChannel(pixel.b), 1);
        }

        float hash(float x, float y)
        {
            float p0 = frac(x * 1689.1984);
            float p1 = frac(y * 1689.1984);
            float p2 = p0;
            float d = (p0 * (p1 + 33.33)) + (p1 * (p2 + 33.33)) + (p2 * (p0 + 33.33));
            p0 += d;
            p1 += d;
            p2 += d;
            return frac((p0 + p1) * p2);
        }

        float doubleCircle(float x, float a)
        {
            a = saturate(a);
            if (x <= a)
                return a - sqrt(max(0.0, (a * a) - (x * x)));
            return a + sqrt(max(0.0, pow(1.0 - a, 2.0) - pow(x - 1.0, 2.0)));
        }

        float hslChannel(float x, float twiceSaturation, float saturationInverse, float lightness, float lightnessInverse, float lightnessTwice)
        {
            float ct = (twiceSaturation * x) + saturationInverse;
            return lightness >= 0.5 ? (lightnessInverse * ct) + lightnessTwice : lightness * ct;
        }

        float3 fromHsl(float hue, float saturation, float lightness)
        {
            float third = 1.0 / 3.0;
            float twoThirds = 2.0 / 3.0;
            float r;
            float g;
            float b;
            if (hue < third)
            {
                r = 6.0 * (third - hue);
                g = 6.0 * hue;
                b = 0.0;
            }
            else if (hue < twoThirds)
            {
                r = 0.0;
                g = 6.0 * (twoThirds - hue);
                b = 6.0 * (hue - third);
            }
            else
            {
                r = 6.0 * (hue - twoThirds);
                g = 0.0;
                b = 6.0 * (1.0 - hue);
            }
            r = min(r, 1.0);
            g = min(g, 1.0);
            b = min(b, 1.0);
            float twice = 2.0 * saturation;
            float inverseSaturation = 1.0 - saturation;
            float inverseLightness = 1.0 - lightness;
            float twiceLightness = (2.0 * lightness) - 1.0;
            return float3(
                hslChannel(r, twice, inverseSaturation, lightness, inverseLightness, twiceLightness),
                hslChannel(g, twice, inverseSaturation, lightness, inverseLightness, twiceLightness),
                hslChannel(b, twice, inverseSaturation, lightness, inverseLightness, twiceLightness));
        }

        float3 vibrance(float r, float g, float b)
        {
            float inverted = 1.0 - vibrancyDarkness;
            float minc = min(r, min(g, b));
            float maxc = max(r, max(g, b));
            float delta = maxc - minc;
            float lightness = (minc + maxc) * 0.5;
            float saturation = 0.0;
            float hue = 0.0;
            if (lightness > 0.0 && lightness < 1.0)
            {
                float mul = lightness < 0.5 ? lightness : 1.0 - lightness;
                saturation = delta / (mul * 2.0);
            }
            if (delta > 0.0)
            {
                float sum = 0.0;
                if (r == maxc && maxc != g)
                    sum += (g - b) / delta;
                if (g == maxc && maxc != b)
                    sum += 2.0 + ((b - r) / delta);
                if (b == maxc && maxc != r)
                    sum += 4.0 + ((r - g) / delta);
                hue = sum / 6.0;
                if (hue < 0.0)
                    hue += 1.0;
            }

            float brightness = doubleCircle(sqrt((r * r * 0.299) + (g * g * 0.587) + (b * b * 0.114)), 0.8 * inverted);
            float b1 = 0.11 * inverted;
            float boost = 0.0;
            if (saturation > 0.0)
            {
                float edge0 = b1 - (0.66 * 0.5);
                float edge1 = b1 + (0.66 * 0.5);
                float x = 1.0 - (pow(1.0 - (saturation * cos(0.93)), 2.0) + pow(1.0 - (brightness * sin(0.93)), 2.0));
                float t = saturate((x - edge0) / (edge1 - edge0));
                boost = t * t * (3.0 - (2.0 * t));
            }
            return fromHsl(hue, saturate(saturation + (boost * vibrancy / passes)), lightness);
        }

        float4 tap(float x, float y)
        {
            return tex.SampleLevel(lin, float2(x / sourceW, y / sourceH), 0);
        }

        float4 psDown(float4 pos : SV_Position) : SV_Target
        {
            float x = 2.0 * pos.x;
            float y = 2.0 * pos.y;
            float4 sum = tap(x, y) * 4.0
                + tap(x - radius, y - radius)
                + tap(x + radius, y + radius)
                + tap(x + radius, y - radius)
                + tap(x - radius, y + radius);
            sum /= 8.0;
            if (vibrancy != 0.0)
                sum.rgb = vibrance(sum.r, sum.g, sum.b);
            return float4(sum.rgb, 1);
        }

        float4 psUp(float4 pos : SV_Position) : SV_Target
        {
            float x = pos.x * 0.5;
            float y = pos.y * 0.5;
            float near = radius * 0.25;
            float far = radius * 0.5;
            float4 sum = tap(x - far, y)
                + tap(x - near, y + near) * 2.0
                + tap(x, y + far)
                + tap(x + near, y + near) * 2.0
                + tap(x + far, y)
                + tap(x + near, y - near) * 2.0
                + tap(x, y - far)
                + tap(x - near, y - near) * 2.0;
            return float4((sum / 12.0).rgb, 1);
        }

        float4 psFinish(float4 pos : SV_Position) : SV_Target
        {
            float3 pixel = tex.Load(int3((int)pos.x, (int)pos.y, 0)).rgb;
            float amount = (hash(pos.x / destW, pos.y / destH) - 0.5) * noise;
            float3 color = round(saturate((pixel + amount) * finishBrightness) * 255.0) / 255.0;
            return float4(color, 1);
        }
        """;

    private readonly GpuDevice _gpu;
    private nint _vertex;
    private nint _prepare;
    private nint _down;
    private nint _up;
    private nint _finish;
    private bool _disposed;

    internal GpuKawase(GpuDevice gpu)
    {
        _gpu = gpu;
        _vertex = ShaderOf(12, "vsFull", "vs_4_0");
        _prepare = ShaderOf(15, "psPrepare", "ps_4_0");
        _down = ShaderOf(15, "psDown", "ps_4_0");
        _up = ShaderOf(15, "psUp", "ps_4_0");
        _finish = ShaderOf(15, "psFinish", "ps_4_0");
    }

    internal bool Ready => _vertex != 0 && _prepare != 0 && _down != 0 && _up != 0 && _finish != 0;

    /// <summary>Uploads <paramref name="source"/> and blurs it. The caller holds the device gate.</summary>
    internal bool TryBlur(ReadOnlySpan<uint> source, int width, int height, BlurTuning tuning, Span<uint> destination)
    {
        if (!Ready || tuning is null || width <= 0 || height <= 0 || width > BehindBlurPlan.MaxSide || height > BehindBlurPlan.MaxSide
            || source.Length < (long)width * height || destination.Length < (long)width * height)
        {
            return false;
        }

        using var uploaded = _gpu.Create(width, height, GpuUse.Source);
        if (uploaded is null || !_gpu.Upload(uploaded, source, width, height))
            return false;

        return BlurTexture(uploaded, width, height, tuning, destination);
    }

    /// <summary>Blurs a picture already on the device. The caller holds the device gate and keeps <paramref name="source"/>.</summary>
    internal bool BlurTexture(GpuTexture source, int width, int height, BlurTuning tuning, Span<uint> destination)
    {
        if (!Ready || tuning is null || width <= 0 || height <= 0 || destination.Length < (long)width * height)
            return false;

        var parameters = BlurShaderParameters.From(tuning);
        var passes = parameters.Passes;
        var widths = new int[passes + 1];
        var heights = new int[passes + 1];
        widths[0] = width;
        heights[0] = height;
        for (var i = 1; i <= passes; i++)
        {
            widths[i] = (widths[i - 1] + 1) / 2;
            heights[i] = (heights[i - 1] + 1) / 2;
        }

        var made = new List<GpuTexture>(passes + 3);
        try
        {
            var levels = new GpuTexture?[passes + 1];
            if (!Level(levels, 0, widths[0], heights[0], made) || !Pass(_prepare, source, levels[0]!, width, height, width, height, parameters))
                return false;

            for (var i = 1; i <= passes; i++)
            {
                if (!Level(levels, i, widths[i], heights[i], made)
                    || !Pass(_down, levels[i - 1]!, levels[i]!, widths[i - 1], heights[i - 1], widths[i], heights[i], parameters))
                {
                    return false;
                }
            }

            for (var i = passes - 1; i >= 0; i--)
            {
                var up = _gpu.Create(widths[i], heights[i], GpuUse.Target, FloatFormat);
                if (up is null)
                    return false;

                made.Add(up);
                if (!Pass(_up, levels[i + 1]!, up, widths[i + 1], heights[i + 1], widths[i], heights[i], parameters))
                    return false;

                levels[i] = up;
            }

            using var output = _gpu.Create(width, height, GpuUse.Target);
            if (output is null || !Pass(_finish, levels[0]!, output, width, height, width, height, parameters))
                return false;

            var read = new uint[width * height];
            if (!ReadInto(_gpu, output, width, height, read))
                return false;

            read.AsSpan().CopyTo(destination);
            return true;
        }
        finally
        {
            foreach (var texture in made)
                texture.Dispose();
        }
    }

    /// <summary>Reads a BGRA texture into <paramref name="destination"/>, width pixels to a row. The caller holds the device gate.</summary>
    internal static bool ReadInto(GpuDevice gpu, GpuTexture source, int width, int height, uint[] destination)
    {
        if (destination.Length < (long)width * height)
            return false;

        using var staging = gpu.Create(width, height, GpuUse.Readback);
        if (staging is null)
            return false;

        return gpu.Read(source, staging, width, height, (rows, pitch) =>
        {
            for (var y = 0; y < height; y++)
            {
                var from = (uint*)(rows + (y * pitch));
                for (var x = 0; x < width; x++)
                    destination[(y * width) + x] = from[x];
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        GpuCom.Release(ref _finish);
        GpuCom.Release(ref _up);
        GpuCom.Release(ref _down);
        GpuCom.Release(ref _prepare);
        GpuCom.Release(ref _vertex);
    }

    private bool Pass(nint shader, GpuTexture source, GpuTexture destination, int sourceWidth, int sourceHeight, int destinationWidth, int destinationHeight, BlurShaderParameters parameters)
    {
        Span<float> constants = stackalloc float[16];
        parameters.Write(constants, sourceWidth, sourceHeight, destinationWidth, destinationHeight);
        return _gpu.DrawFullscreen(destination, destinationWidth, destinationHeight, source, constants, _vertex, shader, clear: true);
    }

    private bool Level(GpuTexture?[] levels, int index, int width, int height, List<GpuTexture> made)
    {
        var texture = _gpu.Create(width, height, GpuUse.Target, FloatFormat);
        if (texture is null)
            return false;

        made.Add(texture);
        levels[index] = texture;
        return true;
    }

    private nint ShaderOf(int slot, string entry, string target)
    {
        var code = GpuDevice.CompileSource(Shader, entry, target);
        if (code.Length == 0 || !_gpu.CreateShader(slot, code, out var shader))
            return 0;

        return shader;
    }
}

/// <summary><see cref="GpuKawase"/> behind <see cref="IPictureBlur"/>. It takes the device gate itself.</summary>
internal sealed class GpuPictureBlur : IPictureBlur, IDisposable
{
    private readonly GpuDevice _device;
    private readonly GpuKawase _kawase;

    internal GpuPictureBlur(GpuDevice device)
    {
        _device = device;
        _kawase = new GpuKawase(device);
    }

    internal bool Ready => _kawase.Ready;

    internal GpuKawase Kawase => _kawase;

    /// <inheritdoc />
    public bool TryApply(ReadOnlySpan<uint> source, int width, int height, BlurTuning tuning, Span<uint> destination)
    {
        lock (_device.Gate)
            return _kawase.TryBlur(source, width, height, tuning, destination);
    }

    public void Dispose() => _kawase.Dispose();
}
