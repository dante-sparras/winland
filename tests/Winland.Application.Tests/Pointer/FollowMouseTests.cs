namespace Winland.Application.Tests;

public class FollowMouseTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void FollowMouseDefaultsToTheManual()
    {
        var input = InputOptions.From(ConfigParser.Parse(""));

        Assert.Equal(1, input.FollowMouse);
    }

    [Fact]
    public void FollowMouseNamesClamp()
    {
        var input = InputOptions.From(ConfigParser.Parse("""
            input:follow_mouse = separate
            """));

        Assert.Equal(3, input.FollowMouse);
    }

    [Fact]
    public void MovingOntoAnotherWindowFocusesIt()
    {
        var (core, laid) = Tiled();

        var seeded = Move(core, Center(laid, 2), 1_000);
        var followed = Move(core, Center(laid, 1), 1_100);

        Assert.Null(seeded.Focus);
        Assert.Equal(new WindowId(1), followed.Focus);
        Assert.Contains(followed.Events, item => item is FocusChanged changed && changed.Window == new WindowId(1));
    }

    // A browser's extension panel is a window Winland does not manage. It takes the foreground from the browser, and
    // the pointer crosses the browser on its way to the panel.
    [Fact]
    public void CrossingTheClientAPanelOpenedOverLeavesThePanelTheFocus()
    {
        var (core, laid) = Tiled();
        Move(core, Center(laid, 1), 1_000);
        core.Handle(new WindowFocused(new WindowId(1), FocusCause.User));
        core.Handle(new WindowFocused(new WindowId(99), FocusCause.User));

        var crossed = Move(core, Center(laid, 1) with { X = Center(laid, 1).X + 40 }, 1_100);
        var left = Move(core, Center(laid, 2), 1_200);

        Assert.Null(crossed.Focus);
        Assert.Equal(new WindowId(2), left.Focus);
    }

    [Fact]
    public void DisabledFollowLeavesTheFocusedClient()
    {
        var (core, laid) = Tiled("input:follow_mouse = disabled\n");

        Move(core, Center(laid, 2), 1_000);
        var followed = Move(core, Center(laid, 1), 1_100);

        Assert.Null(followed.Focus);
        Assert.DoesNotContain(followed.Events, item => item is FocusChanged);
    }

    [Fact]
    public void APauseBeforeCrossingLeavesTheFocus()
    {
        var core = Floats("");
        core.Handle(Box(1, 0, 0));
        core.Handle(Box(2, 100, 0));

        Move(core, new Point(101, 50), 0);
        var paused = Move(core, new Point(40, 50), 700);

        Assert.Null(paused.Focus);
        Assert.Equal("\"address\":\"0x2\"", Address(core));
    }

    [Fact]
    public void FloatSwitchFocusesAcrossTiledAndFloating()
    {
        var core = Floats("input:follow_mouse = 0\n");
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(Box(2, 100, 100, 200, 200));
        var switched = Move(core, new Point(700, 700), 1_000);

        Assert.Equal(new WindowId(1), switched.Focus);
    }

    [Fact]
    public void SeparateModeIgnoresThePointerAndAClick()
    {
        var (core, laid) = Tiled("input:follow_mouse = 3\n");
        Move(core, Center(laid, 2), 1_000);
        var moved = Move(core, Center(laid, 1), 1_100);
        core.Handle(new PointerButtonPressed("mouse:272") { Position = Center(laid, 1) });
        var clicked = core.Handle(new WindowFocused(new WindowId(1), FocusCause.User));

        Assert.Null(moved.Focus);
        Assert.Equal(new WindowId(2), clicked.Focus);
    }

    [Fact]
    public void MovingOntoAnotherMonitorFocusesIt()
    {
        var core = new WindowManager(ConfigParser.Parse("input:follow_mouse = 0\n"));
        var left = Display(@"\\.\DISPLAY1", 0, 800, 600);
        var right = Display(@"\\.\DISPLAY2", 800, 800, 600);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var moved = Move(core, new Point(1_000, 100), 1_000);

        Assert.Equal(new MonitorId(1), moved.State.ActiveMonitor);
        Assert.Null(moved.Focus);
        Assert.DoesNotContain(moved.Events, item => item is FocusChanged);
    }

    [Fact]
    public void FollowMouseFocusesTheClientOnTheMonitorUnderThePointer()
    {
        var core = new WindowManager(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n"));
        var left = Display(@"\\.\DISPLAY1", 0, 800, 600);
        var right = Display(@"\\.\DISPLAY2", 800, 800, 600);
        core.Handle(new MonitorsChanged([left, right]));
        var first = core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        var second = core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        Move(core, Center(first, 1), 1_000);
        var followed = Move(core, Center(second, 2), 1_100);

        Assert.Equal(new WindowId(2), followed.Focus);
        Assert.Equal(new MonitorId(1), followed.State.ActiveMonitor);
    }

    [Fact]
    public void ClosingFocusesTheNearestClient()
    {
        var nearest = TiledThree("");
        var next = nearest.Handle(new WindowClosed(new WindowId(3)));

        Assert.Equal(new WindowId(2), next.Focus);
    }

    [Fact]
    public void ClosingPrefersATileOverAFloatOnTop()
    {
        // The float opens first, so it comes before both tiles and would win a tie on distance.
        var core = Floats("");
        core.Handle(Box(3, 1390, 490));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        core.Handle(new Dispatch("focuswindow", "address:0x2"));

        var closed = core.Handle(new WindowClosed(new WindowId(2)));
        var last = core.Handle(new WindowClosed(new WindowId(1)));

        Assert.Equal(new WindowId(1), closed.Focus);
        Assert.Equal(new WindowId(3), last.Focus);
    }

    [Fact]
    public void HoverReachesTheTilesOfAnOpenSpecialWorkspaceBeforeTheTilesUnderIt()
    {
        var (core, _) = Tiled();
        core.Handle(new Dispatch("togglespecialworkspace", "scratch"));
        core.Handle(new WindowAppeared(new WindowId(3), Area));
        var special = core.Handle(new WindowAppeared(new WindowId(4), Area));
        Assert.Equal(new WorkspaceId("special:scratch"), Assert.Single(special.State.Clients, client => client.Window == new WindowId(3)).Workspace);

        Move(core, Center(special, 4), 1_000);
        var hovered = Move(core, Center(special, 3), 1_100);

        Assert.Equal(new WindowId(3), hovered.Focus);
        Assert.Equal(new WorkspaceId("special:scratch"), hovered.State.SpecialWorkspace);
    }

    [Fact]
    public void HoverOnAMonitorWithAnOpenSpecialWorkspaceReachesItsTiles()
    {
        var core = new WindowManager(ConfigParser.Parse(Flat("")));
        var left = Display(@"\\.\DISPLAY1", 0, 1920, 1080);
        var right = Display(@"\\.\DISPLAY2", 1920, 1920, 1080);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), left.WorkArea));
        core.Handle(new Dispatch("togglespecialworkspace", "scratch"));
        core.Handle(new WindowAppeared(new WindowId(3), left.WorkArea));
        var special = core.Handle(new WindowAppeared(new WindowId(4), left.WorkArea));

        Move(core, Center(special, 4), 1_000);
        var hovered = Move(core, Center(special, 3), 1_100);

        Assert.Equal(new WindowId(3), hovered.Focus);
    }

    private static (WindowManager WindowManager, Outcome Laid) Tiled(string extra = "")
    {
        var core = new WindowManager(ConfigParser.Parse(Flat(extra)));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        return (core, core.Handle(new WindowAppeared(new WindowId(2), Area)));
    }

    private static WindowManager TiledThree(string extra)
    {
        var core = new WindowManager(ConfigParser.Parse(Flat(extra)));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        core.Handle(new WindowAppeared(new WindowId(3), Area));
        return core;
    }

    private static string Flat(string extra) =>
        "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + extra;

    private static WindowManager Floats(string extra) =>
        new(ConfigParser.Parse(Flat(extra) + "windowrule = float, class:^(box)$\nwindowrule = move window_x window_y, class:^(box)$\n"));

    private static WindowAppeared Box(long window, int x, int y, int width = 100, int height = 100) =>
        new(new WindowId(window), Area)
        {
            Bounds = new Rectangle(x, y, width, height),
            Traits = WindowTraits.Normal with { Class = "box" },
        };

    private static Outcome Move(WindowManager core, Point point, long time) =>
        core.Handle(new PointerMoved(point) { TimeMs = time });

    private static Point Center(Outcome outcome, long window)
    {
        var bounds = Assert.Single(outcome.State.Clients, client => client.Window == new WindowId(window)).Bounds;
        return new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    private static string Address(WindowManager core)
    {
        var text = core.Execute("j/activewindow").Text;
        var at = text.IndexOf("\"address\":", StringComparison.Ordinal);
        Assert.True(at >= 0, text);
        return text[at..(at + "\"address\":\"0x0\"".Length)];
    }
}
