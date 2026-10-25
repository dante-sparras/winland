namespace Winland.Application.Tests;

public class BorderResizeTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);

    [Fact]
    public void DraggingABorderResizesTheFloatingWindow()
    {
        var core = CoreWith("""
            general:resize_on_border = true
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
            general:border_size = 0
            """);
        Float(core, 1);
        var inside = core.Handle(new PointerMoved(new Point(1279, 540)));
        var reached = core.Handle(new PointerMoved(new Point(1294, 540)));
        var beyond = core.Handle(new PointerMoved(new Point(1295, 540)));

        Assert.Null(inside.ResizeCursor);
        Assert.Equal("right_side", reached.ResizeCursor);
        Assert.Null(beyond.ResizeCursor);

        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1294, 540) });
        var resized = core.Handle(new PointerMoved(new Point(1334, 540)));

        Assert.Equal(new Rectangle(640, 340, 680, 400), BoundsOf(resized, 1));
    }

    [Fact]
    public void CursorChoiceChangesOverTheBorder()
    {
        var core = CoreWith("""
            general:resize_on_border = true
            general:border_size = 0
            """);
        Float(core, 1);

        Assert.Equal("right_side", core.Handle(new PointerMoved(new Point(1280, 540))).ResizeCursor);
        Assert.Equal("top_side", core.Handle(new PointerMoved(new Point(960, 339))).ResizeCursor);
        Assert.Equal("top_right_corner", core.Handle(new PointerMoved(new Point(1280, 339))).ResizeCursor);
        Assert.Null(core.Handle(new PointerMoved(new Point(700, 400))).ResizeCursor);
    }

    [Fact]
    public void APressInsideTheWindowDoesNotResizeIt()
    {
        var core = CoreWith("""
            general:resize_on_border = true
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
        var core = CoreWith("general:resize_on_border = true\n");
        core.Handle(new MonitorsChanged(
        [
            new Monitor(@"\\.\DISPLAY1", "Left", 0, 0, 1920, 1080, 1, true, Wide),
            new Monitor(@"\\.\DISPLAY2", "Right", 1920, 0, 1920, 1080, 1, true, new Rectangle(1920, 0, 1920, 1080)),
        ]));
        Float(core, 1);
        core.Handle(new Dispatch("focusmonitor", "1"));

        Assert.Equal("right_side", core.Handle(new PointerMoved(new Point(1290, 540))).ResizeCursor);
    }

    [Fact]
    public void AFloatingWindowAllowedOverAFullscreenOneKeepsItsRing()
    {
        var core = CoreWith("general:resize_on_border = true\nwindowrule = float, class:^(box)$\n");
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
}
