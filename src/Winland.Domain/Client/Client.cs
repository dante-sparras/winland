using System.Globalization;

namespace Winland.Domain;

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
    FullscreenMode FullscreenClient = FullscreenMode.None,
    bool Pinned = false,
    bool PinFullscreened = false,
    IReadOnlyList<string>? Tags = null,
    int Content = 0,
    bool LayoutFullscreen = false,
    bool AllowedOverFullscreen = true,
    bool BelowFullscreen = false);

public readonly record struct WindowId(long Value)
{
    /// <summary>The window as the replies and selectors write it: <c>0x</c> and the handle in hexadecimal.</summary>
    public string Address => "0x" + Value.ToString("x", CultureInfo.InvariantCulture);
}

public readonly record struct ClientId(long Value);

public readonly record struct Placement(ClientId Client, Rectangle Bounds);
