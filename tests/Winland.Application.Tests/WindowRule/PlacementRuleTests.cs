namespace Winland.Application.Tests;

public class PlacementRuleTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);
    private static readonly Rectangle Left = new(0, 0, 1920, 1080);
    private static readonly Rectangle Right = new(1920, 0, 1920, 1080);
    private static readonly Rectangle Own = new(5, 7, 200, 150);

    [Fact]
    public void ANewFloatOpensCenteredInTheFloatingWorkArea()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("general:float_gaps = 20 40 60 80\n")));
        var area = new Rectangle(100, 0, 1820, 1040);

        var outcome = core.Handle(Dialog(1, area));

        // The floating work area is 180..1880 by 20..980, so its center is (1030, 500).
        Assert.Equal(new Rectangle(930, 425, 200, 150), BoundsOf(outcome, 1));
    }

    [Fact]
    public void AFloatWithoutASizeOpensAtTheDefaultSizeCentered()
    {
        var core = new WindowManager();

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Dialog = true },
        });

        Assert.Equal(new Rectangle(640, 340, 640, 400), BoundsOf(outcome, 1));
    }

    [Fact]
    public void SizeSetsTheFloatSizeAndTheFloatStaysCentered()
    {
        var core = Ruled("size 800 600");

        var outcome = core.Handle(Box(1));

        Assert.Equal(new Rectangle(560, 240, 800, 600), BoundsOf(outcome, 1));
    }

    [Fact]
    public void SizeExpressionsReadTheMonitorAndTheWindow()
    {
        var core = Ruled("size monitor_w*0.5 window_h+(10*2)");

        var outcome = core.Handle(Box(1));

        Assert.Equal(new Rectangle(480, 455, 960, 170), BoundsOf(outcome, 1));
    }

    [Fact]
    public void MoveIsMonitorLocalAndAcceptsMinusValues()
    {
        // Without misc:new_float_force_onscreen the rule's position stands, even partly off the Monitor.
        var core = Configured("""
            misc:new_float_force_onscreen = 0
            windowrulev2 = float, class:^(box)$
            windowrulev2 = move monitor_w-window_w-10 -5, class:^(box)$
            """);
        core.Handle(new MonitorsChanged([Display(@"\\.\DISPLAY1", "Dell", Left), Display(@"\\.\DISPLAY2", "Samsung", Right)]));

        var outcome = core.Handle(Box(1, Right));

        Assert.Equal(new Rectangle(1920 + 1920 - 200 - 10, -5, 200, 150), BoundsOf(outcome, 1));
    }

    [Fact]
    public void AMovedNewFloatIsKeptOnScreenByDefault()
    {
        var core = Ruled("move -50 -5");

        var outcome = core.Handle(Box(1));

        Assert.Equal(new Rectangle(0, 0, 200, 150), BoundsOf(outcome, 1));
    }

    [Fact]
    public void KeepingANewFloatOnScreenLeavesItsBorderOut()
    {
        var core = Configured("""
            general:border_size = 3
            windowrulev2 = float, class:^(box)$
            windowrulev2 = move 1900 1000, class:^(box)$
            """);

        var outcome = core.Handle(Box(1));

        Assert.Equal(new Rectangle(1920 - 200, 1080 - 150, 200, 150), BoundsOf(outcome, 1));
    }

    [Theory]
    [InlineData(0, -500, -500)]
    [InlineData(1, -100, -75)]
    [InlineData(2, 0, 0)]
    public void FloatForceOnscreenKeepsAMovedFloatOnScreen(int mode, int x, int y)
    {
        var core = Configured($"""
            general:border_size = 0
            misc:float_force_onscreen = {mode}
            windowrulev2 = float, class:^(box)$
            """);
        core.Handle(Box(1));

        var moved = core.Execute("dispatch movewindowpixel exact -500 -500,class:box").Effect;

        Assert.Equal(new Rectangle(x, y, 200, 150), BoundsOf(moved, 1));
        Assert.Equal($"{{\"option\":\"misc:float_force_onscreen\",\"int\":{mode},\"set\":true}}", core.Execute("getoption misc:float_force_onscreen").Text);
    }

    [Fact]
    public void MoveCanCenterTheWindowOnTheCursor()
    {
        var core = Ruled("move cursor_x-(window_w*0.5) cursor_y-(window_h*0.5)");

        var outcome = core.Handle(Box(1) with { Pointer = new Point(300, 400) });

        Assert.Equal(new Rectangle(200, 325, 200, 150), BoundsOf(outcome, 1));
    }

    [Fact]
    public void MoveUsesTheSizeFromASizeRule()
    {
        var core = Ruled("size 400 300", "move monitor_w-window_w 0");

        var outcome = core.Handle(Box(1));

        Assert.Equal(new Rectangle(1520, 0, 400, 300), BoundsOf(outcome, 1));
    }

    [Fact]
    public void CenterAndMoveReplaceEachOtherAndTheLastWins()
    {
        var centered = Ruled("move 10 10", "center").Handle(Box(1));
        var moved = Ruled("center", "move 10 10").Handle(Box(1));

        Assert.Equal(new Rectangle(860, 465, 200, 150), BoundsOf(centered, 1));
        Assert.Equal(new Rectangle(10, 10, 200, 150), BoundsOf(moved, 1));
    }

    [Fact]
    public void MoveAndSizeOnlyApplyToFloatingClientsAndSizeIsThePseudoSize()
    {
        var core = new WindowManager(ConfigParser.Parse("""
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 0
            windowrulev2 = move 10 10, class:^(box)$
            windowrulev2 = size 400 300, class:^(box)$
            """));

        var tiled = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Class = "box" },
            Bounds = Own,
        });
        var pseudo = core.Handle(new Pseudo(new WindowId(1)));

        Assert.False(Assert.Single(tiled.State.Clients).Floating);
        Assert.Equal(Area, BoundsOf(tiled, 1));
        Assert.Equal(new Rectangle(760, 390, 400, 300), BoundsOf(pseudo, 1));
    }

    [Fact]
    public void AnExpressionThatCannotBeComputedIsSkipped()
    {
        var core = Ruled("size 100/0 100");

        var outcome = core.Handle(Box(1));

        Assert.Equal(new Rectangle(860, 465, 200, 150), BoundsOf(outcome, 1));
    }

    [Theory]
    [InlineData("(", "1", ")")]
    [InlineData("-", "1", "")]
    [InlineData("2^", "2", "")]
    [InlineData("abs(", "1", ")")]
    public void AnExpressionNestedTooDeeplyWarnsInsteadOfOverflowingTheStack(string open, string middle, string close)
    {
        var nested = string.Concat(Enumerable.Repeat(open, 100_000)) + middle + string.Concat(Enumerable.Repeat(close, 100_000));

        var document = ConfigParser.Parse($"windowrulev2 = size {nested} 10, class:^(box)$\n");

        Assert.Equal([new ConfigWarning(1, "Malformed window rule.")], document.Warnings);
        Assert.Empty(document.WindowRules);
    }

    [Fact]
    public void AnExpressionNestedWithinTheLimitStillParses()
    {
        var nested = new string('(', 100) + "monitor_w" + new string(')', 100);

        var document = ConfigParser.Parse($"windowrulev2 = size {nested} 10, class:^(box)$\n");

        Assert.Empty(document.Warnings);
        Assert.Single(document.WindowRules);
    }

    [Fact]
    public void TheSignOfNotANumberIsZero()
    {
        var document = ConfigParser.Parse("windowrulev2 = move sign(sqrt(-1)) 0, class:.*\n");

        Assert.Empty(document.Warnings);
        Assert.Single(document.WindowRules);
        Assert.True(RuleExpression.TryEvaluate("sign(sqrt(-1))", new Dictionary<string, double>(), out var value));
        Assert.Equal(0, value);
    }

    [Theory]
    [InlineData("move 10")]
    [InlineData("move 10,10")]
    [InlineData("size 50% 50%")]
    [InlineData("size 800x600 1")]
    [InlineData("move unknown_w 0")]
    [InlineData("move (10 10")]
    [InlineData("no_close_for soon")]
    [InlineData("no_close_for -1")]
    [InlineData("center maybe")]
    [InlineData("monitor")]
    public void AMalformedPlacementRuleWarnsAndIsSkipped(string effect)
    {
        var document = ConfigParser.Parse($"windowrulev2 = {effect}, class:^(box)$\n");

        Assert.Equal([new ConfigWarning(1, "Malformed window rule.")], document.Warnings);
        Assert.Empty(document.WindowRules);
    }

    [Fact]
    public void MonitorOpensTheWindowOnThatMonitorAndFocusesIt()
    {
        var core = Monitors("windowrulev2 = monitor desc:Samsung, class:^(box)$\n");

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Left) { Traits = WindowTraits.Normal with { Class = "box" } });

        Assert.Equal(new MonitorId(1), outcome.State.ActiveMonitor);
        Assert.Equal(Right, BoundsOf(outcome, 1));
    }

    [Fact]
    public void SilentMonitorOpensTheWindowThereWithoutFocusingIt()
    {
        var core = Monitors("windowrulev2 = monitor desc:Samsung silent, class:^(box)$\n");

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Left) { Traits = WindowTraits.Normal with { Class = "box" } });

        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
        Assert.Equal(Right, BoundsOf(outcome, 1));
    }

    [Fact]
    public void AMonitorThatDoesNotExistKeepsTheMonitorTheWindowOpenedOn()
    {
        var core = Monitors("windowrulev2 = monitor HDMI-A-9, class:^(box)$\n");

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Left) { Traits = WindowTraits.Normal with { Class = "box" } });

        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
        Assert.Equal(Left, BoundsOf(outcome, 1));
    }

    [Fact]
    public void NoCloseForRefusesToCloseUntilTheTimeHasPassed()
    {
        long now = 1_000;
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = no_close_for 500, class:^(box)$\n")) { Clock = () => now };
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Class = "box" } });

        var early = core.Handle(new Dispatch("killactive"));
        var byAddress = core.Execute("dispatch closewindow address:0x1");
        now = 1_500;
        var late = core.Handle(new Dispatch("killactive"));

        Assert.Null(early.Close);
        Assert.Equal("error: can't close window, it's not closeable yet (noclosefor)", early.Error);
        Assert.Null(byAddress.Effect.Close);
        Assert.Equal(new WindowId(1), late.Close);
    }

    [Fact]
    public void TheFirstResizeAfterMapIsUndoneOnce()
    {
        long now = 0;
        var core = Ruled("size 800 600");
        core.Clock = () => now;
        core.Handle(Box(1));
        var placed = new Rectangle(560, 240, 800, 600);

        Assert.True(core.AwaitsFirstResize(new WindowId(1)));
        var moved = core.Handle(new WindowResized(new WindowId(1), placed with { X = 0 }));
        now = 100;
        var undone = core.Handle(new WindowResized(new WindowId(1), new Rectangle(0, 0, 1200, 900)));
        var kept = core.Handle(new WindowResized(new WindowId(1), new Rectangle(0, 0, 1000, 700)));

        Assert.Empty(moved.Placements);
        Assert.Equal(placed, BoundsOf(undone, 1));
        Assert.Empty(kept.Placements);
        Assert.False(core.AwaitsFirstResize(new WindowId(1)));
    }

    [Fact]
    public void AResizeLongAfterMapIsLeftAlone()
    {
        long now = 0;
        var core = Ruled("center");
        core.Clock = () => now;
        core.Handle(Box(1));
        now = WindowManager.ReapplyMilliseconds + 1;

        var resized = core.Handle(new WindowResized(new WindowId(1), new Rectangle(0, 0, 1200, 900)));

        Assert.Empty(resized.Placements);
        Assert.False(core.AwaitsFirstResize(new WindowId(1)));
    }

    [Fact]
    public void ExecRuleBlocksHonorSizeAndMove()
    {
        var core = new WindowManager();
        var launch = core.Handle(new Dispatch("exec", "[float; size 300 200; move 40 50] kitty")).Launch;
        Assert.NotNull(launch);
        core.NoteLaunched(40, launch);

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area) { ProcessId = 40, Bounds = Own });

        Assert.Equal(new Rectangle(40, 50, 300, 200), BoundsOf(outcome, 1));
    }

    private static WindowManager Ruled(params string[] effects)
    {
        var text = "windowrulev2 = float, class:^(box)$\n";
        foreach (var effect in effects)
            text += $"windowrulev2 = {effect}, class:^(box)$\n";

        return new WindowManager(ConfigParser.Parse(text));
    }

    private static WindowManager Monitors(string config)
    {
        var core = new WindowManager(ConfigParser.Parse("general:gaps_out = 0\ngeneral:border_size = 0\n" + config));
        core.Handle(new MonitorsChanged([Display(@"\\.\DISPLAY1", "Dell", Left), Display(@"\\.\DISPLAY2", "Samsung", Right)]));
        return core;
    }

    private static WindowAppeared Box(long window, Rectangle? area = null) =>
        new(new WindowId(window), area ?? Area)
        {
            Traits = WindowTraits.Normal with { Class = "box" },
            Bounds = Own,
        };

    private static WindowAppeared Dialog(long window, Rectangle area) =>
        new(new WindowId(window), area)
        {
            Traits = WindowTraits.Normal with { Dialog = true },
            Bounds = Own,
        };
}
