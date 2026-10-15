using PluginApi = Winland.PluginApi;

namespace Winland.Plugins;

internal static class PluginEvents
{
    public static PluginApi.PluginEvent? Map(StreamEvent streamEvent) => streamEvent switch
    {
        ClientOpened opened => new PluginApi.WindowOpened(opened.Window.Value, opened.Workspace.Name, opened.Class, opened.Title),
        ClientClosed closed => new PluginApi.WindowClosed(closed.Window.Value),
        FocusChanged focused => new PluginApi.WindowFocused(focused.Window?.Value),
        ClientUrgent urgent => new PluginApi.WindowUrgent(urgent.Window.Value),
        ClientMoved moved => new PluginApi.WindowMoved(moved.Window.Value, moved.Workspace.Name),
        ClientPinned pinned => new PluginApi.WindowPinned(pinned.Window.Value, pinned.Pinned),
        WorkspaceActivated activated => new PluginApi.WorkspaceSwitched(activated.Workspace.Name),
        ConfigWarned warned => new PluginApi.ConfigurationWarned(warned.Line, warned.Message, warned.Source),
        ConfigReloaded => new PluginApi.ConfigurationReloaded(),
        SubmapChanged changed => new PluginApi.SubmapSwitched(changed.Name),
        MonitorAdded added => new PluginApi.MonitorConnected(added.Name),
        MonitorAddedV2 added => new PluginApi.MonitorConnected(added.Name, added.Id, added.Description),
        MonitorRemoved removed => new PluginApi.MonitorDisconnected(removed.Name),
        MonitorRemovedV2 removed => new PluginApi.MonitorDisconnected(removed.Name, removed.Id, removed.Description),
        _ => null,
    };
}
