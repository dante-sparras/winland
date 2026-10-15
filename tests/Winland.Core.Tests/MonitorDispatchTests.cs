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
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("1"), Client(outcome, 1).Workspace);
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 2).Workspace);
        Assert.Equal(
            [
                @"focusedmon>>\\.\DISPLAY2,2",
                @"focusedmonv2>>\\.\DISPLAY2,2",
                "workspace>>2",
                "workspacev2>>2,2",
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
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 1).Workspace);
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 2).Workspace);
        Assert.Equal(new Rectangle(1920, 0, 960, 1080), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(2880, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(
            [
                "movewindow>>1,2",
                "movewindowv2>>1,2,2",
                @"focusedmon>>\\.\DISPLAY2,2",
                @"focusedmonv2>>\\.\DISPLAY2,2",
                "workspace>>2",
                "workspacev2>>2,2",
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
    public void DwindleMoveWindowStaysWhenTheFocalPointIsStillOnThisMonitor()
    {
        var core = Gapped();

        var outcome = core.Handle(new Dispatch("movewindow", "r"));

        Assert.Equal(new WorkspaceId("1"), Client(outcome, 1).Workspace);
        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
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

        Assert.Equal(new WorkspaceId("2"), Client(outcome, 1).Workspace);
        Assert.Equal(new MonitorId(1), outcome.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
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
                @"focusedmon>>\\.\DISPLAY2,2",
                @"focusedmonv2>>\\.\DISPLAY2,2",
                "workspace>>2",
                "workspacev2>>2,2",
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
        Assert.Equal(new WorkspaceId("1"), outcome.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(1), Workspace(outcome, "1").Monitor);
        Assert.Equal(new MonitorId(1), Workspace(outcome, "2").Monitor);
        Assert.Equal(new MonitorId(0), Workspace(outcome, "3").Monitor);
        Assert.Equal(new WorkspaceId("1"), Client(outcome, 1).Workspace);
        Assert.Equal(Right, BoundsOf(outcome, 1));
        Assert.Equal([new WindowId(2)], outcome.Hide);
        Assert.Contains(outcome.State.Hidden, client => client.Window == new WindowId(2));
        Assert.Equal(
            [
                "createworkspace>>3",
                "createworkspacev2>>3,3",
                @"moveworkspace>>1,\\.\DISPLAY2",
                @"moveworkspacev2>>1,1,\\.\DISPLAY2",
                @"focusedmon>>\\.\DISPLAY2,1",
                @"focusedmonv2>>\\.\DISPLAY2,1",
            ],
            Lines(outcome));

        var monitors = System.Text.Json.JsonDocument.Parse(core.Execute("monitors").Text).RootElement.EnumerateArray().ToArray();
        Assert.Equal("3", monitors[0].GetProperty("activeWorkspace").GetProperty("name").GetString());
        Assert.False(monitors[0].GetProperty("focused").GetBoolean());
        Assert.Equal("1", monitors[1].GetProperty("activeWorkspace").GetProperty("name").GetString());
        Assert.True(monitors[1].GetProperty("focused").GetBoolean());
    }

    [Fact]
    public void MoveWorkspaceParksAnUnfocusedMonitorWorkspaceWithoutFollowing()
    {
        var core = TwoMonitors();

        var outcome = core.Handle(new Dispatch("moveworkspacetomonitor", @"2 \\.\DISPLAY1"));

        Assert.Equal("moveworkspacetomonitor", outcome.Dispatcher);
        Assert.Null(outcome.Focus);
        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("1"), outcome.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), Workspace(outcome, "2").Monitor);
        Assert.Equal(new MonitorId(1), Workspace(outcome, "3").Monitor);
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 2).Workspace);
        Assert.Contains(outcome.State.Hidden, client => client.Window == new WindowId(2));
        Assert.Equal(
            [
                "createworkspace>>3",
                "createworkspacev2>>3,3",
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
            core.Handle(new Dispatch("moveworkspacetomonitor", "2 missing")).Error);
    }

    [Fact]
    public void SwapActiveWorkspacesExchangesTheTwoMonitors()
    {
        var core = TwoMonitors();

        var outcome = core.Handle(new Dispatch("swapactiveworkspaces", @"\\.\DISPLAY1 \\.\DISPLAY2"));

        Assert.Equal("swapactiveworkspaces", outcome.Dispatcher);
        Assert.Equal(new WindowId(2), outcome.Focus);
        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), Workspace(outcome, "2").Monitor);
        Assert.Equal(new MonitorId(1), Workspace(outcome, "1").Monitor);
        Assert.Equal(Left, BoundsOf(outcome, 2));
        Assert.Equal(Right, BoundsOf(outcome, 1));
        Assert.Empty(outcome.Hide);
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 2).Workspace);
        Assert.Equal(new WorkspaceId("1"), Client(outcome, 1).Workspace);
        Assert.Equal(
            [
                @"moveworkspace>>1,\\.\DISPLAY2",
                @"moveworkspacev2>>1,1,\\.\DISPLAY2",
                @"moveworkspace>>2,\\.\DISPLAY1",
                @"moveworkspacev2>>2,2,\\.\DISPLAY1",
                "workspace>>2",
                "workspacev2>>2,2",
                "activewindow>>,",
                "activewindowv2>>2",
            ],
            Lines(outcome));

        var same = core.Handle(new Dispatch("swapactiveworkspaces", "current current"));
        Assert.Null(same.Error);
        Assert.Equal(new WorkspaceId("2"), same.State.ActiveWorkspace);
        Assert.Empty(same.Events);
    }

    [Fact]
    public void MoveWindowMonTargetsTheNamedMonitor()
    {
        var core = TwoMonitors();

        var outcome = core.Handle(new Dispatch("movewindow", @"mon:\\.\DISPLAY2"));

        Assert.Equal(new WindowId(1), outcome.Focus);
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 1).Workspace);
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

    private static Workspace Workspace(Outcome outcome, string name) =>
        Assert.Single(outcome.State.Workspaces, workspace => workspace.Id == new WorkspaceId(name));

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
