using Winland.Core;

namespace Winland.Core.Tests.Conformance;

/// <summary>Winland's snap case. The test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class SnapConformance
{
    [Fact]
    public void Snap()
    {
        var desk = new Desk("monitor = HEADLESS-2, addreserved, 200, 200, 200, 200\n");
        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Ok("workspace", "2");

        SpawnFloatingKitty(desk, "kittyProcA");
        Assert.Single(desk.Clients());
        desk.Ok("movewindowpixel", "exact 500 500,class:kittyProcA");

        SpawnFloatingKitty(desk, "kittyProcB");
        Assert.Equal(2, desk.Clients().Length);

        WindowSnap(desk, respectGaps: false);
        MonitorSnap(desk, respectGaps: false, overlap: false);

        desk.Keyword("general:snap:respect_gaps", "true");
        WindowSnap(desk, respectGaps: true);
        MonitorSnap(desk, respectGaps: true, overlap: false);

        desk.Keyword("general:snap:respect_gaps", "false");
        desk.Keyword("general:snap:border_overlap", "true");
        MonitorSnap(desk, respectGaps: false, overlap: true);

        desk.Keyword("general:snap:respect_gaps", "true");
        MonitorSnap(desk, respectGaps: true, overlap: true);
    }

    private static void SpawnFloatingKitty(Desk desk, string name)
    {
        desk.Spawn(name);
        desk.Ok("setfloating");
        desk.Ok("resizewindowpixel", $"exact 100 100,class:{name}");
    }

    private static void WindowSnap(Desk desk, bool respectGaps)
    {
        const int BorderSize = 2;
        const int WindowSize = 100;
        const int Other = 500;
        const int WindowGap = 8;
        const int GapsIn = 5;
        var gap = (respectGaps ? 2 * GapsIn : 0) + (2 * BorderSize);
        var end = gap + WindowSize;

        var x = WindowGap + end;
        ExpectNoSnapMove(desk, Other + x, Other);
        ExpectNoSnapMove(desk, Other - x, Other);
        ExpectNoSnapMove(desk, Other, Other + x);
        ExpectNoSnapMove(desk, Other, Other - x);
        x -= 1;
        ExpectSnapMove(desk, Other + x, Other, Other + end, Other);
        ExpectSnapMove(desk, Other - x, Other, Other - end, Other);
        ExpectSnapMove(desk, Other, Other + x, Other, Other + end);
        ExpectSnapMove(desk, Other, Other - x, Other, Other - end);
    }

    private static void MonitorSnap(Desk desk, bool respectGaps, bool overlap)
    {
        const int BorderSize = 2;
        const int WindowSize = 100;
        const int MonitorGap = 10;
        const int GapsOut = 20;
        var resp = respectGaps ? GapsOut : 0;
        var gap = resp + (overlap ? 0 : BorderSize);
        var end = gap + WindowSize;

        var x = MonitorGap + gap;
        ExpectNoSnapMove(desk, x, x);
        x -= 1;
        ExpectSnapMove(desk, x, x, gap, gap);

        x = MonitorGap + end;
        ExpectNoSnapMove(desk, 1920 - x, 1080 - x);
        x -= 1;
        ExpectSnapMove(desk, 1920 - x, 1080 - x, 1920 - end, 1080 - end);

        // The reserved area.
        const int Reserved = 200;
        var reservedGap = Reserved + resp + BorderSize;
        var reservedEnd = reservedGap + WindowSize;

        x = MonitorGap + reservedGap;
        ExpectNoSnapMove(desk, x, x);
        x -= 1;
        ExpectSnapMove(desk, x, x, reservedGap, reservedGap);

        x = MonitorGap + reservedEnd;
        ExpectNoSnapMove(desk, 1920 - x, 1080 - x);
        x -= 1;
        ExpectSnapMove(desk, 1920 - x, 1080 - x, 1920 - reservedEnd, 1080 - reservedEnd);
    }

    private static void ExpectNoSnapMove(Desk desk, int x, int y) => ExpectSnapMove(desk, x, y, x, y);

    private static void ExpectSnapMove(Desk desk, int fromX, int fromY, int toX, int toY)
    {
        desk.Ok("movewindowpixel", $"exact {fromX} {fromY},class:kittyProcB");
        SnapMove(desk);
        Assert.Equal($"{toX},{toY}", desk.ActivePlace().At);
    }

    // The test plugin snaps the active floating window where it stands, as one step of a move drag does.
    // Here a move drag of the active window steps away and back, so it ends where it began and snaps there.
    private static void SnapMove(Desk desk)
    {
        var bounds = desk.Placed(desk.ActiveId());
        var middle = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        var aside = middle with { X = middle.X + 1 };
        desk.Handle(new PointerGesture("movewindow", middle, PointerDragPhase.Begin, "mouse:272"));
        desk.Handle(new PointerGesture("movewindow", aside, PointerDragPhase.Move, "mouse:272"));
        desk.Handle(new PointerGesture("movewindow", middle, PointerDragPhase.Move, "mouse:272"));
        desk.Handle(new PointerGesture("movewindow", middle, PointerDragPhase.End, "mouse:272"));
    }
}
