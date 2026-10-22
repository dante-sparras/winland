namespace Winland.Domain;

/// <summary>
/// What the core recorded about one Client for the next start. <see cref="Floating"/>, <see cref="Bounds"/>,
/// <see cref="Pinned"/> and the fullscreen modes are null in a snapshot written before they were recorded,
/// and the window rules decide them then.
/// </summary>
public sealed record RecoveredClient(
    WindowId Window,
    WorkspaceId Workspace,
    MonitorId Monitor,
    bool Hidden,
    string Class,
    string Title,
    long ProcessId,
    bool Layered = false,
    bool? Floating = null,
    Rectangle? Bounds = null,
    bool? Pinned = null,
    FullscreenMode? Fullscreen = null,
    FullscreenMode? FullscreenClient = null)
{
    /// <summary>The style the window had before fullscreen mode 2 dropped its frame. Null when the frame is its own.</summary>
    public uint? FrameStyle { get; init; }

    /// <summary>True when the window's region is the corner outline Winland cut, not one of its own.</summary>
    public bool Region { get; init; }

    /// <summary>The identity of the Monitor the Client was on, or its Windows name when it has no model and serial.</summary>
    public string MonitorIdentity { get; init; } = "";
}
