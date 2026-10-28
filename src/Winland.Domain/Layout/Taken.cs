namespace Winland.Domain;

/// <summary>
/// A Client handed from one Workspace's Layout to another. A Layout leaves the fullscreen mode at none;
/// <see cref="LiveWorkspace.Take"/> fills it in so the Client is fullscreen again where it lands.
/// </summary>
internal readonly record struct Taken(
    bool Floating,
    bool Minimized,
    Rectangle Bounds,
    string Class = "",
    string Title = "",
    long ProcessId = 0,
    string? InitialClass = null,
    string? InitialTitle = null,
    Size? MinSize = null,
    Size? MaxSize = null,
    double? ScrollingWidth = null,
    FullscreenMode Fullscreen = FullscreenMode.None,
    TiledLimits? TiledLimits = null,
    bool LayoutFullscreen = false);
