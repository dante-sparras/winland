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
