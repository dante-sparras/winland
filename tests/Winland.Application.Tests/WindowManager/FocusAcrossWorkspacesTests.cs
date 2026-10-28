namespace Winland.Application.Tests;

/// <summary>Focus when a window moves away or the user switches Workspace or Monitor.</summary>
public class FocusAcrossWorkspacesTests
{
    // The reference's text reply says Invalid when no window is active. The JSON reply is an empty object.
    private const string NoWindow = "{}";

    [Fact]
    public void CrossMonitorFullscreenFocus()
    {
        // A monitor right of the first. Each Monitor has its own Workspaces.
        var desk = new TestDesktop();
        desk.AddMonitor("HYPRTEST-2", TestDesktop.Second);

        // One window on the left monitor.
        desk.Ok("workspace", "1");
        desk.Spawn("1A");
        desk.Ok("focuswindow", "class:1A");
        var source = desk.ActiveWindow().GetProperty("monitor").GetInt32();

        // Two windows on the right monitor.
        desk.Ok("focusmonitor", "HYPRTEST-2");
        desk.Ok("workspace", "2");
        desk.Spawn("2A");
        desk.Spawn("2B");
        desk.Ok("focuswindow", "class:2B");
        var destination = desk.ActiveWindow().GetProperty("monitor").GetInt32();

        // The two windows really are on different monitors.
        Assert.NotEqual(source, destination);

        // Float and fullscreen 2B.
        desk.Ok("focuswindow", "class:2B");
        desk.Ok("togglefloating");
        desk.Ok("fullscreen", "0 toggle");

        // Focus 1A, and move focus right.
        desk.Ok("focuswindow", "class:1A");
        desk.Ok("movefocus", "r");

        Assert.Equal("2B", desk.ActiveClass());
    }

    [Fact]
    public void SilentMoveLastTiledWindowUnfocusesWindow()
    {
        var desk = new TestDesktop();
        desk.Ok("workspace", "1");
        Assert.Empty(desk.Clients());

        desk.Spawn("silent_move_last_tiled");
        Assert.Single(desk.Clients());

        var active = desk.ActiveWindow();
        Assert.Equal("silent_move_last_tiled", TestDesktop.ClassOf(active));
        Assert.Equal(1, TestDesktop.WorkspaceId(active));
        Assert.False(TestDesktop.Floating(active));

        desk.Ok("movetoworkspacesilent", "2");

        // Focus is checked before switching workspaces, which could hide stale focus.
        Assert.Equal(1, desk.ActiveWorkspace().GetProperty("id").GetInt32());
        Assert.Equal(NoWindow, desk.Send("j/activewindow"));
        Assert.Single(desk.Clients());

        var client = desk.Client("silent_move_last_tiled");
        Assert.Equal(2, TestDesktop.WorkspaceId(client));
    }

    [Fact]
    public void CrossMonitorEmptyWorkspaceUnfocusesWindow()
    {
        const string TestOutput = "HYPRTEST-UNFOCUS";
        var desk = new TestDesktop();
        desk.AddMonitor(TestOutput, TestDesktop.Second);

        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Ok("workspace", "1");
        Assert.Empty(desk.Clients());

        desk.Ok("focusmonitor", TestOutput);
        desk.Ok("workspace", "2");
        desk.Spawn("cross_monitor_ws2");
        Assert.Equal("cross_monitor_ws2", desk.ActiveClass());

        desk.Ok("workspace", "1");
        Assert.Equal(1, desk.ActiveWorkspace().GetProperty("id").GetInt32());
        Assert.Equal(NoWindow, desk.Send("j/activewindow"));
    }
}
