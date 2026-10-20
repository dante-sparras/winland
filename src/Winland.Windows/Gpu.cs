using System.Diagnostics;

namespace Winland.Windows;

/// <summary>
/// The one <see cref="GpuDevice"/> the window effects share. It is made when the first effect needs it, made again after it was
/// lost, and ended with the process's decorations. A machine that cannot make one is not asked again for half a minute.
/// </summary>
internal static class Gpu
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);
    private static readonly Lock Gate = new();
    private static GpuDevice? _device;
    private static long _failedAt;

    /// <summary>The device, or null when the machine cannot draw the effects.</summary>
    internal static GpuDevice? Acquire()
    {
        lock (Gate)
        {
            if (_device is { } device && !device.Lost())
                return device;

            _device?.Dispose();
            _device = null;
            if (_failedAt != 0 && Stopwatch.GetElapsedTime(_failedAt) < RetryAfter)
                return null;

            _device = GpuDevice.TryCreate();
            _failedAt = _device is null ? Stopwatch.GetTimestamp() : 0;
            return _device;
        }
    }

    /// <summary>Ends the device. Whoever still holds it finds it lost.</summary>
    internal static void Shutdown()
    {
        lock (Gate)
        {
            _device?.Dispose();
            _device = null;
            _failedAt = 0;
        }
    }
}
