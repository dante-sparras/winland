namespace Winland.Tests.Conformance;

/// <summary>Winland's drag cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class DragConformance
{
    // The reference rebinds the left button to a drag with no modifier before its first window.
    private const string DragOnLeftButton = """
        unbind = SUPER, mouse:272
        bindm = , mouse:272, movewindow
        """;

    [Fact]
    public void DragTiledCentered()
    {
        var desk = new Desk("binds {\n    drag_center_window = true\n}\n" + DragOnLeftButton);
        desk.Spawn("kitty");
        Assert.Single(desk.Clients());
        desk.Ok("focuswindow", "class:kitty");

        // Grab well away from the center, so centering is observable.
        var (grabX, grabY) = Grab(desk);
        MoveCursor(desk, grabX, grabY);
        Click(desk, pressed: true);
        MoveCursor(desk, 700, 500);

        // The grab point is ignored: the floating window is centered on the cursor.
        var moved = Active(desk);
        Assert.True(Nearly(moved.X + (moved.Width / 2), 700));
        Assert.True(Nearly(moved.Y + (moved.Height / 2), 500));

        Click(desk, pressed: false);
    }

    [Fact]
    public void DragTiledProportional()
    {
        var desk = new Desk("binds {\n    drag_center_window = false\n}\n" + DragOnLeftButton);
        desk.Spawn("kitty");
        Assert.Single(desk.Clients());
        desk.Ok("focuswindow", "class:kitty");

        // Grab three quarters across and down the tiled window.
        var (grabX, grabY) = Grab(desk);
        MoveCursor(desk, grabX, grabY);
        Click(desk, pressed: true);
        MoveCursor(desk, 700, 500);

        // The window shrinks as it floats, but the cursor still sits three quarters across and down.
        var moved = Active(desk);
        Assert.True(Nearly(moved.X + (moved.Width * 3 / 4), 700));
        Assert.True(Nearly(moved.Y + (moved.Height * 3 / 4), 500));

        Click(desk, pressed: false);
    }

    [Fact]
    public void DragFullscreenCentered()
    {
        var desk = new Desk("binds {\n    drag_center_window = true\n}\n" + DragOnLeftButton);
        desk.Spawn("kitty");
        Assert.Single(desk.Clients());
        desk.Ok("focuswindow", "class:kitty");
        desk.Ok("setfloating");
        desk.Ok("resizewindowpixel", "exact 600 400,class:kitty");
        desk.Ok("movewindowpixel", "exact 100 100,class:kitty");
        desk.Ok("fullscreen", "0 set");

        // Grab well away from the center, so centering is observable.
        var (grabX, grabY) = Grab(desk);
        MoveCursor(desk, grabX, grabY);
        Click(desk, pressed: true);
        MoveCursor(desk, 700, 500);

        // The grab point is ignored: the restored window is centered on the cursor.
        var moved = Active(desk);
        Assert.Equal(0, Desk.Fullscreen(desk.ActiveWindow()));
        Assert.True(Nearly(moved.X + (moved.Width / 2), 700));
        Assert.True(Nearly(moved.Y + (moved.Height / 2), 500));

        Click(desk, pressed: false);
    }

    [Fact]
    public void DragFullscreenProportional()
    {
        var desk = new Desk("binds {\n    drag_center_window = false\n}\n" + DragOnLeftButton);
        desk.Spawn("kitty");
        Assert.Single(desk.Clients());
        desk.Ok("focuswindow", "class:kitty");
        desk.Ok("setfloating");
        desk.Ok("resizewindowpixel", "exact 600 400,class:kitty");
        desk.Ok("movewindowpixel", "exact 100 100,class:kitty");
        desk.Ok("fullscreen", "0 set");

        // Grab three quarters across and down the fullscreen surface, far from where the restored window would go.
        var (grabX, grabY) = Grab(desk);
        MoveCursor(desk, grabX, grabY);
        Click(desk, pressed: true);
        MoveCursor(desk, 700, 500);

        // The window shrinks back to its floating size, but the cursor still sits three quarters across and down.
        var moved = Active(desk);
        Assert.Equal(0, Desk.Fullscreen(desk.ActiveWindow()));
        Assert.True(Nearly(moved.X + (moved.Width * 3 / 4), 700, 8));
        Assert.True(Nearly(moved.Y + (moved.Height * 3 / 4), 500, 8));

        Click(desk, pressed: false);
    }

    [Fact]
    public void DragThresholdSurvivesScrollEvents()
    {
        var desk = new Desk("""
            general {
                snap {
                    enabled = false
                }
            }
            binds {
                scroll_event_delay = 0
            }
            """ + "\n" + DragOnLeftButton);
        desk.Spawn("drag_threshold");
        Assert.Single(desk.Clients());
        desk.Ok("focuswindow", "class:drag_threshold");
        desk.Ok("setfloating");
        desk.Ok("resizewindowpixel", "exact 300 200,class:drag_threshold");

        foreach (var threshold in new[] { 0, 40 })
        {
            desk.Keyword("binds:drag_threshold", threshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
            desk.Ok("movewindowpixel", "exact 200 200,class:drag_threshold");
            MoveCursor(desk, 350, 300);
            Click(desk, pressed: true);
            ExpectDragState(desk, active: true, reached: threshold == 0);

            if (threshold > 0)
            {
                // Motion below the threshold does not pick up the window.
                MoveCursor(desk, 360, 300);
                ExpectDragState(desk, active: true, reached: false);
                Assert.True(Nearly(Active(desk).X, 200));
            }

            MoveCursor(desk, 410, 300);
            ExpectDragState(desk, active: true, reached: true);
            var before = Active(desk);

            // Scroll events do not end the drag.
            Scroll(desk, 120);
            ExpectDragState(desk, active: true, reached: true);
            Scroll(desk, -120);
            ExpectDragState(desk, active: true, reached: true);

            // A small move follows at once, without crossing the threshold again.
            MoveCursor(desk, 420, 300);
            var after = Active(desk);
            Assert.True(Nearly(after.X, before.X + 10));
            Assert.True(Nearly(after.Y, before.Y));

            Click(desk, pressed: false);
            ExpectDragState(desk, active: false, reached: false);

            // A new drag starts with its own threshold, not the last drag's.
            Click(desk, pressed: true);
            ExpectDragState(desk, active: true, reached: threshold == 0);
            Click(desk, pressed: false);
            ExpectDragState(desk, active: false, reached: false);
        }
    }

    private static Rectangle Active(Desk desk) => desk.Placed(desk.ActiveId());

    // Three quarters across and down the active window.
    private static (int X, int Y) Grab(Desk desk)
    {
        var bounds = Active(desk);
        return (bounds.X + (bounds.Width * 3 / 4), bounds.Y + (bounds.Height * 3 / 4));
    }

    // The reference warps the cursor with a dispatcher, which then moves the pointer there.
    private static void MoveCursor(Desk desk, int x, int y)
    {
        desk.Ok("movecursor", $"{x} {y}");
        desk.MovePointer(x, y);
    }

    // The test plugin's click: the left button, where the pointer is.
    private static void Click(Desk desk, bool pressed)
    {
        if (pressed)
            desk.Handle(new PointerButtonPressed("mouse:272") { Position = desk.Pointer });
        else
            desk.Handle(new PointerButtonReleased("mouse:272") { Position = desk.Pointer });
    }

    // The test plugin's scroll: a positive delta turns the wheel down.
    private static void Scroll(Desk desk, int delta) =>
        desk.Handle(new WheelTurned(delta > 0 ? "mouse_down" : "mouse_up", Environment.TickCount64));

    // The test plugin reads the drag controller: whether a drag holds a window, and whether it has picked it up.
    private static void ExpectDragState(Desk desk, bool active, bool reached)
    {
        var gesture = typeof(Core).GetField("_gesture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(desk.Core);
        var pickedUp = gesture is not null && (bool)gesture.GetType().GetProperty("PickedUp")!.GetValue(gesture)!;
        Assert.Equal((active, reached), (gesture is not null, pickedUp));
    }

    private static bool Nearly(int a, int b, int tolerance = 4) => Math.Abs(a - b) <= tolerance;
}
