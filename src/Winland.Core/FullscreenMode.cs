namespace Winland;

/// <summary>
/// How a Client is shown. 0 is its layout slot, 1 fills the monitor work area, and 2 fills the monitor rectangle.
/// </summary>
public enum FullscreenMode
{
    None = 0,
    Maximized = 1,
    Fullscreen = 2,
}

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

/// <summary>Internal and client modes from a <c>fullscreen_state</c> rule. The client mode is stored, not sent to the window.</summary>
public readonly record struct FullscreenRule(FullscreenMode Internal, FullscreenMode Client);
