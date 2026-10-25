namespace Winland.Application.Tests;

public class MouseDragTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);

    private static readonly WorkspaceId RightWorkspace = WorkspaceId.FromNumber(1, new MonitorId(1));

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

    [Fact]
    public void AMouseBindPressedOnTheBorderTakesTheClient()
    {
        var core = new WindowManager();
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
        var crossed = core.Handle(new PointerMoved(new Point(2400, 500)));

        var crossing = Client(crossed, 2);
        Assert.True(crossing.Floating);
        Assert.Equal(RightWorkspace, crossing.Workspace);
        Assert.Equal(new MonitorId(0), crossed.State.ActiveMonitor);
        Assert.Contains(crossed.Events, item => item is ClientMoved changed && changed.Window == new WindowId(2));
        Assert.Equal([@"movewindow>>2,1,\\.\DISPLAY2", @"movewindowv2>>2,1,1,\\.\DISPLAY2"], MoveLines(crossed));

        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(2400, 500) });

        var moved = Client(dropped, 2);
        Assert.False(moved.Floating);
        Assert.NotEqual(Client(dropped, 1).Workspace, moved.Workspace);
        Assert.Equal(new Rectangle(1920, 0, 1920, 1080), moved.Bounds);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), Client(dropped, 1).Bounds);
        Assert.DoesNotContain(dropped.Events, item => item is ClientMoved);
    }

    [Fact]
    public void AFloatingWindowDroppedOnAnotherMonitorKeepsItsRectangleThere()
    {
        var core = TwoMonitors();
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });
        var crossed = core.Handle(new PointerMoved(new Point(2700, 400)));
        Assert.Equal(RightWorkspace, Client(crossed, 1).Workspace);
        Assert.Equal(new MonitorId(0), crossed.State.ActiveMonitor);

        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(2700, 400) });

        var moved = Client(dropped, 1);
        Assert.True(moved.Floating);
        Assert.Equal(new Rectangle(2640, 340, 640, 400), moved.Bounds);
        Assert.NotEqual(WorkspaceId.Initial, moved.Workspace);
        Assert.Equal(new MonitorId(1), dropped.State.ActiveMonitor);
        Assert.Empty(MoveLines(dropped));
    }

    [Fact]
    public void AFloatingDragJoinsTheWorkspaceUnderItsMiddleAndKeepsTheGrab()
    {
        var core = TwoMonitors();
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });

        // The pointer is still on the left Monitor. The float's middle is 260 pixels to its right, and still on the left.
        var held = core.Handle(new PointerMoved(new Point(1600, 400)));
        Assert.Equal(WorkspaceId.Initial, Client(held, 1).Workspace);
        Assert.Equal(new Rectangle(1540, 340, 640, 400), BoundsOf(held, 1));
        Assert.Empty(MoveLines(held));

        var crossed = core.Handle(new PointerMoved(new Point(1700, 400)));
        Assert.Equal(RightWorkspace, Client(crossed, 1).Workspace);
        Assert.True(Client(crossed, 1).Floating);
        Assert.Equal(new Rectangle(1640, 340, 640, 400), BoundsOf(crossed, 1));
        Assert.Equal(new MonitorId(0), crossed.State.ActiveMonitor);
        Assert.Equal([@"movewindow>>1,1,\\.\DISPLAY2", @"movewindowv2>>1,1,1,\\.\DISPLAY2"], MoveLines(crossed));

        var followed = core.Handle(new PointerMoved(new Point(1800, 400)));
        Assert.Equal(RightWorkspace, Client(followed, 1).Workspace);
        Assert.Equal(new Rectangle(1740, 340, 640, 400), BoundsOf(followed, 1));
        Assert.Empty(MoveLines(followed));
    }

    [Fact]
    public void AFloatingDragInMasterKeepsFollowingAfterItCrosses()
    {
        var core = TwoMonitors("general:layout = master\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });
        var crossed = core.Handle(new PointerMoved(new Point(2700, 400)));

        var followed = core.Handle(new PointerMoved(new Point(2800, 400)));

        Assert.Equal(RightWorkspace, Client(crossed, 1).Workspace);
        Assert.Equal([@"movewindow>>1,1,\\.\DISPLAY2", @"movewindowv2>>1,1,1,\\.\DISPLAY2"], MoveLines(crossed));
        Assert.Equal(new Rectangle(2740, 340, 640, 400), BoundsOf(followed, 1));
        Assert.Equal(RightWorkspace, Client(followed, 1).Workspace);
        Assert.Empty(MoveLines(followed));
    }

    [Fact]
    public void AFloatingDragCrossesBackOntoTheMonitorItLeft()
    {
        var core = TwoMonitors();
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });
        core.Handle(new PointerMoved(new Point(2700, 400)));

        var back = core.Handle(new PointerMoved(new Point(100, 400)));

        Assert.Equal(WorkspaceId.Initial, Client(back, 1).Workspace);
        Assert.Equal(new Rectangle(40, 340, 640, 400), BoundsOf(back, 1));
        Assert.Equal([@"movewindow>>1,1,\\.\DISPLAY1", @"movewindowv2>>1,1,1,\\.\DISPLAY1"], MoveLines(back));

        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(100, 400) });
        Assert.Equal(WorkspaceId.Initial, Client(dropped, 1).Workspace);
        Assert.True(Client(dropped, 1).Floating);
        Assert.Equal(new MonitorId(0), dropped.State.ActiveMonitor);
        Assert.Empty(MoveLines(dropped));
    }

    [Fact]
    public void ALiftedTileDropsIntoTheLayoutItJoinedAtThePointer()
    {
        var core = TwoMonitors();
        Appear(core, 1);
        var right = new Rectangle(1920, 0, 1920, 1080);
        core.Handle(new WindowAppeared(new WindowId(2), right));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(960, 540) });

        var crossed = core.Handle(new PointerMoved(new Point(3360, 540)));
        Assert.True(Client(crossed, 1).Floating);
        Assert.Equal(RightWorkspace, Client(crossed, 1).Workspace);
        Assert.Equal([@"movewindow>>1,1,\\.\DISPLAY2", @"movewindowv2>>1,1,1,\\.\DISPLAY2"], MoveLines(crossed));
        Assert.Contains(Lines(crossed), line => line == "changefloatingmode>>1,1");

        // The grab is the middle of the 1920-wide tile, so the next move still tracks the pointer.
        var followed = core.Handle(new PointerMoved(new Point(3400, 540)));
        Assert.True(Client(followed, 1).Floating);
        Assert.Equal(new Rectangle(2440, 0, 1920, 1080), BoundsOf(followed, 1));
        Assert.Empty(MoveLines(followed));

        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(3360, 540) });
        Assert.False(Client(dropped, 1).Floating);
        Assert.False(Client(dropped, 2).Floating);
        Assert.Equal(RightWorkspace, Client(dropped, 1).Workspace);
        Assert.Equal(RightWorkspace, Client(dropped, 2).Workspace);
        Assert.Equal(new Rectangle(2880, 0, 960, 1080), BoundsOf(dropped, 1));
        Assert.Equal(new Rectangle(1920, 0, 960, 1080), BoundsOf(dropped, 2));
    }

    [Fact]
    public void AFloatingResizeJoinsTheWorkspaceUnderItsMiddleAndKeepsResizing()
    {
        var core = TwoMonitors("bindm = SUPER, mouse:273, resizewindow\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        core.Handle(new Dispatch("movewindowpixel", "exact 1500 300,address:0x1"));
        Press(core, "Super_L");
        // The bottom right corner lies on the other Monitor, which does not own the window, so the press is on the
        // bottom right and still on this Monitor. Growing the right edge by 200 puts the middle on the other one.
        core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(1900, 690) });

        var crossed = core.Handle(new PointerMoved(new Point(2100, 690)));
        Assert.Equal(new Rectangle(1500, 300, 840, 400), BoundsOf(crossed, 1));
        Assert.Equal(RightWorkspace, Client(crossed, 1).Workspace);
        Assert.Equal(new MonitorId(0), crossed.State.ActiveMonitor);
        Assert.Equal([@"movewindow>>1,1,\\.\DISPLAY2", @"movewindowv2>>1,1,1,\\.\DISPLAY2"], MoveLines(crossed));

        var grown = core.Handle(new PointerMoved(new Point(2200, 690)));
        Assert.Equal(new Rectangle(1500, 300, 940, 400), BoundsOf(grown, 1));
        Assert.Equal(RightWorkspace, Client(grown, 1).Workspace);
        Assert.Empty(MoveLines(grown));

        var dropped = core.Handle(new PointerButtonReleased("mouse:273") { Position = new Point(2200, 690) });
        Assert.Equal(RightWorkspace, Client(dropped, 1).Workspace);
        Assert.True(Client(dropped, 1).Floating);
        Assert.Equal(new MonitorId(0), dropped.State.ActiveMonitor);
    }

    [Fact]
    public void ATiledResizeDoesNotJoinAnotherMonitor()
    {
        var core = TwoMonitors("bindm = SUPER, mouse:273, resizewindow\n");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:273") { Position = new Point(960, 540) });

        var resized = core.Handle(new PointerMoved(new Point(1200, 540)));

        Assert.Equal(WorkspaceId.Initial, Client(resized, 1).Workspace);
        Assert.Equal(WorkspaceId.Initial, Client(resized, 2).Workspace);
        Assert.False(Client(resized, 1).Floating);
        Assert.Empty(MoveLines(resized));
    }

    [Fact]
    public void ADragJoinsTheSpecialWorkspaceTheOtherMonitorShows()
    {
        var core = TwoMonitors();
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        core.Handle(new Dispatch("focusmonitor", @"\\.\DISPLAY2"));
        core.Handle(new Dispatch("togglespecialworkspace", "scratch"));
        core.Handle(new Dispatch("focusmonitor", @"\\.\DISPLAY1"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });

        var crossed = core.Handle(new PointerMoved(new Point(2700, 400)));

        Assert.Equal(new WorkspaceId("special:scratch"), Client(crossed, 1).Workspace);
        Assert.True(Client(crossed, 1).Floating);
        Assert.Equal(new Rectangle(2640, 340, 640, 400), BoundsOf(crossed, 1));
        Assert.Equal(new WorkspaceId("special:scratch"), Screen(crossed, 1).SpecialWorkspace);
        Assert.Equal([@"movewindow>>1,special:scratch,\\.\DISPLAY2", @"movewindowv2>>1,-98,special:scratch,\\.\DISPLAY2"], MoveLines(crossed));
        Assert.Equal(new MonitorId(0), crossed.State.ActiveMonitor);
    }

    [Fact]
    public void ADragJoinsTheSpecialWorkspaceItsOwnMonitorOpens()
    {
        var core = Flat("bindm = SUPER, mouse:272, movewindow\n");
        core.Handle(new MonitorsChanged(
        [
            new Monitor(@"\\.\DISPLAY1", "Left", 0, 0, 1920, 1080, 1, true, Wide),
        ]));
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });
        core.Handle(new PointerMoved(new Point(750, 430)));
        core.Handle(new Dispatch("togglespecialworkspace", "scratch"));

        var joined = core.Handle(new PointerMoved(new Point(760, 440)));

        Assert.Equal(new WorkspaceId("special:scratch"), Client(joined, 1).Workspace);
        Assert.True(Client(joined, 1).Floating);
        Assert.Equal(new Rectangle(700, 380, 640, 400), BoundsOf(joined, 1));
        Assert.Equal([@"movewindow>>1,special:scratch,\\.\DISPLAY1", @"movewindowv2>>1,-98,special:scratch,\\.\DISPLAY1"], MoveLines(joined));
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

    private static WindowManager TwoMonitors(string extra = "")
    {
        var core = Flat(extra + "bindm = SUPER, mouse:272, movewindow\n");
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

    [Fact]
    public void TheMouseDispatcherOnAKeyLetsTheKeyThroughWithNoWindowUnderThePointer()
    {
        var core = Flat("bind = SUPER, Z, mouse, movewindow\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        core.Handle(new PointerMoved(new Point(10, 10)));
        Press(core, "Super_L");

        Assert.False(core.Handle(new KeyPressed("z")).Consumed);
    }

    [Fact]
    public void TheMouseDispatcherOnAKeyDragsWhileTheKeyIsHeld()
    {
        var core = Flat("bind = SUPER, Z, mouse, movewindow\n");
        Appear(core, 1);
        core.Handle(new Dispatch("togglefloating"));
        core.Handle(new PointerMoved(new Point(700, 400)));
        Press(core, "Super_L");

        var pressed = core.Handle(new KeyPressed("z"));
        var moved = core.Handle(new PointerMoved(new Point(750, 430)));
        var released = core.Handle(new KeyReleased("z"));
        var after = core.Handle(new PointerMoved(new Point(900, 600)));

        Assert.True(pressed.Consumed);
        Assert.Equal(new Rectangle(690, 370, 640, 400), BoundsOf(moved, 1));
        Assert.Null(released.ResizeCursor);
        Assert.DoesNotContain(after.Placements, placement => placement.Client == new ClientId(1));
    }

    // Dwindle with 1 on the left, 2 at the top right, and 3 and 4 side by side at the bottom right.
    private static WindowManager FourTiles(string options)
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

    private static WindowManager Flat(string binds)
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + binds)));
        return core;
    }

    private static Outcome Appear(WindowManager core, long window) =>
        core.Handle(new WindowAppeared(new WindowId(window), Wide));

    private static void Press(WindowManager core, string key) =>
        core.Handle(new KeyPressed(key));

    private static MonitorSnapshot Screen(Outcome outcome, int id) =>
        Assert.Single(outcome.State.Screens, screen => screen.Id == new MonitorId(id));

    private static IReadOnlyList<string> Lines(Outcome outcome) =>
        CommandProtocol.FormatStream(outcome.Events, outcome.State);

    private static IReadOnlyList<string> MoveLines(Outcome outcome) =>
        [.. Lines(outcome).Where(line => line.StartsWith("movewindow>>", StringComparison.Ordinal) || line.StartsWith("movewindowv2>>", StringComparison.Ordinal))];
}
