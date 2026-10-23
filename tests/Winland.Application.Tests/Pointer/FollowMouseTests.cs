namespace Winland.Application.Tests;

public class FollowMouseTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void FollowMouseDefaultsToTheManual()
    {
        var input = InputOptions.From(ConfigParser.Parse(""));
        var session = SessionOptions.From(ConfigParser.Parse(""));

        Assert.Equal(1, input.FollowMouse);
        Assert.Equal(0, input.FollowMouseThreshold);
        Assert.Equal(0, input.FollowMouseShrink);
        Assert.True(input.MouseRefocus);
        Assert.Equal(1, input.FloatSwitchOverrideFocus);
        Assert.Equal(0, input.FocusOnClose);
        Assert.True(session.MouseMoveFocusesMonitor);
    }

    [Fact]
    public void FollowMouseNamesClamp()
    {
        var input = InputOptions.From(ConfigParser.Parse("""
            input:follow_mouse = separate
            input:follow_mouse_threshold = 12.5
            input:follow_mouse_shrink = 400
            input:mouse_refocus = false
            input:float_switch_override_focus = 2
            input:focus_on_close = cursor
            misc:mouse_move_focuses_monitor = false
            """));
        var session = SessionOptions.From(ConfigParser.Parse("misc:mouse_move_focuses_monitor = off\n"));

        Assert.Equal(3, input.FollowMouse);
        Assert.Equal(12.5, input.FollowMouseThreshold);
        Assert.Equal(300, input.FollowMouseShrink);
        Assert.False(input.MouseRefocus);
        Assert.Equal(2, input.FloatSwitchOverrideFocus);
        Assert.Equal(1, input.FocusOnClose);
        Assert.False(session.MouseMoveFocusesMonitor);
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
    public void MovingOntoAWindowWithNoFollowMouseLeavesTheFocus()
    {
        var (core, laid) = Tiled();
        core.Execute("setprop address:0x1 no_follow_mouse 1");

        Move(core, Center(laid, 2), 1_000);
        var followed = Move(core, Center(laid, 1), 1_100);

        Assert.Null(followed.Focus);
        Assert.DoesNotContain(followed.Events, item => item is FocusChanged);

        core.Execute("setprop address:0x1 no_follow_mouse 0");
        Move(core, Center(laid, 2), 1_200);
        Assert.Equal(new WindowId(1), Move(core, Center(laid, 1), 1_300).Focus);
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
    public void ThresholdHoldsFocusUntilThePointerHasTraveledFarEnough()
    {
        var core = Floats("input:follow_mouse_threshold = 50\n");
        core.Handle(Box(1, 0, 0));
        core.Handle(Box(2, 100, 0));

        Move(core, new Point(101, 50), 0);
        var blocked = Move(core, new Point(70, 50), 100);
        var followed = Move(core, new Point(40, 50), 200);

        Assert.Null(blocked.Focus);
        Assert.Equal(new WindowId(1), followed.Focus);
    }

    [Fact]
    public void APauseResetsTheThreshold()
    {
        var core = Floats("input:follow_mouse_threshold = 50\n");
        core.Handle(Box(1, 0, 0));
        core.Handle(Box(2, 100, 0));

        Move(core, new Point(101, 50), 0);
        Move(core, new Point(70, 50), 100);
        var paused = Move(core, new Point(40, 50), 700);

        Assert.Null(paused.Focus);
        Assert.Equal("\"address\":\"0x2\"", Address(core));
    }

    [Fact]
    public void ShrinkLeavesADeadZoneAroundAnUnfocusedClient()
    {
        var core = Floats("input:follow_mouse_shrink = 20\n");
        core.Handle(Box(1, 100, 100, 400, 400));
        core.Handle(Box(2, 520, 100, 400, 400));
        core.Handle(new Dispatch("focuswindow", "address:0x1"));

        Move(core, new Point(300, 300), 1_000);
        var dead = Move(core, new Point(530, 300), 1_100);
        var entered = Move(core, new Point(720, 300), 1_200);

        Assert.Null(dead.Focus);
        Assert.Equal(new WindowId(2), entered.Focus);

        core.Handle(new Dispatch("focuswindow", "address:0x1"));
        Move(core, new Point(300, 300), 2_000);
        var ownEdge = Move(core, new Point(490, 300), 2_100);

        Assert.Null(ownEdge.Focus);
        Assert.Equal("\"address\":\"0x1\"", Address(core));

        core.Handle(new Dispatch("focuswindow", "address:0x2"));
        Move(core, new Point(720, 300), 3_000);
        var otherEdge = Move(core, new Point(490, 300), 3_100);

        Assert.Null(otherEdge.Focus);
        Assert.Equal("\"address\":\"0x2\"", Address(core));
    }

    [Fact]
    public void RefocusWaitsForABoundaryWhenItIsOff()
    {
        var (core, laid) = Tiled("input:mouse_refocus = false\n");
        var right = Center(laid, 2);
        var left = Center(laid, 1);

        Move(core, right, 1_000);
        core.Handle(new Dispatch("movefocus", "l"));
        var stayed = Move(core, new Point(right.X - 30, right.Y), 1_100);
        Move(core, left, 1_200);
        var crossed = Move(core, right, 1_300);

        Assert.Null(stayed.Focus);
        Assert.Equal(new WindowId(2), crossed.Focus);
    }

    [Fact]
    public void AWindowThatReusesAClosedHandleIsNotTakenForTheWindowLastHovered()
    {
        var (core, laid) = Tiled("input:mouse_refocus = false\n");
        var right = Center(laid, 2);
        Move(core, right, 1_000);
        core.Handle(new WindowClosed(new WindowId(2)));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        core.Handle(new Dispatch("movefocus", "l"));

        var hovered = Move(core, new Point(right.X - 30, right.Y), 1_100);

        Assert.Equal(new WindowId(2), hovered.Focus);
    }

    [Fact]
    public void FloatSwitchFocusesAcrossTiledAndFloating()
    {
        var off = Floats("input:follow_mouse = 0\ninput:float_switch_override_focus = 0\n");
        off.Handle(new WindowAppeared(new WindowId(1), Area));
        off.Handle(Box(2, 100, 100, 200, 200));
        var kept = Move(off, new Point(700, 700), 1_000);

        var on = Floats("input:follow_mouse = 0\ninput:float_switch_override_focus = 1\n");
        on.Handle(new WindowAppeared(new WindowId(1), Area));
        on.Handle(Box(2, 100, 100, 200, 200));
        var switched = Move(on, new Point(700, 700), 1_000);

        Assert.Null(kept.Focus);
        Assert.Equal(new WindowId(1), switched.Focus);
    }

    [Fact]
    public void FloatSwitchTwoFollowsBetweenFloats()
    {
        var core = Floats("input:follow_mouse = 0\ninput:float_switch_override_focus = 2\n");
        core.Handle(Box(1, 0, 0));
        core.Handle(Box(2, 200, 0));

        var followed = Move(core, new Point(40, 50), 1_000);

        Assert.Equal(new WindowId(1), followed.Focus);
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
    public void AMonitorStaysPutWhenMouseFocusIsOff()
    {
        var core = new WindowManager(ConfigParser.Parse("""
            input:follow_mouse = 0
            misc:mouse_move_focuses_monitor = false
            """));
        var left = Display(@"\\.\DISPLAY1", 0, 800, 600);
        var right = Display(@"\\.\DISPLAY2", 800, 800, 600);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var moved = Move(core, new Point(1_000, 100), 1_000);

        Assert.Equal(new MonitorId(0), moved.State.ActiveMonitor);
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
    public void FocusOnCloseUsesTheNearestClientThePointerOrTheRecentOne()
    {
        var nearest = TiledThree("input:focus_on_close = 0\n");
        var next = nearest.Handle(new WindowClosed(new WindowId(3)));

        var cursor = TiledThree("input:follow_mouse = 0\ninput:focus_on_close = 1\n");
        Move(cursor, new Point(480, 540), 1_000);
        var under = cursor.Handle(new WindowClosed(new WindowId(3)));

        var recent = TiledThree("input:focus_on_close = mru\n");
        var first = recent.Handle(new WindowClosed(new WindowId(3)));
        var older = recent.Handle(new WindowClosed(new WindowId(2)));

        Assert.Equal(new WindowId(2), next.Focus);
        Assert.Equal(new WindowId(1), under.Focus);
        Assert.Equal(new WindowId(2), first.Focus);
        Assert.Equal(new WindowId(1), older.Focus);
    }

    [Fact]
    public void FocusOnCloseZeroPrefersATileOverAFloatOnTop()
    {
        // The float opens first, so it comes before both tiles and would win a tie on distance.
        var core = Floats("input:focus_on_close = 0\n");
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
        new(ConfigParser.Parse(Flat(extra) + "windowrulev2 = float, class:^(box)$\nwindowrulev2 = move window_x window_y, class:^(box)$\n"));

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
