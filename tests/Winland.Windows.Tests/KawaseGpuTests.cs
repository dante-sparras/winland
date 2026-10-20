using System.Diagnostics;
using Winland;
using Winland.Windows;
using Xunit.Abstractions;

namespace Winland.Tests;

public class KawaseGpuTests(ITestOutputHelper output)
{
    [Fact]
    public void TheGpuBlurMatchesTheCpuBlurWhenADeviceExists()
    {
        using var device = GpuDevice.TryCreate();
        if (device is null)
        {
            output.WriteLine("no hardware adapter");
            return;
        }

        using var blur = new GpuPictureBlur(device);
        if (!blur.Ready)
        {
            output.WriteLine("kawase shaders did not compile");
            return;
        }

        var plain = new BlurTuning { Size = 2, Passes = 2, Noise = 0, Vibrancy = 0, Contrast = 1, Brightness = 1 };
        AssertClose(blur, plain, 48, 32, 2);
        AssertClose(blur, BlurTuning.Documented, 48, 32, 8);
    }

    [Fact]
    public void BlurCostOnARepresentativePicture()
    {
        const int width = 960;
        const int height = 540;
        var source = Picture(width, height);
        var cpu = Time(() => BlurPipeline.Blur(source, width, height, BlurTuning.Documented));
        var heavy = Time(() => BlurPipeline.Blur(source, width, height, BlurTuning.Documented with { Passes = 4 }));
        output.WriteLine($"cpu 960x540 passes=1 {cpu.TotalMilliseconds:F1} ms");
        output.WriteLine($"cpu 960x540 passes=4 {heavy.TotalMilliseconds:F1} ms");

        using var device = GpuDevice.TryCreate();
        if (device is null)
        {
            output.WriteLine("gpu unavailable");
            return;
        }

        using var blur = new GpuPictureBlur(device);
        if (!blur.Ready)
        {
            output.WriteLine("gpu shaders unavailable");
            return;
        }

        var destination = new uint[source.Length];
        Assert.True(blur.TryApply(source, width, height, BlurTuning.Documented, destination));
        var gpu = Time(() => Assert.True(blur.TryApply(source, width, height, BlurTuning.Documented, destination)));
        var gpuHeavy = Time(() => Assert.True(blur.TryApply(source, width, height, BlurTuning.Documented with { Passes = 4 }, destination)));
        output.WriteLine($"gpu 960x540 passes=1 {gpu.TotalMilliseconds:F1} ms");
        output.WriteLine($"gpu 960x540 passes=4 {gpuHeavy.TotalMilliseconds:F1} ms");
        Assert.True(cpu < TimeSpan.FromSeconds(5));
        Assert.True(gpu < TimeSpan.FromSeconds(5));
    }

    private static void AssertClose(IPictureBlur blur, BlurTuning tuning, int width, int height, int tolerance)
    {
        var source = Picture(width, height);
        var cpu = BlurPipeline.Blur(source, width, height, tuning);
        var gpu = new uint[source.Length];
        Assert.True(blur.TryApply(source, width, height, tuning, gpu));
        var worst = 0;
        for (var i = 0; i < cpu.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs((int)((cpu[i] >> 16) & 0xFF) - (int)((gpu[i] >> 16) & 0xFF)));
            worst = Math.Max(worst, Math.Abs((int)((cpu[i] >> 8) & 0xFF) - (int)((gpu[i] >> 8) & 0xFF)));
            worst = Math.Max(worst, Math.Abs((int)(cpu[i] & 0xFF) - (int)(gpu[i] & 0xFF)));
        }

        Assert.True(worst <= tolerance, $"the largest channel difference was {worst}");
    }

    private static uint[] Picture(int width, int height)
    {
        var pixels = new uint[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            var red = (uint)((i * 13) % 256);
            var green = (uint)((i * 29) % 256);
            var blue = (uint)((i * 47) % 256);
            pixels[i] = 0xFF000000u | (red << 16) | (green << 8) | blue;
        }

        return pixels;
    }

    private static TimeSpan Time(Action work)
    {
        var watch = Stopwatch.StartNew();
        work();
        watch.Stop();
        return watch.Elapsed;
    }
}
