namespace Winland.Application.Tests;

public class WorkspaceTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void SwitchingShowsOnlyTheTargetWorkspacesClients()
    {
        var core = Load("""
            bind = SUPER, 1, workspace, 1
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        Appear(core, 2);
        HoldSuper(core);

        var outcome = Tap(core, "2");

        Assert.True(outcome.Consumed);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal(MonitorId.Primary, outcome.State.ActiveMonitor);
        Assert.Empty(outcome.Placements);
        Assert.Empty(outcome.Show);
        Assert.Equal([new WindowId(1), new WindowId(2)], outcome.Hide);
        Assert.Equal(2, outcome.State.Hidden.Count);
        Assert.All(outcome.State.Hidden, client => Assert.Equal(new WorkspaceId("1"), client.Workspace));
        Assert.Contains(outcome.State.Workspaces, workspace => workspace.Id == new WorkspaceId("2") && workspace.Monitor == MonitorId.Primary);
    }

    [Fact]
    public void SwitchingBackRestoresThatWorkspacesLayout()
    {
        var core = Load("""
            bind = SUPER, 1, workspace, 1
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        Appear(core, 2);
        HoldSuper(core);
        Tap(core, "2");
        var opened = Appear(core, 3);

        Assert.Equal(new WorkspaceId("2"), opened.State.ActiveWorkspace);
        Assert.Equal(Area, BoundsOf(opened, 3));
        Assert.Contains(opened.State.Hidden, client => client.Window == new WindowId(1));

        var back = Tap(core, "1");

        Assert.Equal(new WorkspaceId("1"), back.State.ActiveWorkspace);
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(back, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(back, 2));
        Assert.Equal([new WindowId(1), new WindowId(2)], back.Show);
        Assert.Equal([new WindowId(3)], back.Hide);
        Assert.Contains(back.State.Hidden, client => client.Window == new WindowId(3));
    }

    [Fact]
    public void MoveToWorkspaceSendsTheActiveClientAndFollows()
    {
        var core = Load("bind = SUPER SHIFT, 2, movetoworkspace, 2");
        Appear(core, 1);
        Appear(core, 2);
        HoldSuper(core);
        core.Handle(new KeyPressed("Shift_L"));

        var outcome = Tap(core, "2");

        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal(Area, BoundsOf(outcome, 2));
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 2).Workspace);
        Assert.Equal(new WorkspaceId("1"), Client(outcome, 1).Workspace);
        Assert.Equal([new WindowId(2)], outcome.Show);
        Assert.Equal([new WindowId(1)], outcome.Hide);
        Assert.Contains(outcome.State.Hidden, client => client.Window == new WindowId(1));
    }

    [Fact]
    public void MoveToWorkspaceSilentKeepsTheCurrentWorkspace()
    {
        var core = Load("bind = SUPER ALT, 2, movetoworkspacesilent, 2");
        Appear(core, 1);
        Appear(core, 2);
        HoldSuper(core);
        core.Handle(new KeyPressed("Alt_L"));

        var outcome = Tap(core, "2");

        Assert.Equal(WorkspaceId.Initial, outcome.State.ActiveWorkspace);
        Assert.Equal(Area, BoundsOf(outcome, 1));
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 2).Workspace);
        Assert.Equal([new WindowId(2)], outcome.Hide);
        Assert.Empty(outcome.Show);
        Assert.Equal([Client(outcome, 2)], outcome.State.Hidden);
    }

    [Fact]
    public void NewWindowJoinsTheActiveWorkspace()
    {
        var core = Load("""
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        HoldSuper(core);
        Tap(core, "2");

        var outcome = Appear(core, 4);

        Assert.Equal(new WorkspaceId("2"), Client(outcome, 4).Workspace);
        Assert.Equal(Area, BoundsOf(outcome, 4));
        Assert.Contains(outcome.State.Hidden, client => client.Window == new WindowId(1));
    }

    [Fact]
    public void RelativeAndPreviousAddressesFollowTheManual()
    {
        var core = Load("""
            bind = SUPER, O, workspace, +1
            bind = SUPER, I, workspace, -1
            bind = SUPER, P, workspace, previous
            bind = SUPER, 1, workspace, 1
            """);
        Appear(core, 1);
        HoldSuper(core);

        var next = Tap(core, "O");
        Assert.Equal(new WorkspaceId("2"), next.State.ActiveWorkspace);
        Assert.Equal([new WindowId(1)], next.Hide);

        var third = Tap(core, "O");
        Assert.Equal(new WorkspaceId("3"), third.State.ActiveWorkspace);

        var back = Tap(core, "I");
        Assert.Equal(new WorkspaceId("2"), back.State.ActiveWorkspace);

        var previous = Tap(core, "P");
        Assert.Equal(new WorkspaceId("3"), previous.State.ActiveWorkspace);

        var home = Tap(core, "1");
        Assert.Equal(new WorkspaceId("1"), home.State.ActiveWorkspace);
        Assert.Equal([new WindowId(1)], home.Show);
    }

    [Fact]
    public void NumericStepIncludesEmptyIdsAndMonitorStepUsesExistingWorkspaces()
    {
        var core = Load("""
            bind = SUPER SHIFT, 3, movetoworkspacesilent, 3
            bind = SUPER, R, workspace, r+1
            bind = SUPER, M, workspace, m+1
            bind = SUPER, P, workspace, previous
            """);
        Appear(core, 1);
        HoldSuper(core);
        core.Handle(new KeyPressed("Shift_L"));
        Tap(core, "3");
        core.Handle(new KeyReleased("Shift_L"));

        var stepped = Tap(core, "R");

        Assert.Equal(new WorkspaceId("2"), stepped.State.ActiveWorkspace);
        Assert.Contains(stepped.State.Hidden, client => client.Window == new WindowId(1) && client.Workspace == new WorkspaceId("3"));

        var home = Tap(core, "P");
        Assert.Equal(new WorkspaceId("1"), home.State.ActiveWorkspace);

        var cycled = Tap(core, "M");
        Assert.Equal(new WorkspaceId("3"), cycled.State.ActiveWorkspace);
        Assert.Equal([new WindowId(1)], cycled.Show);
    }

    [Fact]
    public void EmptyAddressOpensTheFirstEmptyWorkspace()
    {
        var core = Load("bind = SUPER, E, workspace, empty");
        Appear(core, 1);
        HoldSuper(core);

        var outcome = Tap(core, "E");

        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Contains(outcome.State.Hidden, client => client.Window == new WindowId(1));
    }

    [Fact]
    public void MinimizedClientStaysMinimizedAcrossASwitch()
    {
        var core = Load("""
            bind = SUPER, 1, workspace, 1
            bind = SUPER, 2, workspace, 2
            """);
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new WindowMinimized(new WindowId(2)));
        HoldSuper(core);
        var away = Tap(core, "2");
        Assert.Contains(away.Hide, window => window == new WindowId(2));

        var back = Tap(core, "1");

        Assert.Equal([new WindowId(1)], back.Show);
        Assert.DoesNotContain(back.Show, window => window == new WindowId(2));
        Assert.Equal([new WindowId(2)], back.ShowMinimized);
        Assert.True(Client(back, 2).Minimized);
        Assert.Empty(core.Handle(new WindowFocused(new WindowId(1))).Hide);
        Assert.Equal(Area, BoundsOf(back, 1));
        Assert.DoesNotContain(back.Placements, placement => placement.Client == Client(back, 2).Id);
    }

    [Fact]
    public void DispatchSwitchesTheActiveWorkspace()
    {
        var core = new WindowManager();
        Appear(core, 1);
        Appear(core, 2);

        var outcome = core.Handle(new Dispatch("workspace", "2"));

        Assert.False(outcome.Consumed);
        Assert.Null(outcome.Close);
        Assert.Null(outcome.Focus);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal([new WindowId(1), new WindowId(2)], outcome.Hide);
        Assert.Empty(outcome.Show);
        Assert.All(outcome.State.Hidden, client => Assert.Equal(new WorkspaceId("1"), client.Workspace));
    }

    [Fact]
    public void DispatchMoveToWorkspaceFollowsAndSilentStays()
    {
        var core = new WindowManager();
        Appear(core, 1);
        Appear(core, 2);

        var moved = core.Handle(new Dispatch("movetoworkspacesilent", "3"));

        Assert.False(moved.Consumed);
        Assert.Equal(WorkspaceId.Initial, moved.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("3"), Client(moved, 2).Workspace);
        Assert.Equal([new WindowId(2)], moved.Hide);
        Assert.Equal(Area, BoundsOf(moved, 1));

        var followed = core.Handle(new Dispatch("movetoworkspace", "3"));

        Assert.Equal(new WorkspaceId("3"), followed.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("3"), Client(followed, 1).Workspace);
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(followed, 2));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(followed, 1));
        Assert.Equal([new WindowId(2), new WindowId(1)], followed.Show);
        Assert.Empty(followed.Hide);
    }

    [Theory]
    [InlineData("movetoworkspace")]
    [InlineData("movetoworkspacesilent")]
    public void MovingAHiddenClientOntoTheShownWorkspaceShowsIt(string dispatcher)
    {
        var core = new WindowManager();
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new Dispatch("movetoworkspacesilent", "3"));

        var moved = core.Handle(new Dispatch(dispatcher, "1,address:0x2"));

        Assert.Equal(WorkspaceId.Initial, moved.State.ActiveWorkspace);
        Assert.Equal(WorkspaceId.Initial, Client(moved, 2).Workspace);
        Assert.Contains(new WindowId(2), moved.Show);
        Assert.DoesNotContain(new WindowId(2), moved.Hide);
        Assert.DoesNotContain(moved.State.Hidden, client => client.Window == new WindowId(2));
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(moved, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(moved, 2));
        if (dispatcher == "movetoworkspace")
            Assert.Equal(new WindowId(2), moved.Focus);
    }

    [Fact]
    public void MoveFocusStaysInsideTheActiveWorkspace()
    {
        var core = Load("bind = SUPER, H, movefocus, l");
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new Dispatch("workspace", "2"));
        Appear(core, 3);
        Appear(core, 4);
        HoldSuper(core);

        var focused = Tap(core, "H");

        Assert.True(focused.Consumed);
        Assert.Equal(new WindowId(3), focused.Focus);
        Assert.Equal(new WorkspaceId("2"), focused.State.ActiveWorkspace);
        Assert.Contains(focused.State.Hidden, client => client.Window == new WindowId(1));
        Assert.Contains(focused.State.Hidden, client => client.Window == new WindowId(2));

        var stopped = core.Handle(new Dispatch("movefocus", "l"));

        Assert.Null(stopped.Focus);
        Assert.Equal(new WorkspaceId("2"), stopped.State.ActiveWorkspace);
        Assert.Equal(new WindowId(3), core.Handle(new Dispatch("killactive")).Close);

        var back = core.Handle(new Dispatch("workspace", "1"));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(back, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(back, 2));
        Assert.Contains(back.State.Hidden, client => client.Window == new WindowId(3));
        Assert.Contains(back.State.Hidden, client => client.Window == new WindowId(4));
    }

    [Fact]
    public void ToggleFloatingAfterSwitchingLeavesTheOtherWorkspaceTiled()
    {
        var core = Load("bind = SUPER, Space, togglefloating");
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new Dispatch("workspace", "2"));
        Appear(core, 3);
        HoldSuper(core);

        var floating = Tap(core, "Space");

        Assert.True(floating.Consumed);
        Assert.Equal(new WorkspaceId("2"), floating.State.ActiveWorkspace);
        Assert.True(Client(floating, 3).Floating);
        Assert.False(Client(floating, 1).Floating);
        Assert.False(Client(floating, 2).Floating);
        Assert.Equal(new Rectangle(640, 340, 640, 400), BoundsOf(floating, 3));

        var back = core.Handle(new Dispatch("workspace", "1"));

        Assert.Equal(new WorkspaceId("1"), back.State.ActiveWorkspace);
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(back, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(back, 2));
        Assert.Contains(back.Hide, window => window == new WindowId(3));
        Assert.Contains(back.State.Hidden, client => client.Window == new WindowId(3) && client.Floating);
    }

    [Fact]
    public void InvalidWorkspaceAddressDoesNotSwitch()
    {
        var core = Load("""
            bind = SUPER, Z, workspace, 0
            bind = SUPER, N, workspace, name:
            """);
        Appear(core, 1);
        HoldSuper(core);

        var zero = Tap(core, "Z");
        Assert.Equal(WorkspaceId.Initial, zero.State.ActiveWorkspace);
        Assert.Empty(zero.Hide);

        var blank = Tap(core, "N");
        Assert.Equal(WorkspaceId.Initial, blank.State.ActiveWorkspace);
        Assert.Contains(blank.State.Clients, client => client.Window == new WindowId(1));
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("all_monitors", "1")]
    [InlineData("true", "1")]
    [InlineData("0", "2")]
    public void BackAndForthGoesToThePreviousWorkspace(string value, string expected)
    {
        var core = Load($"binds:workspace_back_and_forth = {value}\n");
        Appear(core, 1);
        core.Execute("dispatch workspace 2");

        var again = core.Execute("dispatch workspace 2").Effect;

        Assert.Equal(new WorkspaceId(expected), again.State.ActiveWorkspace);
    }

    [Fact]
    public void BackAndForthStaysWithoutAPreviousWorkspace()
    {
        var core = Load("binds:workspace_back_and_forth = 1\n");
        Appear(core, 1);

        var same = core.Execute("dispatch workspace 1").Effect;

        Assert.Equal(new WorkspaceId("1"), same.State.ActiveWorkspace);
        Assert.Equal("{\"option\":\"binds:workspace_back_and_forth\",\"int\":1,\"set\":true}", core.Execute("getoption binds:workspace_back_and_forth").Text);
    }

    [Theory]
    [InlineData("all_monitors", 3, 1)]
    [InlineData("per_monitor", 1, 0)]
    public void BackAndForthPerMonitorLooksOnlyAtThisMonitorsWorkspaces(string value, int workspace, int monitor)
    {
        var core = Load($"binds:workspace_back_and_forth = {value}\n");
        core.Handle(new MonitorsChanged(
        [
            new Monitor("LEFT", "Left", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1080)),
            new Monitor("RIGHT", "Right", 1920, 0, 1920, 1080, 1, true, new Rectangle(1920, 0, 1920, 1080)),
        ]));
        // LEFT shows 4, then RIGHT shows 3, then focus returns to LEFT. The visit before that is 3, on RIGHT.
        // The windows keep Workspaces 1 and 2, so an empty one is not removed from the history.
        core.Execute("dispatch focusmonitor LEFT");
        core.Execute("dispatch workspace 2");
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        core.Execute("dispatch workspace 1");
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Execute("dispatch workspace 4");
        core.Execute("dispatch focusmonitor RIGHT");
        core.Execute("dispatch workspace 3");
        core.Execute("dispatch focusmonitor LEFT");

        var again = core.Execute("dispatch workspace 4").Effect;

        Assert.Equal(WorkspaceId.FromNumber(workspace, new MonitorId(monitor)), again.State.ActiveWorkspace);
    }

    [Fact]
    public void SwitchingToAnEmptyWorkspaceOnAnotherMonitorMovesTheCursorThere()
    {
        var core = TwoMonitors("");

        var switched = ShowTheRightsWorkspace(core);

        Assert.Equal(new WarpTo(new Point(2880, 540)), switched.Warp);
    }

    [Fact]
    public void SwitchingToAWorkspaceOnAnotherMonitorMovesTheCursorToItsFocusedClient()
    {
        var core = TwoMonitors("");
        core.Execute("dispatch focusmonitor RIGHT");
        core.Execute("dispatch workspace 2");
        core.Handle(new WindowAppeared(new WindowId(1), Right));
        core.Handle(new WindowAppeared(new WindowId(2), Right));
        core.Handle(new WindowFocused(new WindowId(1), FocusCause.User));
        core.Execute("dispatch focusmonitor LEFT");

        var switched = core.Execute("dispatch workspace previous").Effect;

        Assert.Equal(new WarpTo(new Point(2400, 540)), switched.Warp);
    }

    [Fact]
    public void NoWarpsKeepsTheCursorWhenASwitchLandsOnAnotherMonitor()
    {
        var core = TwoMonitors("cursor:no_warps = true\n");

        var switched = ShowTheRightsWorkspace(core);

        Assert.Null(switched.Warp);
    }

    [Fact]
    public void SwitchingOnTheSameMonitorDoesNotMoveTheCursor()
    {
        var core = TwoMonitors("");

        var switched = core.Execute("dispatch workspace 3").Effect;

        Assert.Null(switched.Warp);
    }

    private static readonly Rectangle Right = new(1920, 0, 1920, 1080);

    // RIGHT shows empty Workspace 2. previous from LEFT lands there.
    private static Outcome ShowTheRightsWorkspace(WindowManager core)
    {
        core.Execute("dispatch focusmonitor RIGHT");
        core.Execute("dispatch workspace 2");
        core.Execute("dispatch focusmonitor LEFT");
        return core.Execute("dispatch workspace previous").Effect;
    }

    private static WindowManager TwoMonitors(string config)
    {
        var core = Load(config);
        core.Handle(new MonitorsChanged(
        [
            new Monitor("LEFT", "Left", 0, 0, 1920, 1080, 1, true, Area),
            new Monitor("RIGHT", "Right", 1920, 0, 1920, 1080, 1, true, Right),
        ]));
        core.Execute("dispatch focusmonitor LEFT");
        return core;
    }

    // A loaded config applies its gaps and borders like a reload, and these rectangles have none.
    private const string Gapless = "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n";

    private static WindowManager Load(string text)
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(Gapless + text)));
        return core;
    }

    private static void HoldSuper(WindowManager core) => core.Handle(new KeyPressed("Super_L"));
}
