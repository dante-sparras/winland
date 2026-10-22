namespace Winland.Application.Tests;

public class BorderResizeTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);

    [Fact]
    public void DraggingABorderResizesTheFloatingWindow()
    {
        var core = CoreWith("""
            general:resize_on_border = true
            general:extend_border_grab_area = 15
            general:border_size = 0
            """);
        Float(core, 1);
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1280, 540) });

        var resized = core.Handle(new PointerMoved(new Point(1320, 570)));

        Assert.Equal("resizewindow", resized.Dispatcher);
        Assert.True(resized.Consumed);
        Assert.Equal(new Rectangle(640, 340, 680, 430), BoundsOf(resized, 1));
        Assert.True(resized.Live);
    }

    // Winland tests floating windows, with their grab rings, before the tiles under them.
    [Fact]
    public void AFloatingBorderOverATileResizesTheFloatingWindow()
    {
        var core = CoreWith("""
            general:resize_on_border = true
            general:extend_border_grab_area = 15
            general:border_size = 0
            """);
        Appear(core, 1);
        Float(core, 2);
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1765, 540) });

        var resized = core.Handle(new PointerMoved(new Point(1805, 570)));

        Assert.Equal("resizewindow", resized.Dispatcher);
        Assert.True(resized.Consumed);
        Assert.Equal(new Rectangle(1120, 340, 680, 430), BoundsOf(resized, 2));
    }

    [Fact]
    public void GrabAreaExtendsPastTheBorderAndStopsAtItsEdge()
    {
        var core = CoreWith("""
            general:resize_on_border = true
            general:extend_border_grab_area = 30
            general:border_size = 0
            """);
        Float(core, 1);
        var inside = core.Handle(new PointerMoved(new Point(1279, 540)));
        var reached = core.Handle(new PointerMoved(new Point(1305, 540)));
        var beyond = core.Handle(new PointerMoved(new Point(1310, 540)));

        Assert.Null(inside.ResizeCursor);
        Assert.Equal("right_side", reached.ResizeCursor);
        Assert.Null(beyond.ResizeCursor);

        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1305, 540) });
        var resized = core.Handle(new PointerMoved(new Point(1345, 540)));

        Assert.Equal(new Rectangle(640, 340, 680, 400), BoundsOf(resized, 1));
    }

    [Fact]
    public void ResizeCornerForcesTheFloatingGrab()
    {
        var core = CoreWith("""
            general:resize_on_border = true
            general:extend_border_grab_area = 15
            general:border_size = 0
            general:resize_corner = top_left
            """);
        Float(core, 1);
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1280, 540) });

        var resized = core.Handle(new PointerMoved(new Point(1320, 570)));

        Assert.Equal(new Rectangle(680, 370, 600, 370), BoundsOf(resized, 1));
    }

    [Fact]
    public void CursorChoiceChangesOverTheBorder()
    {
        var core = CoreWith("""
            general:resize_on_border = true
            general:extend_border_grab_area = 15
            general:border_size = 0
            general:hover_icon_on_border = true
            """);
        Float(core, 1);

        Assert.Equal("right_side", core.Handle(new PointerMoved(new Point(1280, 540))).ResizeCursor);
        Assert.Equal("top_side", core.Handle(new PointerMoved(new Point(960, 339))).ResizeCursor);
        Assert.Equal("top_right_corner", core.Handle(new PointerMoved(new Point(1280, 339))).ResizeCursor);
        Assert.Null(core.Handle(new PointerMoved(new Point(700, 400))).ResizeCursor);
    }

    [Fact]
    public void HoverIconCanBeTurnedOffWithoutTurningOffTheResize()
    {
        var core = CoreWith("""
            general:resize_on_border = true
            general:extend_border_grab_area = 15
            general:border_size = 0
            general:hover_icon_on_border = false
            """);
        Float(core, 1);

        var hover = core.Handle(new PointerMoved(new Point(1280, 540)));
        Assert.Null(hover.ResizeCursor);

        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1280, 540) });
        var resized = core.Handle(new PointerMoved(new Point(1320, 540)));

        Assert.Equal(new Rectangle(640, 340, 680, 400), BoundsOf(resized, 1));
        Assert.Null(resized.ResizeCursor);
    }

    [Fact]
    public void APressInsideTheWindowDoesNotResizeIt()
    {
        var core = CoreWith("""
            general:resize_on_border = true
            general:extend_border_grab_area = 15
            general:border_size = 0
            """);
        Float(core, 1);
        var pressed = core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });
        var moved = core.Handle(new PointerMoved(new Point(760, 430)));

        Assert.False(pressed.Consumed);
        Assert.Equal(new Rectangle(640, 340, 640, 400), Client(moved, 1).Bounds);
    }

    [Fact]
    public void DraggingTheOuterEdgeResizesATiledWindow()
    {
        var core = CoreWith("""
            general:resize_on_border = true
            general:extend_border_grab_area = 20
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 0
            """);
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(-5, 100) });

        var resized = core.Handle(new PointerMoved(new Point(95, 100)));

        Assert.Equal(new Rectangle(0, 0, 1060, 1080), BoundsOf(resized, 1));
        Assert.Equal(new Rectangle(1060, 0, 860, 1080), BoundsOf(resized, 2));
    }

    [Fact]
    public void FloatingSnapPullsAWindowToItsNeighborAndToTheMonitor()
    {
        var core = CoreWith("""
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 0
            general:snap:enabled = true
            general:snap:window_gap = 20
            general:snap:monitor_gap = 0
            bindm = SUPER, mouse:272, movewindow
            """);
        Float(core, 1);
        Float(core, 2);
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });
        var parked = core.Handle(new PointerMoved(new Point(60, 400)));

        Assert.Equal(new Rectangle(0, 340, 640, 400), BoundsOf(parked, 2));

        core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(60, 400) });
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(30, 400) });
        var snapped = core.Handle(new PointerMoved(new Point(45, 400)));
        Assert.Equal(new Rectangle(0, 340, 640, 400), BoundsOf(snapped, 2));

        core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(45, 400) });
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(30, 400) });
        var clear = core.Handle(new PointerMoved(new Point(70, 400)));
        Assert.Equal(new Rectangle(40, 340, 640, 400), BoundsOf(clear, 2));
    }

    [Fact]
    public void MonitorSnapPullsAFloatingWindowToTheEdge()
    {
        var core = CoreWith("""
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 0
            general:snap:enabled = true
            general:snap:window_gap = 0
            general:snap:monitor_gap = 15
            bindm = SUPER, mouse:272, movewindow
            """);
        Float(core, 1);
        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(700, 400) });

        var snapped = core.Handle(new PointerMoved(new Point(68, 400)));

        Assert.Equal(new Rectangle(0, 340, 640, 400), BoundsOf(snapped, 1));
    }

    [Fact]
    public void FloatGapsKeepAFloatingWindowInsideTheInset()
    {
        var core = CoreWith("""
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 0
            general:float_gaps = 500
            misc:float_force_onscreen = 2
            """);
        Appear(core, 1);

        var floated = core.Handle(new Dispatch("togglefloating"));

        Assert.Equal(new Rectangle(640, 180, 640, 400), BoundsOf(floated, 1));
    }

    [Fact]
    public void ANegativeFloatGapUsesTheOuterGaps()
    {
        var stayed = CoreWith("""
            general:gaps_in = 0
            general:gaps_out = 500
            general:border_size = 0
            general:float_gaps = 0
            """);
        Appear(stayed, 1);
        var open = stayed.Handle(new Dispatch("togglefloating"));
        Assert.Equal(new Rectangle(640, 340, 640, 400), BoundsOf(open, 1));

        var core = CoreWith("""
            general:gaps_in = 0
            general:gaps_out = 500
            general:border_size = 0
            general:float_gaps = -1
            misc:float_force_onscreen = 2
            """);
        Appear(core, 1);
        var floated = core.Handle(new Dispatch("togglefloating"));

        Assert.Equal(new Rectangle(640, 180, 640, 400), BoundsOf(floated, 1));
    }

    [Fact]
    public void BorderAndSnapKeywordsAreRead()
    {
        var document = ConfigParser.Parse("""
            general {
                resize_on_border = true
                extend_border_grab_area = 20
                hover_icon_on_border = false
                resize_corner = bottom_left
                float_gaps = 4 8
                snap {
                    enabled = true
                    window_gap = 12
                    monitor_gap = 6
                    border_overlap = true
                    respect_gaps = true
                }
            }
            """);

        Assert.Empty(document.Warnings);
        var options = LayoutOptions.From(document);
        Assert.True(options.ResizeOnBorder);
        Assert.Equal(20, options.ExtendBorderGrabArea);
        Assert.False(options.HoverIconOnBorder);
        Assert.Equal(4, options.ResizeCorner);
        Assert.Equal(new EdgeInsets(4, 8, 4, 8), options.FloatGaps);
        Assert.True(options.SnapEnabled);
        Assert.Equal(12, options.SnapWindowGap);
        Assert.Equal(6, options.SnapMonitorGap);
        Assert.True(options.SnapBorderOverlap);
        Assert.True(options.SnapRespectGaps);
    }

    [Fact]
    public void SnapRespectsGapsAndBorderOverlap()
    {
        var work = new Rectangle(0, 0, 1920, 1080);
        var options = LayoutOptions.From(ConfigParser.Parse("""
            general:border_size = 0
            general:gaps_in = 5
            general:gaps_out = 10
            general:snap:enabled = true
            general:snap:window_gap = 30
            general:snap:monitor_gap = 0
            general:snap:respect_gaps = true
            """));
        var snapped = FloatingSnap.Apply(
            new Rectangle(80, 100, 100, 100),
            SnapEdges.All,
            resize: false,
            [new Rectangle(200, 100, 100, 100)],
            work,
            work,
            options);

        Assert.Equal(new Rectangle(90, 100, 100, 100), snapped);

        var overlap = LayoutOptions.From(ConfigParser.Parse("""
            general:border_size = 1
            general:snap:enabled = true
            general:snap:window_gap = 0
            general:snap:monitor_gap = 20
            general:snap:border_overlap = true
            """));
        var hung = FloatingSnap.Apply(
            new Rectangle(5, 100, 100, 100),
            SnapEdges.All,
            resize: false,
            [],
            work,
            work,
            overlap);

        Assert.Equal(0, hung.X);
    }

    [Fact]
    public void APressOverTilesHiddenByAnOpenSpecialWorkspaceDoesNotResizeThem()
    {
        var core = CoreWith("general:resize_on_border = true\ngeneral:gaps_in = 20\n");
        Appear(core, 1);
        var gap = Gap(core.Handle(new WindowAppeared(new WindowId(2), Wide)));
        core.Handle(new Dispatch("togglespecialworkspace", "scratch"));
        Appear(core, 3);

        var pressed = core.Handle(new PointerButtonPressed("mouse:272") { Position = gap });

        Assert.False(pressed.Consumed);
        Assert.NotEqual("resizewindow", pressed.Dispatcher);
        Assert.NotEqual(new WindowId(1), pressed.Focus);
        Assert.Null(core.Handle(new PointerMoved(gap)).ResizeCursor);
    }

    [Fact]
    public void TheBordersOfAnOpenSpecialWorkspaceResize()
    {
        var core = CoreWith("general:resize_on_border = true\ngeneral:gaps_in = 20\n");
        core.Handle(new Dispatch("togglespecialworkspace", "scratch"));
        Appear(core, 1);
        var gap = Gap(core.Handle(new WindowAppeared(new WindowId(2), Wide)));

        var pressed = core.Handle(new PointerButtonPressed("mouse:272") { Position = gap });

        Assert.True(pressed.Consumed);
        Assert.Equal("resizewindow", pressed.Dispatcher);
        Assert.Equal(new WindowId(1), pressed.Focus);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    public void ACoveringClientTakesNoBorderResizeAndHidesTheBordersBehindIt(string mode)
    {
        var core = CoreWith("general:resize_on_border = true\ngeneral:gaps_in = 20\n");
        Appear(core, 1);
        var gap = Gap(core.Handle(new WindowAppeared(new WindowId(2), Wide)));
        core.Handle(new Dispatch("fullscreen", mode));

        var pressed = core.Handle(new PointerButtonPressed("mouse:272") { Position = gap });

        Assert.False(pressed.Consumed);
        Assert.NotEqual("resizewindow", pressed.Dispatcher);
    }

    [Fact]
    public void TheRingIsLookedForOnTheMonitorUnderThePointer()
    {
        var core = CoreWith("general:resize_on_border = true\ngeneral:extend_border_grab_area = 30\n");
        core.Handle(new MonitorsChanged(
        [
            new Monitor(@"\\.\DISPLAY1", "Left", 0, 0, 1920, 1080, 1, true, Wide),
            new Monitor(@"\\.\DISPLAY2", "Right", 1920, 0, 1920, 1080, 1, true, new Rectangle(1920, 0, 1920, 1080)),
        ]));
        Float(core, 1);
        core.Handle(new Dispatch("focusmonitor", "1"));

        Assert.Equal("right_side", core.Handle(new PointerMoved(new Point(1305, 540))).ResizeCursor);
    }

    [Fact]
    public void AFloatingWindowAllowedOverAFullscreenOneKeepsItsRing()
    {
        var core = CoreWith("general:resize_on_border = true\ngeneral:extend_border_grab_area = 30\nwindowrulev2 = float, class:^(box)$\n");
        Appear(core, 1);
        core.Handle(new Dispatch("fullscreen", "0"));
        core.Handle(new WindowAppeared(new WindowId(2), Wide)
        {
            Traits = WindowTraits.Normal with { Class = "box" },
            Bounds = new Rectangle(0, 0, 640, 400),
        });

        var box = Client(core.Handle(new Dispatch("no_op")), 2).Bounds;

        Assert.Equal("right_side", core.Handle(new PointerMoved(new Point(box.X + box.Width + 10, box.Y + (box.Height / 2)))).ResizeCursor);
    }

    private static Point Gap(Outcome laid)
    {
        var left = Client(laid, 1).Bounds;
        var right = Client(laid, 2).Bounds;
        Assert.True(right.X > left.X + left.Width, "The tiles must leave a gap.");
        return new Point(left.X + left.Width + 5, left.Y + (left.Height / 2));
    }

    private static WindowManager CoreWith(string config)
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + config)));
        return core;
    }

    private static void Float(WindowManager core, long window)
    {
        Appear(core, window);
        core.Handle(new Dispatch("togglefloating"));
    }

    private static void Appear(WindowManager core, long window) =>
        core.Handle(new WindowAppeared(new WindowId(window), Wide));

    private static void Press(WindowManager core, string key) =>
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
