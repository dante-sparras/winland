using Winland.Core;

namespace Winland.Core.Tests;

public class GeometryDispatcherTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);
    private static readonly Rectangle Centered = new(860, 465, 200, 150);

    [Theory]
    [InlineData("movewindowpixel")]
    [InlineData("resizewindowpixel")]
    [InlineData("centerwindow")]
    [InlineData("bringactivetotop")]
    [InlineData("alterzorder")]
    [InlineData("movecursortocorner")]
    public void TheDispatchersAreKnown(string name) => Assert.True(Dispatchers.IsKnown(name));

    [Theory]
    [InlineData("10 -20,address:0x1", 870, 445)]
    [InlineData("exact 0 0,address:0x1", 0, 0)]
    [InlineData("exact 50% 25%,address:0x1", 960, 270)]
    [InlineData("10% 0,address:0x1", 946, 465)]
    [InlineData("left 0,address:0x1", 860, 465)]
    public void MoveWindowPixelMovesAFloatingClient(string arguments, int x, int y)
    {
        var core = OneFloat();

        var outcome = core.Handle(new Dispatch("movewindowpixel", arguments));

        Assert.Null(outcome.Error);
        Assert.Equal(Centered with { X = x, Y = y }, BoundsOf(outcome, 1));
    }

    [Fact]
    public void MoveWindowPixelLeavesATiledClientAndRejectsAMissingWindow()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var tiled = core.Handle(new Dispatch("movewindowpixel", "10 10,address:0x1"));
        var missing = core.Handle(new Dispatch("movewindowpixel", "10 10,address:0x9"));

        Assert.Null(tiled.Error);
        Assert.Empty(tiled.Placements);
        Assert.Equal("error: moveWindow: no window", missing.Error);
    }

    [Fact]
    public void MoveWindowPixelRejectsAFullscreenClient()
    {
        var core = OneFloat();
        core.Handle(new Dispatch("fullscreen", "0"));

        var outcome = core.Handle(new Dispatch("movewindowpixel", "10 10,address:0x1"));

        Assert.Equal("error: Window is fullscreen", outcome.Error);
    }

    [Fact]
    public void ResizeWindowPixelGrowsAFloatingClientFromItsCenter()
    {
        var core = OneFloat();

        var exact = core.Handle(new Dispatch("resizewindowpixel", "exact 400 300,address:0x1"));
        var relative = core.Handle(new Dispatch("resizewindowpixel", "-200 -150,address:0x1"));

        Assert.Equal(new Rectangle(760, 390, 400, 300), BoundsOf(exact, 1));
        Assert.Equal(Centered, BoundsOf(relative, 1));
    }

    [Fact]
    public void ResizeWindowPixelRejectsASizeBelowOnePixel()
    {
        var core = OneFloat();

        var outcome = core.Handle(new Dispatch("resizewindowpixel", "exact 0 100,address:0x1"));

        Assert.Equal("error: Invalid size", outcome.Error);
        Assert.Empty(outcome.Placements);
    }

    [Fact]
    public void ResizeWindowPixelResizesATiledClientsSplit()
    {
        var core = new WindowManager(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var outcome = core.Handle(new Dispatch("resizewindowpixel", "100 0,address:0x1"));

        Assert.Equal(new Rectangle(0, 0, 1060, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(1060, 0, 860, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void CenterWindowPutsTheActiveFloatBackInTheMiddleOfTheWorkArea()
    {
        var core = OneFloat();
        core.Handle(new Dispatch("movewindowpixel", "exact 0 0,address:0x1"));

        var outcome = core.Handle(new Dispatch("centerwindow"));

        Assert.Equal(Centered, BoundsOf(outcome, 1));
    }

    [Fact]
    public void CenterWindowRejectsATiledClient()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new Dispatch("centerwindow"));

        Assert.Equal("error: No floating window found", outcome.Error);
        Assert.Empty(outcome.Placements);
    }

    [Fact]
    public void BringActiveToTopRaisesTheActiveClient()
    {
        var core = TwoFloats();

        var outcome = core.Handle(new Dispatch("bringactivetotop"));

        Assert.Equal([new ZOrderRequest(new WindowId(2), true)], outcome.ZOrder);
    }

    [Fact]
    public void AlterZOrderRaisesOrLowersTheChosenWindow()
    {
        var core = TwoFloats();

        var lowered = core.Handle(new Dispatch("alterzorder", "bottom,address:0x1"));
        var raised = core.Handle(new Dispatch("alterzorder", "top,address:0x1"));

        Assert.Equal([new ZOrderRequest(new WindowId(1), false)], lowered.ZOrder);
        Assert.Equal([new ZOrderRequest(new WindowId(1), true)], raised.ZOrder);
    }

    [Fact]
    public void LoweringTheWindowUnderThePointerFocusesTheOneNowUnderIt()
    {
        var core = TwoFloats();
        core.Handle(new PointerMoved(new Point(960, 540)));

        var lowered = core.Handle(new Dispatch("alterzorder", "bottom"));

        Assert.Equal([new ZOrderRequest(new WindowId(2), false)], lowered.ZOrder);
        Assert.Equal(new WindowId(1), lowered.Focus);
        Assert.Equal("alterzorder", lowered.Dispatcher);
    }

    [Fact]
    public void WithFollowMouseOffTheFocusStaysAfterAZOrderChange()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = float, class:^(box)$\ninput:follow_mouse = 0\n"));
        core.Handle(Box(1));
        core.Handle(Box(2));
        core.Handle(new PointerMoved(new Point(960, 540)));

        Assert.Null(core.Handle(new Dispatch("alterzorder", "bottom")).Focus);
    }

    [Fact]
    public void AlterZOrderWithoutAWindowUsesTheActiveFloat()
    {
        var core = TwoFloats();

        var outcome = core.Handle(new Dispatch("alterzorder", "bottom"));

        Assert.Equal([new ZOrderRequest(new WindowId(2), false)], outcome.ZOrder);
    }

    [Fact]
    public void AlterZOrderWithoutAWindowLeavesAnActiveTiledClient()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new Dispatch("alterzorder", "top"));

        Assert.Empty(outcome.ZOrder);
        Assert.Null(outcome.Error);
    }

    [Fact]
    public void AlterZOrderRejectsAnUnknownPosition()
    {
        var core = TwoFloats();

        var outcome = core.Handle(new Dispatch("alterzorder", "middle,address:0x1"));

        Assert.Equal("error: Bad z-order position: middle", outcome.Error);
        Assert.Empty(outcome.ZOrder);
    }

    [Fact]
    public void ZOrderRequestsFromABindAreKept()
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("""
            windowrulev2 = float, class:^(box)$
            bind = SUPER, Z, alterzorder, bottom,address:0x1
            bind = SUPER, Z, bringactivetotop
            """)));
        core.Handle(Box(1));
        core.Handle(Box(2));

        core.Handle(new KeyPressed("Super_L"));
        var outcome = core.Handle(new KeyPressed("Z"));

        Assert.Equal(
            [new ZOrderRequest(new WindowId(1), false), new ZOrderRequest(new WindowId(2), true)],
            outcome.ZOrder);
    }

    [Theory]
    [InlineData("0", 860, 615)]
    [InlineData("1", 1060, 615)]
    [InlineData("2", 1060, 465)]
    [InlineData("3", 860, 465)]
    public void MoveCursorToCornerWarpsToTheActiveClientsCorner(string corner, int x, int y)
    {
        var core = OneFloat();

        var outcome = core.Handle(new Dispatch("movecursortocorner", corner));

        Assert.Equal(new WarpTo(new Point(x, y)), outcome.Warp);
    }

    [Theory]
    [InlineData("4", "error: Corner must be 0 - 3")]
    [InlineData("top", "error: moveCursorToCorner, arg has to be a number")]
    public void MoveCursorToCornerRejectsABadCorner(string corner, string error)
    {
        var core = OneFloat();

        var outcome = core.Handle(new Dispatch("movecursortocorner", corner));

        Assert.Equal(error, outcome.Error);
        Assert.Null(outcome.Warp);
    }

    [Fact]
    public void AWindowAfterACommaIsActedOnInPlaceOfTheActiveOne()
    {
        var core = TwoFloats();
        core.Handle(new Dispatch("movewindowpixel", "exact 0 0,address:0x1"));

        var centered = core.Handle(new Dispatch("centerwindow", ",address:0x1"));
        Assert.Equal(Centered, BoundsOf(centered, 1));

        var warped = core.Handle(new Dispatch("movecursortocorner", "3,address:0x1"));
        Assert.Equal(new WarpTo(new Point(860, 465)), warped.Warp);
        Assert.Null(warped.Focus);

        var full = core.Handle(new Dispatch("fullscreen", "0,address:0x1"));
        Assert.Equal(FullscreenMode.Fullscreen, Assert.Single(full.State.Clients, client => client.Window == new WindowId(1)).Fullscreen);
        Assert.Equal(FullscreenMode.None, Assert.Single(full.State.Clients, client => client.Window == new WindowId(2)).Fullscreen);
    }

    [Fact]
    public void AWindowThatMatchesNothingIsAnError()
    {
        var core = TwoFloats();

        var outcome = core.Handle(new Dispatch("centerwindow", ",address:0x9"));

        Assert.Equal("error: window not found", outcome.Error);
        Assert.Empty(outcome.Placements);
    }

    [Fact]
    public void ABindPassesTheWindowAfterTheComma()
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("""
            windowrulev2 = float, class:^(box)$
            bind = SUPER, Z, togglegroup, ,address:0x1
            """)));
        core.Handle(Box(1));
        core.Handle(Box(2));

        core.Handle(new KeyPressed("Super_L"));
        core.Handle(new KeyPressed("Z"));

        Assert.Contains("\"grouped\":[\"0x1\"]", core.Execute("j/clients").Text, StringComparison.Ordinal);
    }

    private static WindowManager OneFloat()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = float, class:^(box)$\n"));
        core.Handle(Box(1));
        return core;
    }

    private static WindowManager TwoFloats()
    {
        var core = OneFloat();
        core.Handle(Box(2));
        return core;
    }

    private static WindowAppeared Box(long window) =>
        new(new WindowId(window), Area)
        {
            Traits = WindowTraits.Normal with { Class = "box" },
            Bounds = new Rectangle(5, 7, 200, 150),
        };

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        return Assert.Single(outcome.Placements, placement => placement.Client == client.Id).Bounds;
    }
}
