using Winland;

namespace Winland.Tests;

public class TouchSwipeTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);
    private static readonly WorkspaceId Next = new("2");

    private const string On = "gestures:workspace_swipe_touch = true\n";

    [Fact]
    public void TheTouchSwipeOptionsAreRead()
    {
        var options = GestureOptions.From(ConfigParser.Parse(On + "gestures:workspace_swipe_touch_invert = true\n"));

        Assert.True(options.Touch);
        Assert.True(options.TouchInvert);
        Assert.False(GestureOptions.Documented.Touch);
        Assert.False(GestureOptions.Documented.TouchInvert);
    }

    [Fact]
    public void AFingerFromTheRightEdgeMovingLeftSwitchesToTheNextWorkspace()
    {
        var core = Twin(On);

        core.Handle(new TouchDown(1, 0, 0.999, 0.5));
        core.Handle(new TouchMoved(1, 0, 0.7, 0.5));
        core.Handle(new TouchMoved(1, 0, 0.4, 0.5));
        var ended = core.Handle(new TouchUp(1, 0));

        Assert.Equal(Next, ended.State.ActiveWorkspace);
        Assert.Equal("workspace", ended.Dispatcher);
    }

    [Fact]
    public void InvertTurnsTheSwipeAround()
    {
        var core = Twin(On + "gestures:workspace_swipe_touch_invert = true\n");

        core.Handle(new TouchDown(1, 0, 0.001, 0.5));
        core.Handle(new TouchMoved(1, 0, 0.6, 0.5));

        Assert.Equal(Next, core.Handle(new TouchUp(1, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void AShortSwipeOrAFingerAwayFromTheEdgeOrTheOptionOffChangesNothing()
    {
        var shortSwipe = Twin(On);
        shortSwipe.Handle(new TouchDown(1, 0, 0.999, 0.5));
        shortSwipe.Handle(new TouchMoved(1, 0, 0.9, 0.5));
        Assert.Equal(WorkspaceId.Initial, shortSwipe.Handle(new TouchUp(1, 0)).State.ActiveWorkspace);

        // The edge is the outer gap and the border, 21 pixels of 1920 by default.
        var inside = Twin(On);
        inside.Handle(new TouchDown(1, 0, 0.985, 0.5));
        inside.Handle(new TouchMoved(1, 0, 0.2, 0.5));
        Assert.Equal(WorkspaceId.Initial, inside.Handle(new TouchUp(1, 0)).State.ActiveWorkspace);

        var off = Twin("");
        off.Handle(new TouchDown(1, 0, 0.999, 0.5));
        off.Handle(new TouchMoved(1, 0, 0.2, 0.5));
        Assert.Equal(WorkspaceId.Initial, off.Handle(new TouchUp(1, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void OtherFingersDuringASwipeAreHeldAndCannotDriveOrEndIt()
    {
        var core = Twin(On);

        core.Handle(new TouchDown(1, 0, 0.999, 0.5));
        core.Handle(new TouchMoved(1, 0, 0.9, 0.5));
        core.Handle(new TouchDown(2, 0, 0.999, 0.5));
        core.Handle(new TouchMoved(2, 0, 0.1, 0.5));
        Assert.Equal(WorkspaceId.Initial, core.Handle(new TouchUp(2, 0)).State.ActiveWorkspace);
        core.Handle(new TouchDown(2, 0, 0.999, 0.5));
        Assert.Equal(WorkspaceId.Initial, core.Handle(new TouchUp(2, 0, Cancelled: true)).State.ActiveWorkspace);

        core.Handle(new TouchMoved(1, 0, 0.3, 0.5));
        Assert.Equal(Next, core.Handle(new TouchUp(1, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void AFingerDownWhenTheSwipeEndsStaysHeldUntilItLifts()
    {
        var core = Twin(On);

        core.Handle(new TouchDown(1, 0, 0.999, 0.5));
        core.Handle(new TouchDown(1, 1, 0.5, 0.5));
        core.Handle(new TouchUp(1, 0, Cancelled: true));

        // A new edge finger while one is still held starts nothing.
        core.Handle(new TouchDown(1, 2, 0.999, 0.5));
        core.Handle(new TouchMoved(1, 2, 0.2, 0.5));
        Assert.Equal(WorkspaceId.Initial, core.Handle(new TouchUp(1, 2)).State.ActiveWorkspace);
        core.Handle(new TouchUp(1, 1));

        core.Handle(new TouchDown(1, 3, 0.999, 0.5));
        core.Handle(new TouchMoved(1, 3, 0.2, 0.5));
        Assert.Equal(Next, core.Handle(new TouchUp(1, 3)).State.ActiveWorkspace);
    }

    [Fact]
    public void ACancelledFingerPutsTheWorkspaceBack()
    {
        var core = Twin(On);

        core.Handle(new TouchDown(1, 0, 0.999, 0.5));
        core.Handle(new TouchMoved(1, 0, 0.1, 0.5));

        Assert.Equal(WorkspaceId.Initial, core.Handle(new TouchUp(1, 0, Cancelled: true)).State.ActiveWorkspace);
        Assert.Equal(WorkspaceId.Initial, core.Handle(new TouchUp(1, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void TheScreenWindowsMapsTheTouchScreenToPicksTheMonitor()
    {
        var core = new Core();
        var right = new Rectangle(1920, 0, 1920, 1080);
        core.Handle(new MonitorsChanged([
            new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area),
            new Monitor("DISPLAY2", "", 1920, 0, 1920, 1080, 1, true, right),
        ]));
        core.Handle(new Configure(ConfigParser.Parse(On)));
        core.Handle(new Dispatch("focusmonitor", "DISPLAY2"));
        core.Handle(new Dispatch("workspace", "5"));
        core.Handle(new WindowAppeared(new WindowId(5), right));
        core.Handle(new Dispatch("workspace", "6"));
        core.Handle(new WindowAppeared(new WindowId(6), right));
        core.Handle(new Dispatch("workspace", "5"));
        core.Handle(new Dispatch("focusmonitor", "DISPLAY1"));

        core.Handle(new TouchDown(1, 0, 0.999, 0.5, right));
        core.Handle(new TouchMoved(1, 0, 0.2, 0.5, right));
        var ended = core.Handle(new TouchUp(1, 0));

        Assert.Equal(new WorkspaceId("6"), ended.State.ActiveWorkspace);
    }

    [Fact]
    public void ATransformTurnsALeftEdgeFingerIntoARightEdgeSwipe()
    {
        var core = Twin(On + "input:touchdevice:transform = 4\n");

        core.Handle(new TouchDown(1, 0, 0.001, 0.5));
        core.Handle(new TouchMoved(1, 0, 0.3, 0.5));
        core.Handle(new TouchMoved(1, 0, 0.6, 0.5));

        Assert.Equal(Next, core.Handle(new TouchUp(1, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void AnOutputMapsTheTouchOntoThatMonitor()
    {
        var core = new Core();
        var right = new Rectangle(1920, 0, 1920, 1080);
        core.Handle(new MonitorsChanged([
            new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area),
            new Monitor("DISPLAY2", "", 1920, 0, 1920, 1080, 1, true, right),
        ]));
        core.Handle(new Configure(ConfigParser.Parse(On + "input:touchdevice:output = DISPLAY2\n")));
        core.Handle(new Dispatch("focusmonitor", "DISPLAY2"));
        core.Handle(new Dispatch("workspace", "5"));
        core.Handle(new WindowAppeared(new WindowId(5), right));
        core.Handle(new Dispatch("workspace", "6"));
        core.Handle(new WindowAppeared(new WindowId(6), right));
        core.Handle(new Dispatch("workspace", "5"));
        core.Handle(new Dispatch("focusmonitor", "DISPLAY1"));

        core.Handle(new TouchDown(1, 0, 0.999, 0.5, Area));
        core.Handle(new TouchMoved(1, 0, 0.2, 0.5, Area));

        Assert.Equal(new WorkspaceId("6"), core.Handle(new TouchUp(1, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void ADisabledTouchScreenDoesNotSwipe()
    {
        var core = Twin(On + "input:touchdevice:enabled = false\n");

        core.Handle(new TouchDown(1, 0, 0.999, 0.5));
        core.Handle(new TouchMoved(1, 0, 0.2, 0.5));

        Assert.Equal(WorkspaceId.Initial, core.Handle(new TouchUp(1, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void DisablingTheTouchScreenPutsTheSwipeBack()
    {
        var core = Twin(On);
        core.Handle(new TouchDown(1, 0, 0.999, 0.5));
        core.Handle(new TouchMoved(1, 0, 0.4, 0.5));
        core.Handle(new Configure(ConfigParser.Parse(On + "input:touchdevice:enabled = false\n")));

        Assert.Equal(WorkspaceId.Initial, core.Handle(new TouchMoved(1, 0, 0.2, 0.5)).State.ActiveWorkspace);
    }

    [Fact]
    public void AScreensFramesBecomeDownsMovesAndUpsOnlyForWantedFingers()
    {
        var screen = new TouchScreen(7);

        Assert.Empty(screen.Frame(Down((1, 0.5, 0.5)), wanted: false, null));
        Assert.Equal([new TouchDown(7, 2, 0.1, 0.2)], screen.Frame(Down((1, 0.6, 0.5), (2, 0.1, 0.2)), wanted: true, null));
        Assert.Empty(screen.Frame(Down((2, 0.1, 0.2)), wanted: false, null));
        Assert.Equal([new TouchMoved(7, 2, 0.3, 0.2)], screen.Frame(Down((2, 0.3, 0.2)), wanted: false, null));
        Assert.Equal(1, screen.Count);
        Assert.Equal([new TouchUp(7, 2)], screen.Frame(Down(), wanted: true, null));

        _ = screen.Frame(Down((4, 0.5, 0.5)), wanted: true, Area);
        Assert.Equal([new TouchUp(7, 4, Cancelled: true)], screen.Cancel());
        Assert.Equal(0, screen.Count);
    }

    [Theory]
    [InlineData(0, 0.2, 0.1)]
    [InlineData(1, 0.1, 0.8)]
    [InlineData(2, 0.8, 0.9)]
    [InlineData(3, 0.9, 0.2)]
    [InlineData(4, 0.2, 0.1)]
    public void APointOnTheScreenTurnsWithItsDisplay(int quarterTurns, double x, double y)
    {
        Assert.Equal((x, y), TouchScreen.Orient(0.2, 0.1, quarterTurns));
    }

    private static Dictionary<int, (double X, double Y)> Down(params (int Id, double X, double Y)[] fingers) =>
        fingers.ToDictionary(f => f.Id, f => (f.X, f.Y));

    private static Core Twin(string config)
    {
        var core = new Core();
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));
        core.Handle(new Configure(ConfigParser.Parse(config)));
        core.Handle(new Dispatch("workspace", "1"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        core.Handle(new Dispatch("workspace", "1"));
        return core;
    }
}
