namespace Winland.Domain;

/// <summary>
/// A popup with no caption that already covers its monitor is left alone.
/// An exclusive fullscreen game uses that shape, and adopting it would fight the game.
/// </summary>
public static class ExclusiveFullscreenWindow
{
    public const uint Popup = 0x80000000;
    public const uint Caption = 0x00C00000;

    public static bool Is(uint style, Rectangle window, Rectangle monitor)
    {
        if (monitor.Width <= 0 || monitor.Height <= 0 || window.Width <= 0 || window.Height <= 0)
            return false;

        if ((style & Popup) == 0 || (style & Caption) != 0)
            return false;

        const int slack = 2;
        return window.X <= monitor.X + slack
            && window.Y <= monitor.Y + slack
            && window.X + window.Width >= monitor.X + monitor.Width - slack
            && window.Y + window.Height >= monitor.Y + monitor.Height - slack;
    }
}
