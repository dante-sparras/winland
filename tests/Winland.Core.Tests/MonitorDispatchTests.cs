using Winland;

namespace Winland.Tests;

public class MonitorDispatchTests
{
    [Fact]
    public void MoveFocusCrossesOntoTheNeighbouringMonitor()
    {
        var core = TwoMonitors();

        var outcome = core.Handle(new Dispatch("movefocus", "r"));

        Assert.Equal("movefocus", outcome.Dispatcher);
        Assert.Equal(new WindowId(2), outcome.Focus);
        Assert.Equal(new MonitorId(1), outcome.State.ActiveMonitor);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), outcome.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("1"), Client(outcome, 1).Workspace);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), Client(outcome, 2).Workspace);
        // The Monitor already shows this Workspace, so the focus change is not a workspace event.
        Assert.Equal(
            [
                @"focusedmon>>\\.\DISPLAY2,1",
                @"focusedmonv2>>\\.\DISPLAY2,1",
                "activewindow>>,",
                "activewindowv2>>2",
            ],
            Lines(outcome));
    }

    [Fact]
    public void MoveFocusStopsAtTheMonitorEdgeWhenFallbackIsOff()
    {
        var core = TwoMonitors("binds:window_direction_monitor_fallback = false\n");

        var outcome = core.Handle(new Dispatch("movefocus", "right"));

        Assert.Null(outcome.Focus);
        Assert.Null(outcome.Error);
        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("killactive")).Close);
    }

    [Fact]
    public void MoveWindowCrossesOntoTheNeighbouringMonitor()
    {
        var core = TwoMonitors();

        var outcome = core.Handle(new Dispatch("movewindow", "r"));

        Assert.Equal("movewindow", outcome.Dispatcher);
        Assert.Equal(new WindowId(1), outcome.Focus);
        Assert.Equal(new MonitorId(1), outcome.State.ActiveMonitor);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), outcome.State.ActiveWorkspace);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), Client(outcome, 1).Workspace);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), Client(outcome, 2).Workspace);
        // the window goes in at the point just past the edge it crossed.
        Assert.Equal(new Rectangle(1920, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(2880, 0, 960, 1080), BoundsOf(outcome, 2));
        Assert.Equal(
            [
                @"movewindow>>1,1,\\.\DISPLAY2",
                @"movewindowv2>>1,1,1,\\.\DISPLAY2",
                @"focusedmon>>\\.\DISPLAY2,1",
                @"focusedmonv2>>\\.\DISPLAY2,1",
            ],
            Lines(outcome));
    }

    [Fact]
    public void MoveWindowStopsAtTheMonitorEdgeWhenFallbackIsOff()
    {
        var core = TwoMonitors("binds:window_direction_monitor_fallback = false\n");

        var outcome = core.Handle(new Dispatch("movewindow", "r"));

        Assert.Null(outcome.Error);
        Assert.Equal(new WorkspaceId("1"), Client(outcome, 1).Workspace);
        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("killactive")).Close);
    }

    [Fact]
    public void MoveFocusStillCrossesWhenOuterGapsKeepTheClientInset()
    {
        var core = Gapped();

        var outcome = core.Handle(new Dispatch("movefocus", "r"));

        Assert.Equal(new WindowId(2), outcome.Focus);
        Assert.Equal(new MonitorId(1), outcome.State.ActiveMonitor);
    }

    [Fact]
    public void MoveFocusCrossesToTheTileThatSharesTheEdgeWithDefaultGaps()
    {
        // Portrait monitors, so each Workspace stacks its second tile below the first. Outer gaps keep
        // the tiles 44px apart across the shared edge.
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("general:gaps_in = 5\ngeneral:gaps_out = 20\ngeneral:border_size = 2\n")));
        var left = Display(@"\\.\DISPLAY1", "Dell", new Rectangle(0, 0, 1080, 1920));
        var right = Display(@"\\.\DISPLAY2", "Samsung", new Rectangle(1080, 0, 1080, 1920));
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(3), right.WorkArea));
        var opened = core.Handle(new WindowAppeared(new WindowId(4), right.WorkArea));
        Assert.True(Client(opened, 3).Bounds.Y < Client(opened, 4).Bounds.Y);
        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("focuswindow", "address:0x1")).Focus);

        var outcome = core.Handle(new Dispatch("movefocus", "r"));

        Assert.Equal(new WindowId(3), outcome.Focus);
    }

    [Fact]
    public void DwindleMoveWindowCrossesFromATileAtTheEdgeOfTheGaps()
    {
        // a tile that meets the work area's edge reaches the Monitor's for the focal point,
        // so gaps_out does not keep it on this Monitor.
        var core = Gapped();

        var outcome = core.Handle(new Dispatch("movewindow", "r"));

        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), Client(outcome, 1).Workspace);
        Assert.Equal(new MonitorId(1), outcome.State.ActiveMonitor);
    }

    [Fact]
    public void MasterMoveWindowCrossesWhenItCannotMoveInsideTheWorkspace()
    {
        var core = Gapped("""
            general:layout = master
            general:gaps_in = 0
            general:gaps_out = 20
            general:border_size = 0
            """);

        var outcome = core.Handle(new Dispatch("movewindow", "r"));

        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), Client(outcome, 1).Workspace);
        Assert.Equal(new MonitorId(1), outcome.State.ActiveMonitor);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), outcome.State.ActiveWorkspace);
    }

    [Fact]
    public void FocusMonitorAddressesDirectionNameIdAndDescription()
    {
        var core = TwoMonitors();

        var right = core.Handle(new Dispatch("focusmonitor", "r"));
        Assert.Equal("focusmonitor", right.Dispatcher);
        Assert.Equal(new WindowId(2), right.Focus);
        Assert.Equal(new MonitorId(1), right.State.ActiveMonitor);
        Assert.Equal(
            [
                @"focusedmon>>\\.\DISPLAY2,1",
                @"focusedmonv2>>\\.\DISPLAY2,1",
                "activewindow>>,",
                "activewindowv2>>2",
            ],
            Lines(right));

        var same = core.Handle(new Dispatch("focusmonitor", "current"));
        Assert.Null(same.Error);
        Assert.Equal(new MonitorId(1), same.State.ActiveMonitor);
        Assert.Empty(same.Events);

        var left = core.Handle(new Dispatch("focusmonitor", @"\\.\DISPLAY1"));
        Assert.Equal(new WindowId(1), left.Focus);
        Assert.Equal(new MonitorId(0), left.State.ActiveMonitor);

        var byId = core.Handle(new Dispatch("focusmonitor", "1"));
        Assert.Equal(new MonitorId(1), byId.State.ActiveMonitor);
        Assert.Equal(new WindowId(2), byId.Focus);

        var described = core.Handle(new Dispatch("focusmonitor", "desc:Dell"));
        Assert.Equal(new MonitorId(0), described.State.ActiveMonitor);

        var relative = core.Handle(new Dispatch("focusmonitor", "+1"));
        Assert.Equal(new MonitorId(1), relative.State.ActiveMonitor);

        var missing = core.Handle(new Dispatch("focusmonitor", "missing"));
        Assert.Equal("error: monitor not found", missing.Error);
        Assert.Equal(new MonitorId(1), missing.State.ActiveMonitor);
    }

    [Fact]
    public void MoveCurrentWorkspaceFollowsOntoTheOtherMonitor()
    {
        var core = TwoMonitors();

        var outcome = core.Handle(new Dispatch("movecurrentworkspacetomonitor", "right"));

        Assert.Equal("movecurrentworkspacetomonitor", outcome.Dispatcher);
        Assert.Equal(new WindowId(1), outcome.Focus);
        Assert.Equal(new MonitorId(1), outcome.State.ActiveMonitor);
        Assert.Equal(WorkspaceId.FromNumber(2, new MonitorId(1)), outcome.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), Workspace(outcome, new WorkspaceId("2")).Monitor);
        Assert.Equal(new MonitorId(1), Workspace(outcome, WorkspaceId.FromNumber(2, new MonitorId(1))).Monitor);
        Assert.Equal(WorkspaceId.FromNumber(2, new MonitorId(1)), Client(outcome, 1).Workspace);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), Client(outcome, 2).Workspace);
        Assert.Equal(Right, BoundsOf(outcome, 1));
        Assert.Equal([new WindowId(2)], outcome.Hide);
        Assert.Contains(outcome.State.Hidden, client => client.Window == new WindowId(2));
        Assert.Equal(
            [
                @"createworkspace>>2,\\.\DISPLAY1",
                @"createworkspacev2>>2,2,\\.\DISPLAY1",
                @"moveworkspace>>2,\\.\DISPLAY2",
                @"moveworkspacev2>>2,2,\\.\DISPLAY2",
                @"focusedmon>>\\.\DISPLAY2,2",
                @"focusedmonv2>>\\.\DISPLAY2,2",
            ],
            Lines(outcome));

        var monitors = System.Text.Json.JsonDocument.Parse(core.Execute("j/monitors").Text).RootElement.EnumerateArray().ToArray();
        Assert.Equal("2", monitors[0].GetProperty("activeWorkspace").GetProperty("name").GetString());
        Assert.Equal(@"\\.\DISPLAY1", monitors[0].GetProperty("activeWorkspace").GetProperty("monitor").GetString());
        Assert.False(monitors[0].GetProperty("focused").GetBoolean());
        Assert.Equal("2", monitors[1].GetProperty("activeWorkspace").GetProperty("name").GetString());
        Assert.Equal(@"\\.\DISPLAY2", monitors[1].GetProperty("activeWorkspace").GetProperty("monitor").GetString());
        Assert.True(monitors[1].GetProperty("focused").GetBoolean());
    }

    [Fact]
    public void MovingTheActiveWorkspaceLeavesAPinnedClientOnTheSourceMonitor()
    {
        var core = TwoMonitors("windowrulev2 = float, class:^(stick)$\n");
        core.Handle(new WindowAppeared(new WindowId(4), Left) { Traits = WindowTraits.Normal with { Class = "stick" } });
        core.Handle(new Dispatch("pin"));

        var outcome = core.Handle(new Dispatch("movecurrentworkspacetomonitor", "right"));

        var pinned = Client(outcome, 4);
        Assert.True(pinned.Pinned);
        Assert.Equal(new WorkspaceId("2"), pinned.Workspace);
        Assert.Equal(new MonitorId(0), Workspace(outcome, new WorkspaceId("2")).Monitor);
        Assert.DoesNotContain(outcome.State.Hidden, client => client.Window == new WindowId(4));
        Assert.Equal(WorkspaceId.FromNumber(2, new MonitorId(1)), Client(outcome, 1).Workspace);
        Assert.Equal(new MonitorId(1), Workspace(outcome, WorkspaceId.FromNumber(2, new MonitorId(1))).Monitor);
    }

    [Fact]
    public void MoveWorkspaceParksAnUnfocusedMonitorWorkspaceWithoutFollowing()
    {
        var core = TwoMonitors();
        core.Handle(new Dispatch("focusmonitor", @"\\.\DISPLAY2"));
        core.Handle(new Dispatch("focusmonitor", @"\\.\DISPLAY1"));

        var outcome = core.Handle(new Dispatch("moveworkspacetomonitor", @"previous \\.\DISPLAY1"));

        Assert.Equal("moveworkspacetomonitor", outcome.Dispatcher);
        Assert.Null(outcome.Focus);
        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("1"), outcome.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 2).Workspace);
        Assert.Equal(new MonitorId(0), Workspace(outcome, new WorkspaceId("2")).Monitor);
        Assert.Equal(new MonitorId(1), Workspace(outcome, WorkspaceId.FromNumber(2, new MonitorId(1))).Monitor);
        Assert.Equal(
            WorkspaceId.FromNumber(2, new MonitorId(1)),
            Assert.Single(outcome.State.Screens, screen => screen.Id == new MonitorId(1)).ActiveWorkspace);
        Assert.Contains(outcome.State.Hidden, client => client.Window == new WindowId(2));
        Assert.Equal(
            [
                @"createworkspace>>2,\\.\DISPLAY2",
                @"createworkspacev2>>2,2,\\.\DISPLAY2",
                @"moveworkspace>>2,\\.\DISPLAY1",
                @"moveworkspacev2>>2,2,\\.\DISPLAY1",
            ],
            Lines(outcome));
    }

    [Fact]
    public void MoveWorkspaceRejectsAMissingPair()
    {
        var core = TwoMonitors();

        Assert.Equal(
            "error: expected workspace and monitor",
            core.Handle(new Dispatch("moveworkspacetomonitor", "2")).Error);
        Assert.Equal(
            "error: workspace not found",
            core.Handle(new Dispatch("moveworkspacetomonitor", @"9 \\.\DISPLAY2")).Error);
        Assert.Equal(
            "error: monitor not found",
            core.Handle(new Dispatch("moveworkspacetomonitor", "1 missing")).Error);
    }

    [Fact]
    public void SwapActiveWorkspacesExchangesTheTwoMonitors()
    {
        var core = TwoMonitors();

        var outcome = core.Handle(new Dispatch("swapactiveworkspaces", @"\\.\DISPLAY1 \\.\DISPLAY2"));

        Assert.Equal("swapactiveworkspaces", outcome.Dispatcher);
        Assert.Equal(new WindowId(2), outcome.Focus);
        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("1"), outcome.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), Workspace(outcome, new WorkspaceId("1")).Monitor);
        Assert.Equal(new MonitorId(1), Workspace(outcome, WorkspaceId.FromNumber(1, new MonitorId(1))).Monitor);
        Assert.Equal(Left, BoundsOf(outcome, 2));
        Assert.Equal(Right, BoundsOf(outcome, 1));
        Assert.Empty(outcome.Hide);
        Assert.Equal(new WorkspaceId("1"), Client(outcome, 2).Workspace);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), Client(outcome, 1).Workspace);
        Assert.Equal(new WorkspaceId("1"), Assert.Single(outcome.State.Screens, screen => screen.Id == new MonitorId(0)).ActiveWorkspace);
        Assert.Equal(
            WorkspaceId.FromNumber(1, new MonitorId(1)),
            Assert.Single(outcome.State.Screens, screen => screen.Id == new MonitorId(1)).ActiveWorkspace);
        Assert.Equal(
            [
                @"moveworkspace>>1,\\.\DISPLAY2",
                @"moveworkspacev2>>1,1,\\.\DISPLAY2",
                @"moveworkspace>>1,\\.\DISPLAY1",
                @"moveworkspacev2>>1,1,\\.\DISPLAY1",
                @"workspace>>1,\\.\DISPLAY1",
                @"workspacev2>>1,1,\\.\DISPLAY1",
                "activewindow>>,",
                "activewindowv2>>2",
            ],
            Lines(outcome));

        var same = core.Handle(new Dispatch("swapactiveworkspaces", "current current"));
        Assert.Null(same.Error);
        Assert.Equal(new WorkspaceId("1"), same.State.ActiveWorkspace);
        Assert.Empty(same.Events);
    }

    [Fact]
    public void SwapActiveWorkspacesOfTwoUnfocusedMonitorsReportsNoWorkspace()
    {
        var core = new Core();
        var left = Display(@"\\.\DISPLAY1", "Dell", new Rectangle(0, 0, 800, 600));
        var middle = Display(@"\\.\DISPLAY2", "Samsung", new Rectangle(800, 0, 800, 600));
        var right = Display(@"\\.\DISPLAY3", "LG", new Rectangle(1600, 0, 800, 600));
        core.Handle(new MonitorsChanged([left, middle, right]));
        core.Handle(new Dispatch("focusmonitor", @"\\.\DISPLAY2"));
        core.Handle(new Dispatch("workspace", "3"));
        core.Handle(new Dispatch("focusmonitor", @"\\.\DISPLAY3"));
        core.Handle(new Dispatch("workspace", "4"));
        core.Handle(new Dispatch("focusmonitor", @"\\.\DISPLAY1"));

        var outcome = core.Handle(new Dispatch("swapactiveworkspaces", @"\\.\DISPLAY2 \\.\DISPLAY3"));

        var middleScreen = Assert.Single(outcome.State.Screens, screen => screen.Monitor.Name == middle.Name);
        var rightScreen = Assert.Single(outcome.State.Screens, screen => screen.Monitor.Name == right.Name);
        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("1"), outcome.State.ActiveWorkspace);
        Assert.Equal(WorkspaceId.FromNumber(4, middleScreen.Id), middleScreen.ActiveWorkspace);
        Assert.Equal(WorkspaceId.FromNumber(3, rightScreen.Id), rightScreen.ActiveWorkspace);
        Assert.Equal(
            [
                @"moveworkspace>>3,\\.\DISPLAY3",
                @"moveworkspacev2>>3,3,\\.\DISPLAY3",
                @"moveworkspace>>4,\\.\DISPLAY2",
                @"moveworkspacev2>>4,4,\\.\DISPLAY2",
            ],
            Lines(outcome));
    }

    [Fact]
    public void MoveWindowMonTargetsTheNamedMonitor()
    {
        var core = TwoMonitors();

        var outcome = core.Handle(new Dispatch("movewindow", @"mon:\\.\DISPLAY2"));

        Assert.Equal(new WindowId(1), outcome.Focus);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), Client(outcome, 1).Workspace);
        Assert.Equal(new MonitorId(1), outcome.State.ActiveMonitor);
    }

    [Fact]
    public void WindowDirectionMonitorFallbackIsHonored()
    {
        var document = ConfigParser.Parse("binds:window_direction_monitor_fallback = false\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new Setting("binds:window_direction_monitor_fallback", "false"),
            Assert.Single(document.Settings));

        var core = new Core();
        Assert.Equal(
            "{\"option\":\"binds:window_direction_monitor_fallback\",\"bool\":true,\"set\":false}",
            core.Execute("getoption binds:window_direction_monitor_fallback").Text);
        core.Handle(new Configure(document));
        Assert.Equal(
            "{\"option\":\"binds:window_direction_monitor_fallback\",\"bool\":false,\"set\":true}",
            core.Execute("getoption binds:window_direction_monitor_fallback").Text);
    }

    private static readonly Rectangle Left = new(0, 0, 1920, 1080);

    private static readonly Rectangle Right = new(1920, 0, 1920, 1080);

    [Fact]
    public void MoveFocusOntoAnotherMonitorMovesTheCursorToTheWindowsMiddle()
    {
        var core = TwoMonitors();

        var moved = core.Handle(new Dispatch("movefocus", "r"));

        var target = Client(moved, 2).Bounds;
        Assert.Equal(new WarpTo(new Point(target.X + (target.Width / 2), target.Y + (target.Height / 2))), moved.Warp);
    }

    [Fact]
    public void NoWarpsKeepsTheCursorWhereItIsOnMoveFocus()
    {
        var core = TwoMonitors("cursor:no_warps = true\n");

        var moved = core.Handle(new Dispatch("movefocus", "r"));

        Assert.Null(moved.Warp);
    }

    [Theory]
    [InlineData("true", "error: Nothing to focus to in direction left")]
    [InlineData("false", null)]
    public void NoFocusFallbackAnswersAnErrorWhenNothingTakesTheFocus(string value, string? error)
    {
        var core = TwoMonitors($"general:no_focus_fallback = {value}\n");

        var moved = core.Handle(new Dispatch("movefocus", "l"));

        Assert.Equal(error, moved.Error);
    }

    private static Core TwoMonitors(string? config = null)
    {
        var core = config is null ? new Core() : new Core(ConfigParser.Parse(config));
        var left = Display(@"\\.\DISPLAY1", "Dell", Left);
        var right = Display(@"\\.\DISPLAY2", "Samsung", Right);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        return core;
    }

    private static Core Gapped(string? config = null)
    {
        var text = config ?? """
            general:gaps_in = 0
            general:gaps_out = 20
            general:border_size = 0
            """;
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(text)));
        var left = Display(@"\\.\DISPLAY1", "Dell", Left);
        var right = Display(@"\\.\DISPLAY2", "Samsung", Right);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        return core;
    }

    private static Monitor Display(string name, string description, Rectangle area) =>
        new(name, description, area.X, area.Y, area.Width, area.Height, 1, true, area);

    private static Workspace Workspace(Outcome outcome, WorkspaceId id) =>
        Assert.Single(outcome.State.Workspaces, workspace => workspace.Id == id);

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Client(outcome, window);
        return Assert.Single(outcome.Placements, placement => placement.Client == client.Id).Bounds;
    }

    private static IReadOnlyList<string> Lines(Outcome outcome) =>
        CommandProtocol.FormatStream(outcome.Events, outcome.State);
}
