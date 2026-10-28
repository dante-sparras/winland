namespace Winland.Application;

/// <summary>Finds the active Workspace, Monitor and Client, and the Workspace that holds a window.</summary>
public sealed partial class WindowManager
{
    private WorkspaceId ActiveId() => _workspaces.Shown.ActiveOn(FocusedId());

    // Each enabled Monitor shows a Workspace no other enabled Monitor shows. A step may break this on its way, so it is
    // checked after each one, in debug builds, where the tests run.
    [System.Diagnostics.Conditional("DEBUG")]
    private void CheckShownWorkspaces()
    {
        var seen = new HashSet<WorkspaceId>();
        foreach (var screen in _monitors)
        {
            if (screen.Arranged.Enabled && _workspaces.Shown.Shown(screen.Id) is WorkspaceId shown && !seen.Add(shown))
                throw new InvalidOperationException($"Workspace {shown} is shown on two Monitors.");
        }
    }

    private MonitorId FocusedId()
    {
        foreach (var screen in _monitors)
        {
            if (screen.Focused && screen.Arranged.Enabled)
                return screen.Id;
        }

        if (_monitors.Count == 0)
            return MonitorId.Primary;

        foreach (var screen in _monitors)
        {
            if (screen.Arranged.Enabled)
                return screen.Id;
        }

        return _monitors[0].Id;
    }

    private LiveWorkspace FocusedWorkspace() => WorkspaceFor(ActiveId(), FocusedId());

    private LiveWorkspace? WorkspaceContaining(WindowId window)
    {
        foreach (var desk in _workspaces)
        {
            if (desk.ClientOf(window) is not null)
                return desk;
        }

        return null;
    }

    private Client? Find(WindowId window)
    {
        foreach (var desk in _workspaces)
        {
            if (desk.ClientOf(window) is { } client)
                return client;
        }

        return null;
    }

    private Client? ClientOn(WorkspaceId id, WindowId window)
    {
        var desk = _workspaces.Find(id);
        return desk is null ? null : desk.ClientOf(window);
    }

    private Rectangle FocusedMonitorBounds()
    {
        if (_monitors.Count == 0 || MonitorById(FocusedId()) is not { Arranged.Enabled: true } monitor)
            return default;

        return monitor.Bounds;
    }

    private Rectangle WorkArea(LiveWorkspace desk, Rectangle fallback)
    {
        var screen = MonitorById(desk.Monitor);
        if (screen is { Arranged.Enabled: true })
            return screen.Arranged.WorkArea;

        return fallback;
    }

    private LiveMonitor? MonitorById(MonitorId id)
    {
        foreach (var screen in _monitors)
        {
            if (screen.Id == id)
                return screen;
        }

        return null;
    }
}
