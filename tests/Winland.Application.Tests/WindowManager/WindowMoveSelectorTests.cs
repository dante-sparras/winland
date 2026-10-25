namespace Winland.Application.Tests;

public class WindowMoveSelectorTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    private static readonly Rectangle Left = new(0, 0, 1920, 1080);

    private static readonly Rectangle Right = new(1920, 0, 1920, 1080);

    [Fact]
    public void ADirectionOnAnotherWindowLeavesTheFocus()
    {
        var core = Opened(new WindowManager(), 2);
        var opened = core.Handle(new Dispatch("focuswindow", "address:0x1"));
        Assert.Equal(new WindowId(1), opened.Focus);

        var moved = core.Handle(new Dispatch("movewindow", "l,address:0x2"));

        Assert.Null(moved.Error);
        Assert.Null(moved.Focus);
        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("killactive")).Close);
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(moved, 2));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(moved, 1));
    }

    [Fact]
    public void TheFocusedWindowKeepsTheFocusWhenItMovesInMaster()
    {
        var core = Opened(Master(), 2);
        core.Handle(new Dispatch("focuswindow", "address:0x1"));

        var moved = core.Handle(new Dispatch("movewindow", "r,address:0x1"));

        Assert.Null(moved.Focus);
        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("killactive")).Close);
    }

    [Fact]
    public void ADirectionOntoAnotherMonitorDoesNotFollow()
    {
        var core = TwoMonitors();

        var moved = core.Handle(new Dispatch("movewindow", "l,address:0x2"));

        Assert.Null(moved.Error);
        Assert.Null(moved.Focus);
        Assert.Equal(new MonitorId(0), moved.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("1"), moved.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("1"), Client(moved, 2).Workspace);
        Assert.True(BoundsOf(moved, 2).X < Right.X);
        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("killactive")).Close);
    }

    [Fact]
    public void TheFocusedWindowStillFollowsAcrossAMonitor()
    {
        var core = TwoMonitors();

        var moved = core.Handle(new Dispatch("movewindow", "r,address:0x1"));

        Assert.Equal(new WindowId(1), moved.Focus);
        Assert.Equal(new MonitorId(1), moved.State.ActiveMonitor);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), Client(moved, 1).Workspace);
    }

    [Fact]
    public void ASwapOnTheOtherMonitorLeavesTheFocus()
    {
        var core = TwoMonitors();
        var opened = core.Handle(new WindowAppeared(new WindowId(3), Right));
        var near = BoundsOf(opened, 2);
        var far = BoundsOf(opened, 3);

        var moved = core.Handle(new Dispatch("movewindow", "l,address:0x3"));

        Assert.Null(moved.Focus);
        Assert.Equal(new MonitorId(0), moved.State.ActiveMonitor);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), Client(moved, 3).Workspace);
        Assert.Equal(near, BoundsOf(moved, 3));
        Assert.Equal(far, BoundsOf(moved, 2));
        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("killactive")).Close);
    }

    [Fact]
    public void MoveOntoANamedMonitorFollows()
    {
        var core = TwoMonitors();

        var moved = core.Handle(new Dispatch("movewindow", @"mon:\\.\DISPLAY2,address:0x1"));

        Assert.Equal(new WindowId(1), moved.Focus);
        Assert.Equal(new MonitorId(1), moved.State.ActiveMonitor);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), Client(moved, 1).Workspace);
    }

    [Fact]
    public void ASilentMoveOntoAMonitorLeavesTheFocus()
    {
        var core = TwoMonitors();

        var moved = core.Handle(new Dispatch("movewindow", @"mon:\\.\DISPLAY1 silent,address:0x2"));

        Assert.Null(moved.Focus);
        Assert.Equal(new MonitorId(0), moved.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("1"), Client(moved, 2).Workspace);
        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("killactive")).Close);
    }

    [Fact]
    public void ARelativeMonitorIsReadFromTheFocusedMonitor()
    {
        var core = TwoMonitors();

        var moved = core.Handle(new Dispatch("movewindow", "mon:+1,address:0x2"));

        Assert.Null(moved.Error);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), Client(moved, 2).Workspace);
        Assert.Equal(new MonitorId(0), moved.State.ActiveMonitor);
        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("killactive")).Close);
    }

    [Fact]
    public void AFullscreenWindowRefusesTheMove()
    {
        var core = TwoMonitors();
        core.Handle(new Dispatch("fullscreen", "0,address:0x2"));

        var moved = core.Handle(new Dispatch("movewindow", "l,address:0x2"));

        Assert.Equal("error: can't move fullscreen window", moved.Error);
        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("killactive")).Close);
    }

    [Theory]
    [InlineData("movewindow", "l,address:0x9")]
    [InlineData("cyclenext", ",address:0x9")]
    public void AMissingWindowIsRejected(string name, string arguments)
    {
        var core = Opened(new WindowManager(), 1);

        var moved = core.Handle(new Dispatch(name, arguments));

        Assert.Equal("error: window not found", moved.Error);
    }

    [Fact]
    public void CycleNextOnANamedWindowWalksThatWindowsWorkspace()
    {
        var core = Opened(new WindowManager(), 3);

        var cycled = core.Handle(new Dispatch("cyclenext", ",address:0x1"));

        Assert.Equal(new WindowId(2), cycled.Focus);
        Assert.Equal(WorkspaceId.Initial, cycled.State.ActiveWorkspace);
    }

    [Fact]
    public void CycleNextOnAnotherMonitorShowsThatWorkspace()
    {
        var core = TwoMonitors();
        core.Handle(new WindowAppeared(new WindowId(3), Right));

        var cycled = core.Handle(new Dispatch("cyclenext", ",address:0x2"));

        Assert.Equal(new WindowId(3), cycled.Focus);
        Assert.Equal(new MonitorId(1), cycled.State.ActiveMonitor);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), cycled.State.ActiveWorkspace);
    }

    [Fact]
    public void CycleNextOnAHiddenWorkspaceShowsIt()
    {
        var core = Opened(new WindowManager(), 2);
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(new WindowAppeared(new WindowId(3), Area));
        core.Handle(new WindowAppeared(new WindowId(4), Area));
        core.Handle(new Dispatch("workspace", "1"));

        var cycled = core.Handle(new Dispatch("cyclenext", ",address:0x3"));

        Assert.Equal(new WindowId(4), cycled.Focus);
        Assert.Equal(new WorkspaceId("2"), cycled.State.ActiveWorkspace);
    }

    [Fact]
    public void TiledCycleOnAHiddenMasterWorkspaceUsesThatWorkspace()
    {
        var core = Opened(Master(), 2);
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(new WindowAppeared(new WindowId(3), Area));
        core.Handle(new WindowAppeared(new WindowId(4), Area));
        core.Handle(new Dispatch("workspace", "1"));

        var cycled = core.Handle(new Dispatch("cyclenext", "tiled,address:0x3"));

        Assert.Equal(new WindowId(4), cycled.Focus);
        Assert.Equal(new WorkspaceId("2"), cycled.State.ActiveWorkspace);
    }

    private static WindowManager Master() => new(ConfigParser.Parse("general:layout = master\n"));

    private static WindowManager Opened(WindowManager core, int count)
    {
        for (var window = 1; window <= count; window++)
            core.Handle(new WindowAppeared(new WindowId(window), Area));

        return core;
    }

    private static WindowManager TwoMonitors(string? config = null)
    {
        var core = config is null ? new WindowManager() : new WindowManager(ConfigParser.Parse(config));
        var left = Display(@"\\.\DISPLAY1", "Dell", Left);
        var right = Display(@"\\.\DISPLAY2", "Samsung", Right);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        return core;
    }
}
