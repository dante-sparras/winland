using Winland;

namespace Winland.Tests;

public class MouseDragTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);

    [Fact]
    public void FloatingMoveFollowsThePointer()
    {
        var core = Flat("bindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });

        var moved = core.Handle(new PointerMoved(new Point(750, 430)));

        Assert.Equal("movewindow", moved.Dispatcher);
        Assert.True(moved.Live);
        // The float toggle recenters the 640x400 default, then the pointer moves it by (+50, +30).
        Assert.Equal(new Rectangle(690, 370, 640, 400), BoundsOf(moved, 1));
        Assert.True(Client(moved, 1).Floating);
    }

    [Fact]
    public void TiledDragDropsTheClientIntoTheTreeUnderThePointer()
    {
        var core = Flat("bindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1000, 100) });
        core.Handle(new PointerMoved(new Point(100, 500)));

        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(100, 500) });

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(dropped, 2));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(dropped, 1));
        Assert.False(Client(dropped, 2).Floating);
        Assert.False(dropped.Live);
    }

    [Fact]
    public void SecondDragButtonLeavesTheFirstDragToDropItsClient()
    {
        var core = Flat("bindm = SUPER, mouse:272, movewindow\nbindm = SUPER, mouse:273, resizewindow\n");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(480, 540) });
        core.Handle(new PointerMoved(new Point(500, 540)));
        core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(1440, 540) });
        core.Handle(new PointerButtonReleased("mouse:273") { Position = new Point(1440, 540) });

        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(500, 540) });

        Assert.False(Client(dropped, 1).Floating);
        Assert.False(Client(dropped, 2).Floating);
        Assert.False(dropped.Live);
    }

    [Fact]
    public void SecondGestureBeginLeavesTheFirstDragToDropItsClient()
    {
        var core = Flat("");
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new PointerGesture("movewindow", new Point(480, 540), PointerDragPhase.Begin, "mouse:272"));
        core.Handle(new PointerGesture("movewindow", new Point(500, 540), PointerDragPhase.Move, "mouse:272"));
        core.Handle(new PointerGesture("resizewindow", new Point(1440, 540), PointerDragPhase.Begin, "mouse:273"));

        var dropped = core.Handle(new PointerGesture("movewindow", new Point(500, 540), PointerDragPhase.End, "mouse:272"));

        Assert.False(Client(dropped, 1).Floating);
        Assert.False(Client(dropped, 2).Floating);
    }

    [Fact]
    public void FloatingResizeGrowsFromTheGrabbedCorner()
    {
        var core = Flat("bindm = SUPER, mouse:273, resizewindow\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(1270, 730) });

        var resized = core.Handle(new PointerMoved(new Point(1310, 760)));

        Assert.Equal("resizewindow", resized.Dispatcher);
        // The recentered 640x400 float grows by the pointer delta (+40, +30).
        Assert.Equal(new Rectangle(640, 340, 680, 430), BoundsOf(resized, 1));
    }

    [Fact]
    public void TiledResizeChangesTheSplitRatio()
    {
        var core = Flat("bindm = SUPER, mouse:273, resizewindow\n");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(960, 540) });

        var resized = core.Handle(new PointerMoved(new Point(1060, 540)));

        Assert.Equal(new Rectangle(0, 0, 1060, 1080), BoundsOf(resized, 1));
        Assert.Equal(new Rectangle(1060, 0, 860, 1080), BoundsOf(resized, 2));
    }

    [Fact]
    public void TheDragTakesTheClientUnderThePointerAndFocusesIt()
    {
        var core = Flat("bindm = SUPER, mouse:273, resizewindow\n");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");

        var pressed = core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(480, 540) });
        var resized = core.Handle(new PointerMoved(new Point(580, 540)));

        Assert.Equal(new WindowId(1), pressed.Focus);
        Assert.Equal(new Rectangle(0, 0, 1060, 1080), BoundsOf(resized, 1));
        Assert.Equal(new Rectangle(1060, 0, 860, 1080), BoundsOf(resized, 2));
    }

    [Fact]
    public void APressOffEveryClientDoesNotDrag()
    {
        var core = Flat("bindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(10, 20) });

        var moved = core.Handle(new PointerMoved(new Point(60, 50)));

        Assert.Null(moved.Dispatcher);
        Assert.Empty(moved.Placements);
    }

    [Fact]
    public void TheDragThresholdHoldsTheClientUntilThePointerPassesIt()
    {
        var core = Flat("binds:drag_threshold = 10\nbindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });

        var held = core.Handle(new PointerMoved(new Point(706, 408)));
        core.Handle(new PointerMoved(new Point(720, 400)));
        var moved = core.Handle(new PointerMoved(new Point(750, 430)));

        Assert.Equal("movewindow", held.Dispatcher);
        Assert.Empty(held.Placements);
        // The drag starts at (720, 400), where the pointer passed 10 pixels, so the float moves by (+30, +30).
        Assert.Equal(new Rectangle(670, 370, 640, 400), BoundsOf(moved, 1));
    }

    [Fact]
    public void ALiftedTileIsCenteredOnThePointer()
    {
        var core = Flat("bindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1000, 100) });

        var lifted = core.Handle(new PointerMoved(new Point(1200, 300)));

        Assert.Equal(new Rectangle(720, -240, 960, 1080), BoundsOf(lifted, 2));
        Assert.True(Client(lifted, 2).Floating);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), BoundsOf(lifted, 1));
    }

    [Fact]
    public void WithoutDragCenterWindowALiftedTileKeepsThePointGrabbed()
    {
        var core = Flat("binds:drag_center_window = false\nbindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1000, 100) });

        // The drag began at (1000, 100), 40 pixels into the tile and 100 down, and the lifted tile keeps that point.
        var lifted = core.Handle(new PointerMoved(new Point(1200, 300)));

        Assert.Equal(new Rectangle(1160, 200, 960, 1080), BoundsOf(lifted, 2));
        Assert.True(Client(lifted, 2).Floating);

        var moved = core.Handle(new PointerMoved(new Point(1300, 350)));

        Assert.Equal(new Rectangle(1260, 250, 960, 1080), BoundsOf(moved, 2));
        Assert.Equal("{\"option\":\"binds:drag_center_window\",\"bool\":false,\"set\":true}", core.Execute("getoption binds:drag_center_window").Text);
    }

    [Fact]
    public void ADropFollowsThePointerEvenWhenTheSplitIsForced()
    {
        var core = Flat("dwindle:force_split = 2\nbindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1000, 100) });
        core.Handle(new PointerMoved(new Point(100, 500)));

        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(100, 500) });

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(dropped, 2));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(dropped, 1));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void PreciseMouseMoveDropsOnTheSideNearestThePointer(string precise)
    {
        var core = Flat("dwindle:precise_mouse_move = " + precise + "\nbindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1000, 100) });
        core.Handle(new PointerMoved(new Point(960, 1000)));

        // Client 1 fills the screen while 2 is lifted. The pointer is below its center.
        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(960, 1000) });

        if (precise == "true")
        {
            Assert.Equal(new Rectangle(0, 0, 1920, 540), BoundsOf(dropped, 1));
            Assert.Equal(new Rectangle(0, 540, 1920, 540), BoundsOf(dropped, 2));
        }
        else
        {
            Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(dropped, 1));
            Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(dropped, 2));
        }
    }

    [Fact]
    public void SmartResizingMovesTheGrabbedEdgeAndKeepsTheOtherOne()
    {
        var core = FourTiles("");
        core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(1000, 600) });

        // The pointer grabs 3 by its top left. Its left edge moves and its right edge, shared with 4, stays.
        var resized = core.Handle(new PointerMoved(new Point(900, 600)));

        Assert.Equal(new Rectangle(0, 0, 860, 1080), BoundsOf(resized, 1));
        Assert.Equal(new Rectangle(860, 0, 1060, 540), BoundsOf(resized, 2));
        Assert.Equal(new Rectangle(860, 540, 580, 540), BoundsOf(resized, 3));
        Assert.Equal(new Rectangle(1440, 540, 480, 540), BoundsOf(resized, 4));
    }

    [Fact]
    public void WithoutSmartResizingTheNearestSplitMoves()
    {
        var core = FourTiles("dwindle:smart_resizing = false\n");
        core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(1000, 600) });

        var resized = core.Handle(new PointerMoved(new Point(900, 600)));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(resized, 1));
        Assert.Equal(new Rectangle(960, 540, 380, 540), BoundsOf(resized, 3));
        Assert.Equal(new Rectangle(1340, 540, 580, 540), BoundsOf(resized, 4));
    }

    [Fact]
    public void MasterSmartResizingTakesSpaceFromTheTileAboveAGrabbedTopEdge()
    {
        var core = Flat("general:layout = master\nbindm = SUPER, mouse:273, resizewindow\n");
        Outcome placed = null!;
        for (var window = 1; window <= 4; window++)
            placed = Appear(core, window);
        var stack = placed.Placements.Where(placement => placement.Bounds.X > 0).OrderBy(placement => placement.Bounds.Y).ToList();
        Assert.Equal(3, stack.Count);
        var above = stack[0];
        var middle = stack[1];
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(middle.Bounds.X + 10, middle.Bounds.Y + 10) });

        var resized = core.Handle(new PointerMoved(new Point(middle.Bounds.X + 10, middle.Bounds.Y - 50)));

        var grown = Assert.Single(resized.Placements, placement => placement.Client == middle.Client).Bounds;
        var shrunk = Assert.Single(resized.Placements, placement => placement.Client == above.Client).Bounds;
        Assert.Equal(middle.Bounds.Y - 60, grown.Y);
        Assert.Equal(middle.Bounds.Y + middle.Bounds.Height, grown.Y + grown.Height);
        Assert.Equal(above.Bounds.Height - 60, shrunk.Height);
    }

    [Fact]
    public void ResizeWindowOneKeepsTheAspectRatio()
    {
        var core = Flat("bindm = SUPER, mouse:273, resizewindow 1\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(1270, 730) });

        var resized = core.Handle(new PointerMoved(new Point(1370, 740)));

        // 640x400 grows to a width of 740, so the height follows at 740 * 400 / 640.
        Assert.Equal(new Rectangle(640, 340, 740, 463), BoundsOf(resized, 1));
    }

    [Theory]
    [InlineData("resizewindow", 463)]
    [InlineData("resizewindow 2", 410)]
    public void TheKeepAspectRatioPropertyKeepsTheRatioUnlessTheBindSaysTwo(string dispatch, int height)
    {
        var core = Flat($"bindm = SUPER, mouse:273, {dispatch}\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        core.Handle(new Dispatch("setprop", "address:0x1 keep_aspect_ratio 1"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(1270, 730) });

        var resized = core.Handle(new PointerMoved(new Point(1370, 740)));

        Assert.Equal(new Rectangle(640, 340, 740, height), BoundsOf(resized, 1));
    }

    [Fact]
    public void AMouseBindPressedOnTheBorderTakesTheClient()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 2\nbindm = SUPER, mouse:273, resizewindow\n")));
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        var frame = Client(core.Handle(new Dispatch("no_op")), 1).Bounds;
        Press(core, "Super_L");

        // One pixel past the frame's bottom right corner, on the border.
        var corner = new Point(frame.X + frame.Width, frame.Y + frame.Height);
        core.Handle(new PointerButtonPressed("mouse:273") { Position = corner });
        var resized = core.Handle(new PointerMoved(new Point(corner.X + 100, corner.Y + 10)));

        Assert.Equal(frame with { Width = frame.Width + 100, Height = frame.Height + 10 }, BoundsOf(resized, 1));
    }

    [Fact]
    public void AMouseBindCarriesTheResizeMode()
    {
        var document = ConfigParser.Parse("bindm = SUPER, mouse:273, resizewindow 2\n");

        Assert.Empty(document.Warnings);
        var bind = Assert.Single(document.Binds.OfType<Bind>());
        Assert.Equal("resizewindow", bind.Dispatcher);
        Assert.Equal("2", bind.Arguments);
    }

    [Fact]
    public void TheHookGestureCarriesTheResizeMode()
    {
        var core = Flat("");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        core.Handle(new PointerGesture("resizewindow", new Point(1270, 730), PointerDragPhase.Begin, "mouse:273") { Arguments = "1" });

        var resized = core.Handle(new PointerGesture("resizewindow", new Point(1370, 740), PointerDragPhase.Move, "mouse:273") { Arguments = "1" });

        Assert.Equal(new Rectangle(640, 340, 740, 463), BoundsOf(resized, 1));
    }

    [Fact]
    public void TheMouseDispatcherDragsLikeAMouseBind()
    {
        var core = Flat("bind = SUPER, mouse:272, mouse, movewindow\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });

        var moved = core.Handle(new PointerMoved(new Point(750, 430)));

        Assert.Equal(new Rectangle(690, 370, 640, 400), BoundsOf(moved, 1));
    }

    [Theory]
    [InlineData("bind = SUPER, Z, mouse, movewindow", "The mouse dispatcher needs a mouse button.")]
    [InlineData("bind = SUPER, mouse:272, mouse, killactive", "The mouse dispatcher takes movewindow or resizewindow.")]
    [InlineData("bindr = SUPER, mouse:272, mouse, movewindow", "Bind flags conflict.")]
    public void TheMouseDispatcherRejectsWhatCannotDrag(string line, string warning)
    {
        var document = ConfigParser.Parse(line + "\n");

        Assert.Equal([new ConfigWarning(1, warning)], document.Warnings);
        Assert.Empty(document.Binds);
    }

    [Fact]
    public void ADragPressWithNoWindowUnderThePointerGoesThrough()
    {
        var core = Flat("bindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");

        var empty = core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(10, 10) });
        core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(10, 10) });
        var held = core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });

        Assert.False(empty.Consumed);
        Assert.False(core.HasDragTarget(new Point(10, 10)));
        Assert.True(held.Consumed);
        Assert.True(core.HasDragTarget(new Point(700, 400)));
    }

    [Fact]
    public void ATileDroppedOnAnotherMonitorJoinsTheWorkspaceItShows()
    {
        var core = TwoMonitors();
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1400, 500) });
        core.Handle(new PointerMoved(new Point(2400, 500)));

        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(2400, 500) });

        var moved = Client(dropped, 2);
        Assert.False(moved.Floating);
        Assert.NotEqual(Client(dropped, 1).Workspace, moved.Workspace);
        Assert.Equal(new Rectangle(1920, 0, 1920, 1080), moved.Bounds);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), Client(dropped, 1).Bounds);
        Assert.Contains(dropped.Events, item => item is ClientMoved changed && changed.Window == new WindowId(2));
    }

    [Fact]
    public void AFloatingWindowDroppedOnAnotherMonitorKeepsItsRectangleThere()
    {
        var core = TwoMonitors();
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });
        core.Handle(new PointerMoved(new Point(2700, 400)));

        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(2700, 400) });

        var moved = Client(dropped, 1);
        Assert.True(moved.Floating);
        Assert.Equal(new Rectangle(2640, 340, 640, 400), moved.Bounds);
        Assert.NotEqual(WorkspaceId.Initial, moved.Workspace);
    }

    [Fact]
    public void AWindowDroppedOnItsOwnMonitorStaysOnItsWorkspace()
    {
        var core = TwoMonitors();
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });
        core.Handle(new PointerMoved(new Point(900, 400)));

        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(900, 400) });

        Assert.Equal(WorkspaceId.Initial, Client(dropped, 1).Workspace);
    }

    private static Core TwoMonitors()
    {
        var core = Flat("bindm = SUPER, mouse:272, movewindow\n");
        core.Handle(new MonitorsChanged(
        [
            new Monitor(@"\\.\DISPLAY1", "Left", 0, 0, 1920, 1080, 1, true, Wide),
            new Monitor(@"\\.\DISPLAY2", "Right", 1920, 0, 1920, 1080, 1, true, new Rectangle(1920, 0, 1920, 1080)),
        ]));
        return core;
    }

    [Fact]
    public void InMonocleAFloatingClientFollowsThePointer()
    {
        var core = Flat("general:layout = monocle\nbindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });

        var moved = core.Handle(new PointerMoved(new Point(750, 430)));

        Assert.Equal(new Rectangle(690, 370, 640, 400), BoundsOf(moved, 1));
    }

    [Fact]
    public void InMonocleALiftedTileLeavesTheStackAndComesBackShown()
    {
        var core = Flat("general:layout = monocle\nbindm = SUPER, mouse:272, movewindow\nbindm = SUPER, mouse:273, resizewindow\n");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(960, 540) });

        var lifted = core.Handle(new PointerMoved(new Point(1000, 560)));

        Assert.True(Client(lifted, 2).Floating);
        Assert.Equal(Wide, BoundsOf(lifted, 1));
        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(1000, 560) });
        Assert.False(Client(dropped, 2).Floating);
        Assert.Equal(Wide, BoundsOf(dropped, 2));

        core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(1900, 1000) });
        Assert.DoesNotContain(core.Handle(new PointerMoved(new Point(1700, 900))).Placements, placement => placement.Bounds != Wide);
    }

    [Fact]
    public void AMoveShowsTheGrabbingPointerUntilItIsDropped()
    {
        var core = Flat("bindm = SUPER, mouse:272, movewindow\n");
        Appear(core, 1);
        Press(core, "Super_L");

        var pressed = core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });
        var moved = core.Handle(new PointerMoved(new Point(750, 430)));
        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(750, 430) });

        Assert.Equal("grabbing", pressed.ResizeCursor);
        Assert.Equal("grabbing", moved.ResizeCursor);
        Assert.Null(dropped.ResizeCursor);
    }

    [Theory]
    [InlineData(100, 100, "top_left_corner")]
    [InlineData(1800, 100, "top_right_corner")]
    [InlineData(1800, 1000, "bottom_right_corner")]
    [InlineData(100, 1000, "bottom_left_corner")]
    public void AResizeShowsTheCornerItGrabbed(int x, int y, string cursor)
    {
        var core = Flat("bindm = SUPER, mouse:273, resizewindow\n");
        Appear(core, 1);
        Press(core, "Super_L");

        Assert.Equal(cursor, core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(x, y) }).ResizeCursor);
    }

    // Dwindle with 1 on the left, 2 at the top right, and 3 and 4 side by side at the bottom right.
    private static Core FourTiles(string options)
    {
        var core = Flat(options + "bindm = SUPER, mouse:273, resizewindow\n");
        Outcome placed = null!;
        for (var window = 1; window <= 4; window++)
            placed = Appear(core, window);
        Assert.Equal(new Rectangle(960, 540, 480, 540), BoundsOf(placed, 3));
        Assert.Equal(new Rectangle(1440, 540, 480, 540), BoundsOf(placed, 4));
        Press(core, "Super_L");
        return core;
    }

    private static Core Flat(string binds)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + binds)));
        return core;
    }

    private static Outcome Appear(Core core, long window) =>
        core.Handle(new WindowAppeared(new WindowId(window), Wide));

    private static void Press(Core core, string key) =>
        core.Handle(new KeyPressed(key));

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Client(outcome, window);
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }
}
