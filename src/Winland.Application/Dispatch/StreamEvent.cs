namespace Winland.Application;

public abstract record StreamEvent;

public sealed record ClientOpened(WindowId Window, WorkspaceId Workspace, string Class, string Title) : StreamEvent;

public sealed record ClientClosed(WindowId Window) : StreamEvent;

/// <summary>
/// Why the focus moved, as a bit set. A handler can test a bit.
/// <see cref="Unknown"/> is a focus with no equivalent.
/// </summary>
public enum FocusReason : uint
{
    Unknown = 0,
    FollowMouse = 1 << 0,
    Keybind = 1 << 1,
    FocusWindow = 1 << 2,
    MoveIntoGroup = 1 << 3,
    Click = 1 << 4,
    ClickDown = (1 << 5) | Click,
    ClickUp = (1 << 6) | Click,
    DesktopState = 1 << 7,
    SwitchSoft = 1 << 8,
    SwitchHard = 1 << 9,
    GroupCurrent = 1 << 10,
    Workspace = 1 << 11,
    ToggleSpecial = 1 << 12,
    UnmapTiled = 1 << 13,
    UnmapFloating = 1 << 14,
    UnmapGrouped = 1 << 15,
    NewWindow = 1 << 16,
    Ghosts = 1 << 17,
    Other = 1 << 18,
}

/// <summary>The focused window changed. <paramref name="Window"/> is null when nothing is focused.</summary>
public sealed record FocusChanged(WindowId? Window) : StreamEvent
{
    /// <summary>Why the focus moved. <see cref="FocusReason.Unknown"/> when the caller does not say.</summary>
    public FocusReason Reason { get; init; }
}

public sealed record ClientUrgent(WindowId Window) : StreamEvent;

public sealed record ClientMoved(WindowId Window, WorkspaceId Workspace) : StreamEvent;

/// <summary>A Client's window changed its title. <paramref name="Active"/> when it is the focused Client.</summary>
public sealed record ClientRetitled(WindowId Window, string Title, bool Active) : StreamEvent;

/// <summary>A Client's window changed its class. <paramref name="Active"/> when it is the focused Client.</summary>
public sealed record ClientReclassed(WindowId Window, string Class, bool Active) : StreamEvent;

/// <summary>
/// The monitors were laid out again: one came or went, one's mode, position, scale, orientation or mirror changed, or a
/// reload applied the monitor rules. The config hears this as <c>monitor.layout_changed</c>.
/// </summary>
public sealed record MonitorLayoutChanged : StreamEvent;

public sealed record FloatingChanged(WindowId Window, bool Floating) : StreamEvent;

public sealed record ClientMinimized(WindowId Window, bool Minimized) : StreamEvent;

/// <summary>A Client was pinned or unpinned. <paramref name="Pinned"/> is the state after the change.</summary>
public sealed record ClientPinned(WindowId Window, bool Pinned) : StreamEvent;

public sealed record WorkspaceActivated(WorkspaceId Workspace) : StreamEvent;

public sealed record WorkspaceCreated(WorkspaceId Workspace) : StreamEvent;

/// <summary><paramref name="Id"/> and <paramref name="Name"/> are the id and name the workspace had before it was removed. <paramref name="Monitor"/> is the Monitor that showed it.</summary>
public sealed record WorkspaceDestroyed(WorkspaceId Workspace, long Id, string Name, string Monitor) : StreamEvent;

public sealed record WorkspaceMoved(WorkspaceId Workspace, string Monitor) : StreamEvent;

public sealed record MonitorFocused(string Name, WorkspaceId Workspace) : StreamEvent;

/// <summary>The special workspace shown on a Monitor. Null when it closes.</summary>
public sealed record ActiveSpecial(WorkspaceId? Workspace, string Monitor) : StreamEvent;

public sealed record ConfigWarned(int Line, string Message, string? Source = null) : StreamEvent;

public sealed record ConfigReloaded : StreamEvent;

public sealed record MonitorAdded(string Name) : StreamEvent;

public sealed record MonitorAddedV2(int Id, string Name, string Description) : StreamEvent;

public sealed record MonitorRemoved(string Name) : StreamEvent;

public sealed record MonitorRemovedV2(int Id, string Name, string Description) : StreamEvent;

/// <summary>
/// A Client entered or left a covering fullscreen mode. The data is <c>1</c> or <c>0</c>. <paramref name="Window"/> is
/// the Client, which the event line leaves out.
/// </summary>
public sealed record FullscreenChanged(bool Entered, WindowId? Window = null) : StreamEvent;

/// <summary>A Group was made (<paramref name="Created"/>) or ended. <paramref name="Window"/> is its first member.</summary>
public sealed record GroupToggled(WindowId Window, bool Created) : StreamEvent;

/// <summary>A window was moved into a Group.</summary>
public sealed record ClientIntoGroup(WindowId Window) : StreamEvent;

/// <summary>A window was moved out of a Group.</summary>
public sealed record ClientOutOfGroup(WindowId Window) : StreamEvent;

