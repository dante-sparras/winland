namespace Winland.Domain;

/// <summary>
/// How a Client is shown. 0 is its layout slot, 1 fills the monitor work area, and 2 fills the monitor rectangle.
/// </summary>
public enum FullscreenMode
{
    None = 0,
    Maximized = 1,
    Fullscreen = 2,
}
