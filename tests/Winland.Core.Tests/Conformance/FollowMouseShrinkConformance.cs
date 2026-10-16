namespace Winland.Tests.Conformance;

/// <summary>The pinned release's follow_mouse_shrink case. The test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class FollowMouseShrinkConformance
{
    [Fact(Skip = "input:follow_mouse_shrink is not applied live, and movecursor is not a dispatcher.")]
    public void FollowMouseShrink()
    {
        // Mode 2 lets the cursor position decide focus, and float_switch_override_focus 2 switches it between
        // floating windows. Both come before the first window.
        var desk = new Desk("""
            input {
                follow_mouse = 2
                float_switch_override_focus = 2
            }
            """);
        desk.Send("dispatch workspace name:follow_mouse_shrink");

        // fms_a at 100,100, 400x400: its hitbox is 100..499 across and down.
        desk.Spawn("fms_a");
        desk.Ok("setfloating");
        desk.Ok("resizewindowpixel", "exact 400 400,class:fms_a");
        desk.Ok("movewindowpixel", "exact 100 100,class:fms_a");

        // fms_b at 520,100, 400x400: its hitbox is 520..919 across and 100..499 down.
        desk.Spawn("fms_b");
        desk.Ok("setfloating");
        desk.Ok("resizewindowpixel", "exact 400 400,class:fms_b");
        desk.Ok("movewindowpixel", "exact 520 100,class:fms_b");

        // With no shrink, the cursor at B's left edge focuses B.
        desk.Keyword("input:follow_mouse_shrink", "0");
        desk.Ok("focuswindow", "class:fms_a");
        Assert.Equal("fms_a", desk.ActiveClass());
        MoveCursor(desk, 300, 300);
        MoveCursor(desk, 521, 300);
        Assert.Equal("fms_b", desk.ActiveClass());

        // With a shrink of 20, the cursor 10 pixels inside B is in its dead zone and leaves focus on A.
        desk.Keyword("input:follow_mouse_shrink", "20");
        desk.Ok("focuswindow", "class:fms_a");
        Assert.Equal("fms_a", desk.ActiveClass());
        MoveCursor(desk, 300, 300);
        MoveCursor(desk, 530, 300);
        Assert.Equal("fms_a", desk.ActiveClass());

        // The cursor well inside B focuses it.
        MoveCursor(desk, 720, 300);
        Assert.Equal("fms_b", desk.ActiveClass());

        // The focused window's hitbox is not shrunk.
        desk.Ok("focuswindow", "class:fms_a");
        Assert.Equal("fms_a", desk.ActiveClass());
        MoveCursor(desk, 300, 300);
        MoveCursor(desk, 490, 300);
        Assert.Equal("fms_a", desk.ActiveClass());

        // The same point is outside A once A is not focused: its shrunk hitbox ends at 479.
        desk.Ok("focuswindow", "class:fms_b");
        Assert.Equal("fms_b", desk.ActiveClass());
        MoveCursor(desk, 720, 300);
        MoveCursor(desk, 490, 300);
        Assert.Equal("fms_b", desk.ActiveClass());
    }

    // The reference warps the cursor with a dispatcher, which then moves the pointer there.
    private static void MoveCursor(Desk desk, int x, int y)
    {
        desk.Ok("movecursor", $"{x} {y}");
        desk.MovePointer(x, y);
    }
}
