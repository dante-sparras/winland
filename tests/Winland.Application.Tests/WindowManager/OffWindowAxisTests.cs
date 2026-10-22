namespace Winland.Application.Tests;

public class OffWindowAxisTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData("ignore", 0)]
    [InlineData("send", 1)]
    [InlineData("clamp", 2)]
    [InlineData("warp", 3)]
    [InlineData("WARP", 3)]
    [InlineData("0", 0)]
    [InlineData("3", 3)]
    [InlineData("8", 3)]
    [InlineData("-1", 0)]
    public void TheModeTakesItsNamesAndClampsItsNumber(string value, int mode) =>
        Assert.Equal(mode, InputOptions.From(ConfigParser.Parse($"input:off_window_axis_events = {value}\n")).OffWindowAxisEvents);

    [Fact]
    public void SendIsTheDefaultAndAnUnreadableValueKeepsIt()
    {
        Assert.Equal(1, InputOptions.Documented.OffWindowAxisEvents);
        Assert.Equal(1, InputOptions.From(ConfigParser.Parse("input:off_window_axis_events = never\n")).OffWindowAxisEvents);
        Assert.Equal(1, new WindowManager().OffWindowAxisMode);
    }

    [Fact]
    public void TheModeReachesTheCore()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("input:off_window_axis_events = clamp\n")));

        Assert.Equal(2, core.OffWindowAxisMode);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(100, 50, 100, 50)]
    [InlineData(-30, 50, 0, 50)]
    [InlineData(130, 50, 109, 50)]
    [InlineData(50, -5, 50, 0)]
    [InlineData(50, 700, 50, 59)]
    [InlineData(500, 700, 109, 59)]
    public void ThePointIsClampedOnePixelInFromTheFarEdges(int x, int y, int clampedX, int clampedY) =>
        Assert.Equal(new Point(clampedX, clampedY), BorderPointer.Clamp(new Rectangle(0, 0, 110, 60), new Point(x, y)));

    [Fact]
    public void ABoxWithNoSizeClampsToItsCorner() =>
        Assert.Equal(new Point(5, 6), BorderPointer.Clamp(new Rectangle(5, 6, 0, 0), new Point(50, 60)));

    [Theory]
    [InlineData(2, false, 15, 2)]
    [InlineData(2, true, 15, 17)]
    [InlineData(0, true, 15, 15)]
    [InlineData(0, false, 15, 0)]
    public void TheReachIsTheBorderAndWithResizeOnBorderTheGrabArea(int border, bool resize, int extend, int reach)
    {
        var options = new LayoutOptions { BorderSize = border, ResizeOnBorder = resize, ExtendBorderGrabArea = extend };

        Assert.Equal(reach, BorderPointer.AxisReach(options));
    }

    [Fact]
    public void AFloatingWindowsRingIsItsOffWindowTargetAndTheNearestPointInsideIsClamped()
    {
        var core = CoreWith("general:border_size = 4\n");
        Float(core, 1);

        // The window fills 640..1280 by 340..740. The ring is the 4 pixels of its border.
        var target = core.OffWindowTarget(new Point(1282, 500));

        Assert.Equal(new OffWindowScroll(new WindowId(1), new Point(1279, 500)), target);
        Assert.Equal(new OffWindowScroll(new WindowId(1), new Point(1279, 739)), core.OffWindowTarget(new Point(1283, 743)));
    }

    [Fact]
    public void InsideTheWindowBeyondTheRingOrWithNoBorderThereIsNoTarget()
    {
        var core = CoreWith("general:border_size = 4\n");
        Float(core, 1);

        Assert.Null(core.OffWindowTarget(new Point(900, 500)));
        Assert.Null(core.OffWindowTarget(new Point(1290, 500)));
        Assert.Null(core.OffWindowTarget(new Point(10, 10)));

        var bare = CoreWith("general:border_size = 0\n");
        Float(bare, 1);
        Assert.Null(bare.OffWindowTarget(new Point(1280, 500)));
    }

    [Fact]
    public void ResizeOnBorderWidensTheRingByTheGrabArea()
    {
        var core = CoreWith("general:border_size = 4\ngeneral:resize_on_border = true\ngeneral:extend_border_grab_area = 10\n");
        Float(core, 1);

        Assert.Equal(new OffWindowScroll(new WindowId(1), new Point(1279, 500)), core.OffWindowTarget(new Point(1290, 500)));
        Assert.Null(core.OffWindowTarget(new Point(1294, 500)));
    }

    [Fact]
    public void TheScrollOverrideIsTheWindowsUnderThePointerOrInItsRing()
    {
        var core = CoreWith("general:border_size = 4\n");
        Float(core, 1);
        Assert.Equal(default, core.ScrollOverrideAt(new Point(900, 500)));

        core.Execute("setprop address:0x1 scroll_mouse 2.5");
        core.Execute("setprop address:0x1 scroll_touchpad 0.5");

        Assert.Equal(new ScrollOverride(2.5, 0.5), core.ScrollOverrideAt(new Point(900, 500)));
        Assert.Equal(new ScrollOverride(2.5, 0.5), core.ScrollOverrideAt(new Point(1282, 500)));
        Assert.Equal(default, core.ScrollOverrideAt(new Point(10, 10)));
    }

    [Fact]
    public void AScrollRuleIsTheWindowsOverride()
    {
        var core = CoreWith("windowrulev2 = scroll_mouse 3, class:.*\n");
        Float(core, 1);

        Assert.Equal(new ScrollOverride(3, null), core.ScrollOverrideAt(new Point(900, 500)));
    }

    private static WindowManager CoreWith(string config)
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\n" + config)));
        return core;
    }

    private static void Float(WindowManager core, long window)
    {
        core.Handle(new WindowAppeared(new WindowId(window), Wide));
        core.Handle(new Dispatch("togglefloating"));
    }
}
