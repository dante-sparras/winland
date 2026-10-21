namespace Winland.Domain;

public readonly record struct ClientId(long Value);

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

public readonly record struct Placement(ClientId Client, Rectangle Bounds);
