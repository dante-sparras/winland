using System.Globalization;

namespace Winland.Domain;

/// <param name="Pinned">Filled in snapshots. Layout records do not carry the pin.</param>
/// <param name="AllowedOverFullscreen">Filled in snapshots from FullscreenCover.</param>
/// <param name="BelowFullscreen">Filled in snapshots from FullscreenCover.</param>
public sealed record Client(
    ClientId Id,
    WindowId Window,
    bool Floating = false,
    bool Minimized = false,
    WorkspaceId Workspace = default,
    string Class = "",
    string Title = "",
    long ProcessId = 0,
    string InitialClass = "",
    string InitialTitle = "",
    Rectangle Bounds = default,
    FullscreenMode Fullscreen = FullscreenMode.None,
    bool Pinned = false,
    bool LayoutFullscreen = false,
    bool AllowedOverFullscreen = true,
    bool BelowFullscreen = false)
{
    /// <summary>The query's client mode is always the fullscreen mode.</summary>
    public FullscreenMode FullscreenClient => Fullscreen;
}

public readonly record struct WindowId(long Value)
{
    /// <summary>The window as the replies and selectors write it: <c>0x</c> and the handle in hexadecimal.</summary>
    public string Address => "0x" + Value.ToString("x", CultureInfo.InvariantCulture);
}

public readonly record struct ClientId(long Value);

public readonly record struct Placement(ClientId Client, Rectangle Bounds);
