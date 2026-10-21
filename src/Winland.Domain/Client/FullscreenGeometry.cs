namespace Winland.Domain;

/// <summary>The rectangle a covering mode uses. Mode 2 uses the monitor when that rectangle is known.</summary>
public static class FullscreenGeometry
{
    public static Rectangle Cover(FullscreenMode mode, Rectangle workArea, Rectangle monitor)
    {
        if (mode == FullscreenMode.Maximized)
            return workArea;

        if (mode == FullscreenMode.Fullscreen)
            return monitor.Width > 0 && monitor.Height > 0 ? monitor : workArea;

        return default;
    }
}
