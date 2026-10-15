namespace Winland.PluginApi;

public abstract record PluginEvent;

public sealed record WindowOpened(long Window, string Workspace, string Class, string Title) : PluginEvent;

public sealed record WindowClosed(long Window) : PluginEvent;

public sealed record WindowFocused(long? Window) : PluginEvent;

public sealed record WindowUrgent(long Window) : PluginEvent;

public sealed record WindowMoved(long Window, string Workspace) : PluginEvent;

public sealed record WindowPinned(long Window, bool Pinned) : PluginEvent;

public sealed record WorkspaceSwitched(string Workspace) : PluginEvent;

public sealed record ConfigurationWarned(int Line, string Message, string? Source) : PluginEvent;

public sealed record ConfigurationReloaded : PluginEvent;

public sealed record SubmapSwitched(string Name) : PluginEvent;

public sealed record MonitorConnected(string Name, int? Id = null, string? Description = null) : PluginEvent;

public sealed record MonitorDisconnected(string Name, int? Id = null, string? Description = null) : PluginEvent;
