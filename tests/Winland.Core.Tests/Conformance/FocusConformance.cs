namespace Winland.Tests.Conformance;

/// <summary>The pinned release's focus cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class FocusConformance
{
    // The reference's text reply says Invalid when no window is active. The JSON reply is an empty object.
    private const string NoWindow = "{}";

    [Fact(Skip = "seterror is not a request.")]
    public void ErrorBarReservationFollowsFocusedMonitor()
    {
        const string TestOutput = "HYPRTEST-ERROR-BAR";
        var desk = new Desk();
        desk.AddMonitor(TestOutput, Desk.Second);

        desk.Ok("focusmonitor", "HEADLESS-2");
        Assert.Equal("ok", desk.Send("seterror rgb(ff0000) reservation-test"));
        Assert.True(Reserved(desk, "HEADLESS-2", TestOutput));

        desk.Ok("focusmonitor", TestOutput);
        Assert.True(Reserved(desk, TestOutput, "HEADLESS-2"));

        desk.Ok("focusmonitor", "HEADLESS-2");
        Assert.True(Reserved(desk, "HEADLESS-2", TestOutput));
    }

    [Fact]
    public void CrossMonitorFullscreenFocus()
    {
        // A monitor right of the first, and a workspace rule that pins the destination workspace to it. The rule
        // comes before the first window, so it is in the starting config.
        var desk = new Desk("workspace = name:workspace2, monitor:HYPRTEST-2\n");
        desk.AddMonitor("HYPRTEST-2", Desk.Second);

        // One window on the left monitor.
        desk.Ok("workspace", "name:workspace1");
        desk.Spawn("1A");
        desk.Ok("focuswindow", "class:1A");
        var source = desk.ActiveWindow().GetProperty("monitor").GetInt32();

        // Two windows on the right monitor.
        desk.Ok("workspace", "name:workspace2");
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
        var desk = new Desk();
        desk.Ok("workspace", "1");
        Assert.Empty(desk.Clients());

        desk.Spawn("silent_move_last_tiled");
        Assert.Single(desk.Clients());

        var active = desk.ActiveWindow();
        Assert.Equal("silent_move_last_tiled", Desk.ClassOf(active));
        Assert.Equal(1, Desk.WorkspaceId(active));
        Assert.False(Desk.Floating(active));

        desk.Ok("movetoworkspacesilent", "2");

        // Focus is checked before switching workspaces, which could hide stale focus.
        Assert.Equal(1, desk.ActiveWorkspace().GetProperty("id").GetInt32());
        Assert.Equal(NoWindow, desk.Send("j/activewindow"));
        Assert.Single(desk.Clients());

        var client = desk.Client("silent_move_last_tiled");
        Assert.Equal(2, Desk.WorkspaceId(client));
    }

    [Fact]
    public void CrossMonitorEmptyWorkspaceUnfocusesWindow()
    {
        const string TestOutput = "HYPRTEST-UNFOCUS";
        var desk = new Desk();
        desk.AddMonitor(TestOutput, Desk.Second);

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

    // Whether the first monitor reserves space at its top and the second does not.
    private static bool Reserved(Desk desk, string reserved, string clear)
    {
        var monitors = Monitors(desk);
        int Top(string name) => monitors.Single(monitor => monitor.GetProperty("name").GetString() == name).GetProperty("reserved")[1].GetInt32();
        return Top(reserved) > 0 && Top(clear) == 0;
    }

    private static List<System.Text.Json.JsonElement> Monitors(Desk desk)
    {
        using var document = System.Text.Json.JsonDocument.Parse(desk.Send("j/monitors"));
        return [.. document.RootElement.EnumerateArray().Select(monitor => monitor.Clone())];
    }
}
