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

        desk.Ok("movecurrentworkspacetomonitor", Right);

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
        desk.Ok("togglefloating");
        desk.Ok("pin");

        desk.Spawn("workspace_move_float");
        desk.Ok("togglefloating");
        desk.Place(desk.ActiveId(), 200, 200, 400, 300);

        desk.Spawn("workspace_move_fullscreen");
        desk.Ok("fullscreen", "0 set");

        desk.Ok("movecurrentworkspacetomonitor", Right);

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
        var desk = Prepare(new Desk());

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
        desk.Ok("togglefloating");
        desk.Place(desk.ActiveId(), 200, 200, 400, 300);

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

        desk.MovePointer(100, 100);
        var original = CursorPos(desk);

        desk.Ok("focusmonitor", Right);
        Assert.Equal(original, CursorPos(desk));
        ExpectActiveWorkspace(desk, 301, Right);

        desk.MovePointer(2000, 10);
        Assert.NotEqual(original, CursorPos(desk));
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
    private static string CursorPos(Desk desk) => $"{desk.Pointer.X}, {desk.Pointer.Y}";
}
