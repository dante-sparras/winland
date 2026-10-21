using System.Text.Json;

namespace Winland.Application.Tests.Conformance;

/// <summary>
/// Winland's refactor_state cases. Each test is named after its case, and sends the same commands.
/// Each case keeps the harness's HEADLESS-2 at 0,0 as its left monitor and creates a right one at 1920,0.
/// </summary>
[Trait("Category", "Conformance")]
public class RefactorStateConformance
{
    private const string Left = "HEADLESS-2";

    private const string Right = "HYPRTEST-REFACTOR-R";

    [Fact]
    public void WorkspacePlacementActiveMove()
    {
        var desk = Prepare(new Desk());

        desk.Ok("focusmonitor", Left);
        desk.Ok("workspace", "270");
        desk.Spawn("workspace_move_active");

        desk.Ok("focusmonitor", Right);
        desk.Ok("workspace", "271");

        desk.Ok("focusmonitor", Left);
        desk.Ok("workspace", "270");

        desk.Ok("moveworkspacetomonitor", $"270 {Right}");

        desk.Ok("focusmonitor", Right);
        ExpectActiveWorkspace(desk, 270, Right);

        desk.Ok("focusmonitor", Left);
        var active = desk.ActiveWorkspace();
        Assert.NotEqual(270, active.GetProperty("id").GetInt32());
        Assert.Equal(Left, active.GetProperty("monitor").GetString());
    }

    [Fact]
    public void WorkspacePlacementWindowState()
    {
        var desk = Prepare(new Desk());
        desk.Ok("focusmonitor", Left);
        desk.Ok("workspace", "280");

        desk.Spawn("workspace_move_pinned");
        desk.Ok("setfloating");
        desk.Ok("pin");

        desk.Spawn("workspace_move_float");
        desk.Ok("setfloating");
        desk.Ok("resizewindowpixel", "exact 400 300,activewindow");
        desk.Ok("movewindowpixel", "exact 200 200,activewindow");

        desk.Spawn("workspace_move_fullscreen");
        desk.Ok("fullscreen", "0 set");

        desk.Ok("moveworkspacetomonitor", $"280 {Right}");

        desk.Ok("focusmonitor", Left);
        var pinned = desk.Client("workspace_move_pinned");
        Assert.True(Desk.Pinned(pinned));
        Assert.True(Desk.Floating(pinned));
        Assert.True(pinned.GetProperty("visible").GetBoolean());

        desk.Ok("focusmonitor", Right);
        desk.Ok("focuswindow", "class:workspace_move_float");
        Assert.Equal("workspace_move_float", desk.ActiveClass());
        Assert.Equal("2120,200", desk.ActivePlace().At);

        desk.Ok("focuswindow", "class:workspace_move_fullscreen");
        Assert.Equal("workspace_move_fullscreen", desk.ActiveClass());
        Assert.Equal(2, Desk.Fullscreen(desk.ActiveWindow()));
        desk.ExpectActive("1920,0", "1920,1080");
    }

    [Fact]
    public void GlobalWindowMoveState()
    {
        var desk = Prepare(new Desk("""
            group {
                group_on_movetoworkspace = true
            }
            """));

        desk.Ok("focusmonitor", Right);
        desk.Ok("workspace", "291");

        desk.Ok("focusmonitor", Left);
        desk.Ok("workspace", "290");
        desk.Spawn("global_move_fullscreen");
        desk.Ok("fullscreen", "0 set");
        // A direction refuses a fullscreen Client. mon: puts it on the Workspace that Monitor is showing.
        desk.Ok("movewindow", $"mon:{Right} silent");

        desk.Ok("focusmonitor", Right);
        desk.Ok("focuswindow", "class:global_move_fullscreen");
        Assert.Equal(2, Desk.Fullscreen(desk.ActiveWindow()));

        desk.Ok("workspace", "294");

        desk.Ok("focusmonitor", Left);
        desk.Ok("workspace", "292");
        desk.Spawn("global_move_float");
        desk.Ok("setfloating");
        desk.Ok("resizewindowpixel", "exact 400 300,activewindow");
        desk.Ok("movewindowpixel", "exact 200 200,activewindow");

        desk.Ok("movewindow", $"mon:{Right}");
        Assert.Equal("global_move_float", desk.ActiveClass());
        Assert.Equal("2120,200", desk.ActivePlace().At);

        desk.Ok("workspace", "293");
        desk.Spawn("global_move_group_anchor");
        desk.Ok("togglegroup");

        desk.Ok("focusmonitor", Left);
        desk.Ok("workspace", "295");
        desk.Spawn("global_move_group_member");
        desk.Ok("movewindow", $"mon:{Right}");
        Assert.Equal("global_move_group_member", desk.ActiveClass());

        desk.Ok("changegroupactive", "f");
        Assert.Equal("global_move_group_anchor", desk.ActiveClass());
    }

    [Fact]
    public void PointerNoWarpsFocusesMonitorOnly()
    {
        // The case sets cursor:no_warps before any window opens.
        var desk = Prepare(new Desk("""
            cursor {
                no_warps = true
            }
            """));

        desk.Ok("focusmonitor", Right);
        desk.Ok("workspace", "301");
        desk.Ok("focusmonitor", Left);
        desk.Ok("workspace", "300");

        desk.Ok("movecursor", "100 100");
        var original = CursorPos(desk);

        desk.Ok("focusmonitor", Right);
        Assert.Equal(original, CursorPos(desk));
        ExpectActiveWorkspace(desk, 301, Right);

        desk.Ok("movecursor", "2000 10");
        Assert.NotEqual(original, CursorPos(desk));
    }

    [Fact]
    public void CursorWarpFocusOnMonitorChange()
    {
        var desk = Prepare(new Desk());

        desk.Ok("focusmonitor", Right);
        desk.Ok("workspace", "301");
        desk.Spawn("kitty301");
        desk.Ok("focusmonitor", Left);
        desk.Spawn("kitty300");
        desk.Ok("workspace", "300");

        // no_warps, warp_on_change_workspace, warp_on_monitor_change, and whether the cursor moves. The
        // reference leaves out the combinations it marks as failing.
        CheckCursorWarpWhenSwitchingMonitor(desk, false, 0, 1, true);
        CheckCursorWarpWhenSwitchingMonitor(desk, false, 0, 2, true);
        CheckCursorWarpWhenSwitchingMonitor(desk, false, 1, -1, true);
        CheckCursorWarpWhenSwitchingMonitor(desk, false, 1, 1, true);
        CheckCursorWarpWhenSwitchingMonitor(desk, false, 1, 2, true);
        CheckCursorWarpWhenSwitchingMonitor(desk, false, 2, -1, true);
        CheckCursorWarpWhenSwitchingMonitor(desk, false, 2, 1, true);
        CheckCursorWarpWhenSwitchingMonitor(desk, false, 2, 2, true);
        CheckCursorWarpWhenSwitchingMonitor(desk, true, 0, -1, false);
        CheckCursorWarpWhenSwitchingMonitor(desk, true, 0, 0, false);
        CheckCursorWarpWhenSwitchingMonitor(desk, true, 0, 1, false);
        CheckCursorWarpWhenSwitchingMonitor(desk, true, 0, 2, true);
        CheckCursorWarpWhenSwitchingMonitor(desk, true, 1, -1, false);
        CheckCursorWarpWhenSwitchingMonitor(desk, true, 1, 0, false);
        CheckCursorWarpWhenSwitchingMonitor(desk, true, 1, 1, false);
        CheckCursorWarpWhenSwitchingMonitor(desk, true, 1, 2, true);
        CheckCursorWarpWhenSwitchingMonitor(desk, true, 2, -1, true);
        CheckCursorWarpWhenSwitchingMonitor(desk, true, 2, 0, false);
        CheckCursorWarpWhenSwitchingMonitor(desk, true, 2, 1, false);
        CheckCursorWarpWhenSwitchingMonitor(desk, true, 2, 2, true);
    }

    private static void CheckCursorWarpWhenSwitchingMonitor(Desk desk, bool noWarps, int onWorkspace, int onMonitor, bool expectMove)
    {
        desk.Keyword("cursor:no_warps", noWarps ? "true" : "false");
        desk.Keyword("cursor:warp_on_change_workspace", $"{onWorkspace}");
        desk.Keyword("cursor:warp_on_monitor_change", $"{onMonitor}");

        desk.Ok("focusmonitor", Left);
        desk.Ok("workspace", "300");
        desk.Ok("movecursor", "5 5");
        Assert.Equal("5, 5", CursorPos(desk));

        desk.Ok("focusmonitor", Right);
        if (expectMove)
            Assert.NotEqual("5, 5", CursorPos(desk));
        else
            Assert.Equal("5, 5", CursorPos(desk));
    }

    // The reference creates the right monitor, as output create headless does, at 1920,0.
    private static Desk Prepare(Desk desk)
    {
        desk.AddMonitor(Right, Desk.Second);
        return desk;
    }

    private static void ExpectActiveWorkspace(Desk desk, int id, string monitor)
    {
        var active = desk.ActiveWorkspace();
        Assert.Equal(id, active.GetProperty("id").GetInt32());
        Assert.Equal(monitor, active.GetProperty("monitor").GetString());
    }

    // The cursor position, written as the reference's text reply writes it: "x, y".
    private static string CursorPos(Desk desk)
    {
        using var position = JsonDocument.Parse(desk.Send("j/cursorpos"));
        return $"{position.RootElement.GetProperty("x").GetInt32()}, {position.RootElement.GetProperty("y").GetInt32()}";
    }
}
