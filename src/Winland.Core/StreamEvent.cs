namespace Winland;

public abstract record StreamEvent;

public sealed record ClientOpened(WindowId Window, WorkspaceId Workspace, string Class, string Title) : StreamEvent;

public sealed record ClientClosed(WindowId Window) : StreamEvent;

public sealed record FocusChanged(WindowId? Window) : StreamEvent;

public sealed record ClientUrgent(WindowId Window) : StreamEvent;

public sealed record ClientMoved(WindowId Window, WorkspaceId Workspace) : StreamEvent;

public sealed record FloatingChanged(WindowId Window, bool Floating) : StreamEvent;

public sealed record ClientMinimized(WindowId Window, bool Minimized) : StreamEvent;

/// <summary>A Client was pinned or unpinned. <paramref name="Pinned"/> is the state after the change.</summary>
public sealed record ClientPinned(WindowId Window, bool Pinned) : StreamEvent;

public sealed record WorkspaceActivated(WorkspaceId Workspace) : StreamEvent;

public sealed record WorkspaceCreated(WorkspaceId Workspace) : StreamEvent;

/// <summary><paramref name="Id"/> is the id the workspace had before it was removed.</summary>
public sealed record WorkspaceDestroyed(WorkspaceId Workspace, int Id) : StreamEvent;

public sealed record WorkspaceMoved(WorkspaceId Workspace, string Monitor) : StreamEvent;

public sealed record MonitorFocused(string Name, WorkspaceId Workspace) : StreamEvent;

/// <summary>The special workspace shown on a Monitor. Null when it closes.</summary>
public sealed record ActiveSpecial(WorkspaceId? Workspace, string Monitor) : StreamEvent;

public sealed record ConfigWarned(int Line, string Message, string? Source = null) : StreamEvent;

public sealed record ConfigReloaded : StreamEvent;

/// <summary>The active bind submap changed. <paramref name="Name"/> is empty on the default map.</summary>
public sealed record SubmapChanged(string Name) : StreamEvent;

public sealed record MonitorAdded(string Name) : StreamEvent;

public sealed record MonitorAddedV2(int Id, string Name, string Description) : StreamEvent;

public sealed record MonitorRemoved(string Name) : StreamEvent;

public sealed record MonitorRemovedV2(int Id, string Name, string Description) : StreamEvent;

/// <summary>A custom event line, <c>custom&gt;&gt;text</c>.</summary>
public sealed record CustomEvent(string Text) : StreamEvent;

/// <summary>A Client entered or left a covering fullscreen mode. The data is <c>1</c> or <c>0</c>.</summary>
public sealed record FullscreenChanged(bool Entered) : StreamEvent;

/// <summary>A Group was made (<paramref name="Created"/>) or ended. <paramref name="Window"/> is its first member.</summary>
public sealed record GroupToggled(WindowId Window, bool Created) : StreamEvent;

/// <summary>A window was moved into a Group.</summary>
public sealed record ClientIntoGroup(WindowId Window) : StreamEvent;

/// <summary>A window was moved out of a Group.</summary>
public sealed record ClientOutOfGroup(WindowId Window) : StreamEvent;

/// <summary>Every Group was locked or unlocked. <paramref name="Locked"/> is the state after the change.</summary>
public sealed record GroupsLocked(bool Locked) : StreamEvent;
