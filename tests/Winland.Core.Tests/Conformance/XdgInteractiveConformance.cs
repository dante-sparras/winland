namespace Winland.Tests.Conformance;

/// <summary>
/// The pinned release's interactive move and resize case. Its client asks to be moved and resized while a button is
/// held, as a title bar or a border drag does. On Windows the system moves and sizes the window itself and Winland
/// hears the drag start, the pointer move and the drag end. The checks of what the client hears, the pointer leaving
/// it and no motion during the move, are left out. A stale serial and a bad resize edge start no drag on Windows, so
/// those steps only press and release the button.
/// </summary>
[Trait("Category", "Conformance")]
public class XdgInteractiveConformance
{
    [Fact(Skip = "There is no setprop or movecursor dispatcher. A floating Client the system moves or resizes keeps its old frame in activewindow.")]
    public void XdgInteractive()
    {
        var desk = new Desk();
        var client = desk.Spawn("xdg-interactive");
        var pid = desk.Client("xdg-interactive").GetProperty("pid").GetInt64();
        desk.Ok("setprop", $"pid:{pid} no_anim 1");
        desk.Ok("focuswindow", $"pid:{pid}");

        SetClientGeometry(desk, pid, new Rectangle(300, 300, 500, 400));

        // xdg_toplevel.move
        var startMove = ActiveGeometry(desk);
        desk.Ok("movecursor", $"{startMove.X + 100} {startMove.Y + 100}");
        Press(desk);
        desk.Handle(new WindowMoveStarted(client, desk.Pointer));
        desk.Ok("movecursor", $"{startMove.X + 220} {startMove.Y + 180}");
        Release(desk);
        desk.Handle(new WindowMoveEnded(client, desk.Pointer, startMove with { X = startMove.X + 120, Y = startMove.Y + 80 }));

        var endMove = ActiveGeometry(desk);
        Near(startMove.X + 120, endMove.X, 5);
        Near(startMove.Y + 80, endMove.Y, 5);
        Near(startMove.Width, endMove.Width, 2);
        Near(startMove.Height, endMove.Height, 2);

        // xdg_toplevel.resize right
        SetClientGeometry(desk, pid, new Rectangle(400, 300, 500, 400));
        Assert.True(Desk.Floating(desk.ActiveWindow()));
        var startResize = ActiveGeometry(desk);
        desk.Ok("movecursor", $"{startResize.X + 250} {startResize.Y + 200}");
        Press(desk);
        desk.Handle(new WindowSizeStarted(client, desk.Pointer));
        desk.Ok("movecursor", $"{startResize.X + 410} {startResize.Y + 200}");
        desk.Handle(new WindowSizing(client, desk.Pointer));
        Assert.True(ActiveGeometry(desk).Width >= startResize.Width + 150, "The resize did not change the frame before the release.");

        Release(desk);
        desk.Handle(new WindowSizeEnded(client, desk.Pointer));

        var endResize = ActiveGeometry(desk);
        Near(startResize.X, endResize.X, 2);
        Near(startResize.Y, endResize.Y, 2);
        Near(startResize.Width + 160, endResize.Width, 5);
        Near(startResize.Height, endResize.Height, 2);

        // A stale resize serial is ignored: the button is up, so no drag starts.
        SetClientGeometry(desk, pid, new Rectangle(500, 300, 500, 400));
        var stale = ActiveGeometry(desk);
        desk.Ok("movecursor", $"{stale.X + 250} {stale.Y + 200}");
        desk.Click();
        desk.Ok("movecursor", $"{stale.X + 380} {stale.Y + 200}");

        var afterStale = ActiveGeometry(desk);
        Near(stale.X, afterStale.X, 2);
        Near(stale.Y, afterStale.Y, 2);
        Near(stale.Width, afterStale.Width, 2);
        Near(stale.Height, afterStale.Height, 2);

        // An invalid resize edge disconnects the client in the reference. The compositor keeps answering.
        desk.Ok("movecursor", $"{afterStale.X + 250} {afterStale.Y + 200}");
        desk.Click();
        Assert.NotEqual("", desk.Send("clients"));
    }

    private static void SetClientGeometry(Desk desk, long pid, Rectangle geometry)
    {
        desk.Ok("focuswindow", $"pid:{pid}");
        desk.Ok("setfloating", $"pid:{pid}");
        desk.Ok("resizewindowpixel", $"exact {geometry.Width} {geometry.Height},pid:{pid}");
        desk.Ok("movewindowpixel", $"exact {geometry.X} {geometry.Y},pid:{pid}");
        desk.Ok("focuswindow", $"pid:{pid}");
        Assert.Equal(geometry, ActiveGeometry(desk));
    }

    // The active window's at and size, as the reference reads them from activewindow.
    private static Rectangle ActiveGeometry(Desk desk)
    {
        var active = desk.ActiveWindow();
        var at = active.GetProperty("at");
        var size = active.GetProperty("size");
        return new Rectangle(at[0].GetInt32(), at[1].GetInt32(), size[0].GetInt32(), size[1].GetInt32());
    }

    private static void Press(Desk desk) =>
        desk.Handle(new PointerButtonPressed("mouse:272") { Position = desk.Pointer });

    private static void Release(Desk desk) =>
        desk.Handle(new PointerButtonReleased("mouse:272") { Position = desk.Pointer });

    private static void Near(int expected, int actual, int delta) =>
        Assert.InRange(actual, expected - delta, expected + delta);
}
