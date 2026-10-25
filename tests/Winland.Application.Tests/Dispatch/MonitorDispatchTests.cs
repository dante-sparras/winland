namespace Winland.Application.Tests;

public class MonitorDispatchTests
{
    [Fact]
    public void MoveFocusCrossesOntoTheNeighbouringMonitor()
    {
        var core = TwoMonitors();

        var outcome = core.Handle(new Dispatch("movefocus", "r"));

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
    public void MoveWindowCrossesOntoTheNeighbouringMonitor()
    {
        var core = TwoMonitors();

        var outcome = core.Handle(new Dispatch("movewindow", "r"));

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
        var core = new WindowManager();
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

    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 2)]
    public void MoveFocusCrossesToAClientWithinAOnePixelGapButNotTwo(int gap, long expected)
    {
        var core = TwoMonitors();
        var left = Display(@"\\.\DISPLAY1", "Dell", Left);
        var right = Display(@"\\.\DISPLAY2", "Samsung", Right);
        // Window 2 floats clear of the origin's rows, so only window 4 can be next to it.
        Float(core, 2, new Rectangle(2500, 900, 100, 100));
        core.Handle(new WindowAppeared(new WindowId(3), left.WorkArea));
        Float(core, 3, new Rectangle(Right.X - gap - 100, 100, 100, 100));
        core.Handle(new WindowAppeared(new WindowId(4), right.WorkArea));
        Float(core, 4, new Rectangle(Right.X, 100, 100, 100));
        Focus(core, 2);
        Focus(core, 3);

        var outcome = core.Handle(new Dispatch("movefocus", "r"));

        Assert.Equal(new WindowId(expected), outcome.Focus);
    }

    [Fact]
    public void MoveFocusCrossesToTheMostRecentNeighbor()
    {
        // Portrait monitors, the right one 300px higher, so window 1 shares 660px with window 2 and 960px with window 3.
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n")));
        var left = Display(@"\\.\DISPLAY1", "Dell", new Rectangle(0, 0, 1080, 1920));
        var right = Display(@"\\.\DISPLAY2", "Samsung", new Rectangle(1080, -300, 1080, 1920));
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        var opened = core.Handle(new WindowAppeared(new WindowId(3), right.WorkArea));
        Assert.Equal(new Rectangle(1080, 660, 1080, 960), Client(opened, 3).Bounds);
        Focus(core, 3);
        Focus(core, 2);
        Focus(core, 1);

        var outcome = core.Handle(new Dispatch("movefocus", "r"));

        Assert.Equal(new WindowId(2), outcome.Focus);
    }

    [Fact]
    public void MoveFocusFromAFloatingClientCrossesToAFloatingClientBeforeATile()
    {
        var core = TwoMonitors();
        var left = Display(@"\\.\DISPLAY1", "Dell", Left);
        var right = Display(@"\\.\DISPLAY2", "Samsung", Right);
        core.Handle(new WindowAppeared(new WindowId(3), left.WorkArea));
        Float(core, 3, new Rectangle(Right.X - 100, 100, 100, 100));
        core.Handle(new WindowAppeared(new WindowId(4), right.WorkArea));
        Float(core, 4, new Rectangle(Right.X, 100, 100, 100));
        // The tile was focused after the floating window, and shares as much of the edge.
        Focus(core, 4);
        Focus(core, 2);
        Focus(core, 3);

        var outcome = core.Handle(new Dispatch("movefocus", "r"));

        Assert.Equal(new WindowId(4), outcome.Focus);
    }

    // The focus history only moves when the window reports it has the focus.
    private static void Focus(WindowManager core, long window)
    {
        core.Handle(new Dispatch("focuswindow", $"address:0x{window:x}"));
        core.Handle(new WindowFocused(new WindowId(window)));
    }

    private static void Float(WindowManager core, long window, Rectangle bounds)
    {
        Focus(core, window);
        core.Handle(new Dispatch("togglefloating"));
        var moved = Place(core, window, bounds);
        Assert.Equal(bounds, Client(moved, window).Bounds);
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
        var core = TwoMonitors("windowrule = float, class:^(stick)$\n");
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
    public void MoveWindowMonTargetsTheNamedMonitor()
    {
        var core = TwoMonitors();

        var outcome = core.Handle(new Dispatch("movewindow", @"mon:\\.\DISPLAY2"));

        Assert.Equal(new WindowId(1), outcome.Focus);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), Client(outcome, 1).Workspace);
        Assert.Equal(new MonitorId(1), outcome.State.ActiveMonitor);
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
    [InlineData("bind = SUPER, K, exec, notepad\nbind = SUPER, K, movefocus, r\n")]
    [InlineData("bind = SUPER, K, movefocus, r\nbind = SUPER, K, exec, notepad\n")]
    public void ABindThatMovesFocusMovesTheCursorWhateverOtherDispatchersItRuns(string config)
    {
        var core = TwoMonitors(config);
        core.Handle(new KeyPressed("Super_L"));

        var moved = core.Handle(new KeyPressed("K"));

        var target = Client(moved, 2).Bounds;
        Assert.Equal(new WarpTo(new Point(target.X + (target.Width / 2), target.Y + (target.Height / 2))), moved.Warp);
    }

    [Fact]
    public void MoveFocusAnswersNoErrorWhenNothingTakesTheFocus()
    {
        var core = TwoMonitors();

        var moved = core.Handle(new Dispatch("movefocus", "l"));

        Assert.Null(moved.Error);
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

    private static WindowManager Gapped(string? config = null)
    {
        var text = config ?? """
            general:gaps_in = 0
            general:gaps_out = 20
            general:border_size = 0
            """;
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(text)));
        var left = Display(@"\\.\DISPLAY1", "Dell", Left);
        var right = Display(@"\\.\DISPLAY2", "Samsung", Right);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        return core;
    }

    private static Workspace Workspace(Outcome outcome, WorkspaceId id) =>
        Assert.Single(outcome.State.Workspaces, workspace => workspace.Id == id);

    private static IReadOnlyList<string> Lines(Outcome outcome) =>
        CommandProtocol.FormatStream(outcome.Events, outcome.State);
}
