namespace Winland.Application.Tests;

public class GestureTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    private const string Horizontal = "gesture = 3, horizontal, workspace\n";

    [Fact]
    public void WorkspaceGestureParsesWithoutWarnings()
    {
        var document = ConfigParser.Parse("gesture = 3, horizontal, workspace\ngesture = 4, vertical, workspace\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(
            [new TrackpadGesture(3, GestureAxis.Horizontal), new TrackpadGesture(4, GestureAxis.Vertical)],
            document.Gestures);
    }

    [Theory]
    [InlineData("gesture = 1, horizontal, workspace", "Invalid value 1 for finger count.")]
    [InlineData("gesture = 10, horizontal, workspace", "Invalid value 10 for finger count.")]
    [InlineData("gesture = 3, sideways, workspace", "Invalid direction: sideways.")]
    [InlineData("gesture = 3, horizontal, teleport", "Invalid gesture: teleport.")]
    [InlineData("gesture = 3, pinch, workspace", "Invalid direction: pinch.")]
    [InlineData("gesture = 3, pinchin, workspace", "Invalid direction: pinchin.")]
    [InlineData("gesture = 3, pinchout, workspace", "Invalid direction: pinchout.")]
    [InlineData("gestureq = 3, horizontal, workspace", "Invalid gesture flag 'q'.")]
    [InlineData("gesture = 3, swipe, workspace", "Invalid direction: swipe.")]
    [InlineData("gesture = 3, left, workspace", "Invalid direction: left.")]
    [InlineData("gesture = 3, d, workspace", "Invalid direction: d.")]
    [InlineData("gesture = 3, horizontal, mod: SUPER, workspace", "A gesture is <fingers>, horizontal or vertical, workspace.")]
    [InlineData("gesture = 3, horizontal, scale: 2, workspace", "A gesture is <fingers>, horizontal or vertical, workspace.")]
    [InlineData("gesture = 3, horizontal, unset", "Invalid gesture: unset.")]
    [InlineData("gesturep = 3, horizontal, workspace", "Invalid gesture flag 'p'.")]
    public void MalformedGestureWarnsAndIsSkipped(string line, string warning)
    {
        var document = ConfigParser.Parse(line + "\n");

        Assert.Empty(document.Gestures);
        Assert.Equal(new ConfigWarning(1, warning), Assert.Single(document.Warnings));
    }

    [Theory]
    [InlineData("close")]
    [InlineData("special")]
    [InlineData("dispatcher")]
    [InlineData("float")]
    [InlineData("fullscreen")]
    [InlineData("move")]
    [InlineData("resize")]
    [InlineData("cursorZoom")]
    [InlineData("scrollMove")]
    [InlineData("")]
    public void OtherGestureActionsWarnAndAreSkipped(string action)
    {
        var document = ConfigParser.Parse($"gesture = 3, horizontal, {action}\n");

        Assert.Empty(document.Gestures);
        Assert.Equal(new ConfigWarning(1, $"Invalid gesture: {action}."), Assert.Single(document.Warnings));
    }

    [Theory]
    [InlineData("gestures:close_max_timeout = 1000")]
    [InlineData("gestures:workspace_swipe_touch = true")]
    [InlineData("gestures:workspace_swipe_touch_invert = true")]
    [InlineData("gestures:scrolling:move_snap_to_grid = true")]
    [InlineData("gestures:scrolling:move_snap_cursor = true")]
    public void OptionsForRemovedGesturesWarnAndAreSkipped(string line)
    {
        var document = ConfigParser.Parse(line + "\n");

        Assert.Equal(GestureOptions.Documented, GestureOptions.From(document));
        Assert.Contains("Unsupported keyword", Assert.Single(document.Warnings).Message);
    }

    [Fact]
    public void GestureOptionsAreHonored()
    {
        var document = ConfigParser.Parse("""
            gestures {
                workspace_swipe_distance = 5000
                workspace_swipe_invert = false
            }
            """);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new GestureOptions
            {
                Distance = 2000,
                Invert = false,
            },
            GestureOptions.From(document));
    }

    [Fact]
    public void FingersLeftPastTheCancelRatioSwitchesToTheNextWorkspace()
    {
        var core = Twin(Horizontal);

        var ended = Swipe(core, 3, Steps(20, -10, 0));

        Assert.Equal(new WorkspaceId("2"), ended.State.ActiveWorkspace);
        Assert.Equal("workspace", ended.Dispatcher);
    }

    [Fact]
    public void ALiveKeywordKeepsTheConfiguredGestures()
    {
        var core = Twin(Horizontal);

        Assert.Equal("ok", core.Execute("keyword general:border_size 4").Text);

        Assert.Equal(new WorkspaceId("2"), Swipe(core, 3, Steps(20, -10, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void ShortSlowSwipeReverts()
    {
        var core = Twin(Horizontal);

        var ended = Swipe(core, 3, Steps(14, -10, 0));

        Assert.Equal(WorkspaceId.Initial, ended.State.ActiveWorkspace);
    }

    [Fact]
    public void FastSwipeForcesTheChange()
    {
        var core = Twin(Horizontal);

        Assert.Equal(new WorkspaceId("2"), Swipe(core, 3, Steps(2, -60, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void DistanceSetsTheThreshold()
    {
        var core = Twin(Horizontal + "gestures:workspace_swipe_distance = 100\n");

        var ended = Swipe(core, 3, Steps(6, -10, 0));

        Assert.Equal(new WorkspaceId("2"), ended.State.ActiveWorkspace);
    }

    [Fact]
    public void InvertOffMakesFingersRightGoToTheNextWorkspace()
    {
        var core = Twin(Horizontal + "gestures:workspace_swipe_invert = false\n");

        Assert.Equal(WorkspaceId.Initial, Swipe(core, 3, Steps(20, -10, 0)).State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("2"), Swipe(core, 3, Steps(20, 10, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void FingersRightGoToThePreviousWorkspace()
    {
        var core = Twin(Horizontal);
        core.Handle(new Dispatch("workspace", "2"));

        var ended = Swipe(core, 3, Steps(20, 10, 0));

        Assert.Equal(WorkspaceId.Initial, ended.State.ActiveWorkspace);
    }

    [Fact]
    public void ThereIsNoPreviousWorkspaceBeforeTheFirst()
    {
        var core = Twin(Horizontal);

        var ended = Swipe(core, 3, Steps(20, 10, 0));

        Assert.Equal(WorkspaceId.Initial, ended.State.ActiveWorkspace);
    }

    [Fact]
    public void VerticalGestureReadsVerticalMovement()
    {
        var core = Twin("gesture = 3, vertical, workspace\n");

        Assert.Equal(WorkspaceId.Initial, Swipe(core, 3, Steps(20, -10, 0)).State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("2"), Swipe(core, 3, Steps(20, 0, -10)).State.ActiveWorkspace);
    }

    [Fact]
    public void FingerCountMustMatch()
    {
        var core = Twin("gesture = 4, horizontal, workspace\n");

        Assert.Equal(WorkspaceId.Initial, Swipe(core, 3, Steps(20, -10, 0)).State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("2"), Swipe(core, 4, Steps(20, -10, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void SwipingPastTheLastWorkspaceCreatesOne()
    {
        var core = Twin(Horizontal);
        core.Handle(new Dispatch("workspace", "2"));

        Assert.Equal(new WorkspaceId("3"), Swipe(core, 3, Steps(20, -10, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void EmptyWorkspaceDoesNotSwipeOnToANewOne()
    {
        var core = Load(Horizontal);

        var ended = Swipe(core, 3, Steps(20, -10, 0));

        Assert.Equal(WorkspaceId.Initial, ended.State.ActiveWorkspace);
    }

    [Fact]
    public void DirectionLockHoldsTheFirstDirection()
    {
        var locked = Twin(Horizontal);
        locked.Handle(new Dispatch("workspace", "2"));
        (double, double)[] steps = [(-20, 0), (200, 0)];

        Assert.Equal(new WorkspaceId("2"), Swipe(locked, 3, steps).State.ActiveWorkspace);
    }

    [Fact]
    public void ASwipeSkipsEmptyWorkspaces()
    {
        var core = Load(Horizontal);
        Open(core, 1, "1");
        Open(core, 3, "3");
        core.Handle(new Dispatch("workspace", "1"));

        Assert.Equal(new WorkspaceId("3"), Swipe(core, 3, Steps(20, -10, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void LockedSessionDoesNotSwipe()
    {
        var core = Twin(Horizontal);
        core.KeyState.Inhibit(true);

        var ended = Swipe(core, 3, Steps(20, -10, 0));

        Assert.Equal(WorkspaceId.Initial, ended.State.ActiveWorkspace);
    }

    [Fact]
    public void ReloadReplacesTheGestures()
    {
        var core = Twin(Horizontal);
        core.Handle(new Configure(ConfigParser.Parse("gesture = 4, horizontal, workspace\n")));

        Assert.Equal(WorkspaceId.Initial, Swipe(core, 3, Steps(20, -10, 0)).State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("2"), Swipe(core, 4, Steps(20, -10, 0)).State.ActiveWorkspace);
    }

    // Workspaces 1 and 2 each hold a Client, and 1 is shown.
    private static WindowManager Twin(string config)
    {
        var core = Load(config);
        Open(core, 1, "1");
        Open(core, 2, "2");
        core.Handle(new Dispatch("workspace", "1"));
        return core;
    }

    private static WindowManager Load(string config)
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(config)));
        return core;
    }

    private static void Open(WindowManager core, long window, string workspace)
    {
        core.Handle(new Dispatch("workspace", workspace));
        core.Handle(new WindowAppeared(new WindowId(window), Area));
    }

    private static (double X, double Y)[] Steps(int count, double x, double y) =>
        Enumerable.Repeat((x, y), count).ToArray();

    private static Outcome Swipe(WindowManager core, int fingers, IEnumerable<(double X, double Y)> steps)
    {
        core.Handle(new SwipeBegan(fingers));
        foreach (var (x, y) in steps)
            core.Handle(new SwipeUpdated(fingers, x, y));

        return core.Handle(new SwipeEnded());
    }
}
