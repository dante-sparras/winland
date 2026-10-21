using Winland;

namespace Winland.Tests;

public class GestureTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    private const string Horizontal = "gesture = 3, horizontal, workspace\n";

    [Fact]
    public void WorkspaceGestureParsesWithoutWarnings()
    {
        var document = ConfigParser.Parse("gesture = 3, horizontal, mod: SUPER, scale: 1.5, workspace\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new TrackpadGesture(3, GestureDirection.Horizontal, Modifier.Super, 1.5, "workspace"),
            Assert.Single(document.Gestures));
    }

    [Theory]
    [InlineData("gesture = 1, horizontal, workspace", "Invalid value 1 for finger count.")]
    [InlineData("gesture = 10, horizontal, workspace", "Invalid value 10 for finger count.")]
    [InlineData("gesture = 3, sideways, workspace", "Invalid direction: sideways.")]
    [InlineData("gesture = 3, horizontal, teleport", "Invalid gesture: teleport.")]
    [InlineData("gesture = 3, horizontal, scale: fast, workspace", "Invalid delta scale: fast.")]
    [InlineData("gestureq = 3, horizontal, workspace", "Invalid gesture flag 'q'.")]
    [InlineData("gesture = 3, horizontal, unset", "Can't remove a non-existent gesture.")]
    public void MalformedGestureWarnsAndIsSkipped(string line, string warning)
    {
        var document = ConfigParser.Parse(line + "\n");

        Assert.Empty(document.Gestures);
        Assert.Equal(new ConfigWarning(1, warning), Assert.Single(document.Warnings));
    }

    [Theory]
    [InlineData("gesturep = 3, horizontal, workspace", "Gesture flag 'p' is not honored.")]
    public void UnhonoredGestureWarnsAndStillShadows(string line, string warning)
    {
        var document = ConfigParser.Parse(line + "\n");

        Assert.Single(document.Gestures);
        Assert.Equal(new ConfigWarning(1, warning), Assert.Single(document.Warnings));
    }

    [Fact]
    public void ShadowedGestureWarnsAndUnsetRemovesTheMatch()
    {
        var document = ConfigParser.Parse("""
            gesture = 3, swipe, close
            gesture = 3, left, workspace
            gesture = 3, swipe, unset
            gesture = 3, left, workspace
            """);

        Assert.Equal(
            new TrackpadGesture(3, GestureDirection.Left, Modifier.None, 1, "workspace"),
            Assert.Single(document.Gestures));
        Assert.Equal(
            [new ConfigWarning(2, "Gesture will be overshadowed by a previous gesture. Previous SWIPE shadows new LEFT.")],
            document.Warnings);
    }

    [Fact]
    public void GestureOptionsAreHonored()
    {
        var document = ConfigParser.Parse("""
            gestures {
                workspace_swipe_distance = 5000
                workspace_swipe_invert = false
                workspace_swipe_min_speed_to_force = 0
                workspace_swipe_cancel_ratio = 0.7
                workspace_swipe_create_new = false
                workspace_swipe_direction_lock = false
                workspace_swipe_direction_lock_threshold = 20
                workspace_swipe_forever = true
                workspace_swipe_use_r = true
            }
            """);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new GestureOptions
            {
                Distance = 2000,
                Invert = false,
                MinSpeedToForce = 0,
                CancelRatio = 0.7,
                CreateNew = false,
                DirectionLock = false,
                DirectionLockThreshold = 20,
                Forever = true,
                UseRelative = true,
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
    public void FastSwipeForcesTheChangeUnlessForcingIsOff()
    {
        var forced = Twin(Horizontal);
        var off = Twin(Horizontal + "gestures:workspace_swipe_min_speed_to_force = 0\n");

        Assert.Equal(new WorkspaceId("2"), Swipe(forced, 3, Steps(2, -60, 0)).State.ActiveWorkspace);
        Assert.Equal(WorkspaceId.Initial, Swipe(off, 3, Steps(2, -60, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void DistanceAndCancelRatioSetTheThreshold()
    {
        var core = Twin(Horizontal + "gestures:workspace_swipe_distance = 100\ngestures:workspace_swipe_cancel_ratio = 0.2\n");

        var ended = Swipe(core, 3, Steps(3, -10, 0));

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
    public void OneWayGestureMatchesOnlyItsDirection()
    {
        var core = Twin("gesture = 3, left, workspace\n");
        core.Handle(new Dispatch("workspace", "2"));

        Assert.Equal(new WorkspaceId("2"), Swipe(core, 3, Steps(20, 10, 0)).State.ActiveWorkspace);
        // Left reads the distance as leftward travel, so with the default invert it heads for the previous Workspace.
        Assert.Equal(WorkspaceId.Initial, Swipe(core, 3, Steps(20, -10, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void FingerCountAndModifiersMustMatch()
    {
        var core = Twin("gesture = 4, horizontal, mod: SUPER, workspace\n");

        Assert.Equal(WorkspaceId.Initial, Swipe(core, 4, Steps(20, -10, 0)).State.ActiveWorkspace);
        core.Handle(new KeyPressed("Super_L"));
        Assert.Equal(WorkspaceId.Initial, Swipe(core, 3, Steps(20, -10, 0)).State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("2"), Swipe(core, 4, Steps(20, -10, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void SwipingPastTheLastWorkspaceCreatesOneUnlessCreateNewIsOff()
    {
        var core = Twin(Horizontal);
        core.Handle(new Dispatch("workspace", "2"));
        var off = Twin(Horizontal + "gestures:workspace_swipe_create_new = false\n");
        off.Handle(new Dispatch("workspace", "2"));

        Assert.Equal(new WorkspaceId("3"), Swipe(core, 3, Steps(20, -10, 0)).State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("2"), Swipe(off, 3, Steps(20, -10, 0)).State.ActiveWorkspace);
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
        var free = Twin(Horizontal + "gestures:workspace_swipe_direction_lock = false\n");
        free.Handle(new Dispatch("workspace", "2"));
        (double, double)[] steps = [(-20, 0), (200, 0)];

        Assert.Equal(new WorkspaceId("2"), Swipe(locked, 3, steps).State.ActiveWorkspace);
        Assert.Equal(WorkspaceId.Initial, Swipe(free, 3, steps).State.ActiveWorkspace);
    }

    [Fact]
    public void SwipeForeverKeepsSwitchingEachFullDistance()
    {
        var core = Load(Horizontal + "gestures:workspace_swipe_forever = true\n");
        Open(core, 1, "1");
        Open(core, 2, "2");
        Open(core, 3, "3");
        core.Handle(new Dispatch("workspace", "1"));

        var ended = Swipe(core, 3, Steps(35, -20, 0));

        Assert.Equal(new WorkspaceId("3"), ended.State.ActiveWorkspace);
    }

    [Fact]
    public void UseRCountsEmptyWorkspaces()
    {
        var onMonitor = Load(Horizontal);
        var relative = Load(Horizontal + "gestures:workspace_swipe_use_r = true\n");
        foreach (var core in new[] { onMonitor, relative })
        {
            Open(core, 1, "1");
            Open(core, 3, "3");
            core.Handle(new Dispatch("workspace", "1"));
        }

        Assert.Equal(new WorkspaceId("3"), Swipe(onMonitor, 3, Steps(20, -10, 0)).State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("2"), Swipe(relative, 3, Steps(20, -10, 0)).State.ActiveWorkspace);
    }

    [Fact]
    public void ScaleMultipliesTheDistance()
    {
        var core = Twin("gesture = 3, horizontal, scale: 2, workspace\n");

        var ended = Swipe(core, 3, Steps(8, -10, 0));

        Assert.Equal(new WorkspaceId("2"), ended.State.ActiveWorkspace);
    }

    [Fact]
    public void LockedSessionDoesNotSwipe()
    {
        var core = Twin(Horizontal);
        core.Handle(new InputInhibited(true));

        var ended = Swipe(core, 3, Steps(20, -10, 0));

        Assert.Equal(WorkspaceId.Initial, ended.State.ActiveWorkspace);
    }

    [Fact]
    public void SpecialGestureKeepsItsWorkspaceNameAndUnsetIgnoresIt()
    {
        var document = ConfigParser.Parse("""
            gesture = 3, up, special, scratch
            gesture = 4, up, special
            gesture = 4, up, unset
            """);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new TrackpadGesture(3, GestureDirection.Up, Modifier.None, 1, "special", Argument: "scratch"),
            Assert.Single(document.Gestures));
    }

    [Theory]
    [InlineData(150, true)]
    [InlineData(45, true)]
    [InlineData(44, false)]
    public void SpecialSwipeShowsItsWorkspaceOnceItCoversThirtyPercent(double distance, bool shown)
    {
        var core = Twin("gesture = 3, up, special, scratch\n");
        var scratch = new WorkspaceId("special:scratch");

        var opened = Swipe(core, 3, [(0, -distance)]);
        Assert.Equal(shown ? scratch : null, opened.State.SpecialWorkspace);

        // A second swipe the same length closes what the first one opened.
        if (shown)
            Assert.Null(Swipe(core, 3, [(0, -distance)]).State.SpecialWorkspace);
    }

    [Fact]
    public void LockedSessionDoesNotSwipeTheSpecialWorkspace()
    {
        var core = Twin("gesture = 3, up, special\n");
        core.Handle(new InputInhibited(true));

        Assert.Null(Swipe(core, 3, [(0, -150)]).State.SpecialWorkspace);
    }

    [Theory]
    [InlineData(-30, false)]
    [InlineData(-60, true)]
    public void TheCloseGestureClosesTheFocusedWindowOnceItWentFarEnough(int along, bool closes)
    {
        var core = Twin("gesture = 3, left, close\n");

        var ended = Swipe(core, 3, [(along, 0)]);

        Assert.Equal(closes, ended.Close is not null);
    }

    [Fact]
    public void ADispatcherGestureRunsItsDispatcherWhenTheFingersLift()
    {
        var core = Twin("gesture = 3, left, dispatcher, workspace, 2\n");

        var moved = Swipe(core, 3, [(-10, 0)]);

        Assert.Equal(new WorkspaceId("2"), moved.State.ActiveWorkspace);
    }

    [Theory]
    [InlineData("float", true)]
    [InlineData("tile", false)]
    [InlineData("", true)]
    public void TheFloatGestureFloatsOrTilesTheFocusedWindow(string mode, bool floating)
    {
        var core = Twin($"gesture = 3, right, float, {mode}\n");

        var ended = Swipe(core, 3, [(80, 0)]);

        Assert.Equal(floating, Assert.Single(ended.State.Clients, client => client.Window == new WindowId(1)).Floating);
    }

    [Fact]
    public void ANegativeScaleIsKeptAndATinyOneWarns()
    {
        var document = ConfigParser.Parse("gesture = 3, left, scale:-2, workspace\ngesture = 4, left, scale:0.05, workspace\n");

        Assert.Equal(-2, Assert.Single(document.Gestures).Scale);
        Assert.Single(document.Warnings);
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
    private static Core Twin(string config)
    {
        var core = Load(config);
        Open(core, 1, "1");
        Open(core, 2, "2");
        core.Handle(new Dispatch("workspace", "1"));
        return core;
    }

    private static Core Load(string config)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(config)));
        return core;
    }

    private static void Open(Core core, long window, string workspace)
    {
        core.Handle(new Dispatch("workspace", workspace));
        core.Handle(new WindowAppeared(new WindowId(window), Area));
    }

    private static (double X, double Y)[] Steps(int count, double x, double y) =>
        Enumerable.Repeat((x, y), count).ToArray();

    private static Outcome Swipe(Core core, int fingers, IEnumerable<(double X, double Y)> steps)
    {
        core.Handle(new SwipeBegan(fingers));
        foreach (var (x, y) in steps)
            core.Handle(new SwipeUpdated(fingers, x, y));

        return core.Handle(new SwipeEnded());
    }
}
