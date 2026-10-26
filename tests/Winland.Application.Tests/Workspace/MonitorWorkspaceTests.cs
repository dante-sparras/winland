namespace Winland.Application.Tests;

public class MonitorWorkspaceTests
{
    [Fact]
    public void EachEnabledMonitorShowsItsOwnWorkspace()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1.5);
        core.Handle(new MonitorsChanged([left, right]));

        var first = core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        var second = core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        Assert.Equal(left.WorkArea, BoundsOf(first, 1));
        Assert.Equal(right.WorkArea, BoundsOf(second, 2));
        Assert.Equal(new WorkspaceId("1"), Client(second, 1).Workspace);
        Assert.Equal(new WorkspaceId("1", 1), Client(second, 2).Workspace);
        Assert.Equal(new MonitorId(0), Workspace(second, "1").Monitor);
        Assert.Equal(new MonitorId(1), On(second, 1, "1").Monitor);
        Assert.Equal(new MonitorId(0), second.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("1"), second.State.ActiveWorkspace);
        Assert.DoesNotContain(second.State.Hidden, client => client.Window == new WindowId(1));
        Assert.DoesNotContain(second.State.Hidden, client => client.Window == new WindowId(2));

        var monitors = Json(core.Execute("j/monitors").Text).EnumerateArray().ToArray();
        Assert.Equal(2, monitors.Length);
        Assert.Equal(0, monitors[0].GetProperty("id").GetInt32());
        Assert.Equal(@"\\.\DISPLAY1", monitors[0].GetProperty("name").GetString());
        Assert.Equal("Dell", monitors[0].GetProperty("description").GetString());
        Assert.Equal(0, monitors[0].GetProperty("x").GetInt32());
        Assert.Equal(0, monitors[0].GetProperty("y").GetInt32());
        Assert.Equal(1920, monitors[0].GetProperty("width").GetInt32());
        Assert.Equal(1080, monitors[0].GetProperty("height").GetInt32());
        Assert.Equal(1, monitors[0].GetProperty("scale").GetDouble());
        Assert.True(monitors[0].GetProperty("focused").GetBoolean());
        Assert.Equal(1, monitors[0].GetProperty("activeWorkspace").GetProperty("id").GetInt32());
        Assert.Equal("1", monitors[0].GetProperty("activeWorkspace").GetProperty("name").GetString());

        Assert.Equal(1, monitors[1].GetProperty("id").GetInt32());
        Assert.Equal(@"\\.\DISPLAY2", monitors[1].GetProperty("name").GetString());
        Assert.Equal("Samsung", monitors[1].GetProperty("description").GetString());
        Assert.Equal(1920, monitors[1].GetProperty("x").GetInt32());
        Assert.Equal(0, monitors[1].GetProperty("y").GetInt32());
        Assert.Equal(1280, monitors[1].GetProperty("width").GetInt32());
        Assert.Equal(1024, monitors[1].GetProperty("height").GetInt32());
        Assert.Equal(1.5, monitors[1].GetProperty("scale").GetDouble());
        Assert.False(monitors[1].GetProperty("focused").GetBoolean());
        Assert.Equal(1, monitors[1].GetProperty("activeWorkspace").GetProperty("id").GetInt32());
        Assert.Equal("1", monitors[1].GetProperty("activeWorkspace").GetProperty("name").GetString());
        Assert.Equal(@"\\.\DISPLAY2", monitors[1].GetProperty("activeWorkspace").GetProperty("monitor").GetString());

        var workspaces = Json(core.Execute("j/workspaces").Text).EnumerateArray().ToArray();
        var onLeft = Assert.Single(workspaces, item => item.GetProperty("monitor").GetString() == @"\\.\DISPLAY1");
        var onRight = Assert.Single(workspaces, item => item.GetProperty("monitor").GetString() == @"\\.\DISPLAY2");
        Assert.Equal(0, onLeft.GetProperty("monitorID").GetInt32());
        Assert.Equal(1, onLeft.GetProperty("windows").GetInt32());
        Assert.Equal("1", onLeft.GetProperty("name").GetString());
        Assert.Equal(1, onRight.GetProperty("monitorID").GetInt32());
        Assert.Equal(1, onRight.GetProperty("windows").GetInt32());
        Assert.Equal("1", onRight.GetProperty("name").GetString());

        var clients = Json(core.Execute("j/clients").Text).EnumerateArray().ToArray();
        Assert.False(clients[0].GetProperty("hidden").GetBoolean());
        Assert.False(clients[1].GetProperty("hidden").GetBoolean());
        Assert.Equal(1, clients[0].GetProperty("workspace").GetProperty("id").GetInt32());
        Assert.Equal(@"\\.\DISPLAY1", clients[0].GetProperty("workspace").GetProperty("monitor").GetString());
        Assert.Equal(1, clients[1].GetProperty("workspace").GetProperty("id").GetInt32());
        Assert.Equal(@"\\.\DISPLAY2", clients[1].GetProperty("workspace").GetProperty("monitor").GetString());
    }

    [Fact]
    public void UnpluggingMovesEachWorkspaceIntactOntoARemainingMonitor()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var outcome = core.Handle(new MonitorsChanged([left]));

        Assert.Single(outcome.State.Monitors);
        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("1"), outcome.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), Workspace(outcome, "1").Monitor);
        Assert.Equal(new MonitorId(0), Workspace(outcome, "2").Monitor);
        Assert.Equal(left.WorkArea, BoundsOf(outcome, 1));
        Assert.Equal(new WorkspaceId("1"), Client(outcome, 1).Workspace);
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 2).Workspace);
        Assert.Contains(new WindowId(2), outcome.Hide);
        Assert.Contains(outcome.State.Hidden, client => client.Window == new WindowId(2));

        var workspaces = Json(core.Execute("j/workspaces").Text)
            .EnumerateArray()
            .ToDictionary(item => item.GetProperty("name").GetString()!);
        Assert.Equal(@"\\.\DISPLAY1", workspaces["1"].GetProperty("monitor").GetString());
        Assert.Equal(@"\\.\DISPLAY1", workspaces["2"].GetProperty("monitor").GetString());
        Assert.Equal(0, workspaces["2"].GetProperty("monitorID").GetInt32());
        Assert.Equal(1, workspaces["1"].GetProperty("windows").GetInt32());
        Assert.Equal(1, workspaces["2"].GetProperty("windows").GetInt32());

        var monitor = Assert.Single(Json(core.Execute("j/monitors").Text).EnumerateArray());
        Assert.Equal(@"\\.\DISPLAY1", monitor.GetProperty("name").GetString());
        Assert.True(monitor.GetProperty("focused").GetBoolean());
        Assert.Equal("1", monitor.GetProperty("activeWorkspace").GetProperty("name").GetString());
    }

    [Fact]
    public void ReplugMovesTheWorkspacesBackAndShowsTheOneTheMonitorShowed()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        core.Handle(new MonitorsChanged([left]));

        var back = core.Handle(new MonitorsChanged([left, right]));

        var screen = Assert.Single(back.State.Screens, candidate => candidate.Monitor.Name == right.Name);
        Assert.Equal(WorkspaceId.FromNumber(1, screen.Id), screen.ActiveWorkspace);
        Assert.Equal(screen.Id, On(back, screen.Id.Value, "1").Monitor);
        Assert.Equal(new WorkspaceId("1"), Assert.Single(back.State.Screens, candidate => candidate.Monitor.Name == left.Name).ActiveWorkspace);
        Assert.Contains(new WindowId(2), back.Show);
        Assert.Empty(back.State.Hidden);
        Assert.Equal(right.WorkArea, BoundsOf(back, 2));
        Assert.Equal(left.WorkArea, BoundsOf(back, 1));
    }

    [Fact]
    public void UnpluggingKeepsAGroupAndAPinnedClientWithTheirWorkspace()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = float, class:^(box)$\n"));
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new Dispatch("focusmonitor", right.Name));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(new WindowAppeared(new WindowId(3), right.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(4), right.WorkArea) { Traits = WindowTraits.Normal with { Class = "box" } });
        core.Handle(new Dispatch("pin"));

        var gone = core.Handle(new MonitorsChanged([left]));

        Assert.Equal([new WindowId(2), new WindowId(3)], Assert.Single(gone.State.Groups));
        Assert.Equal(new WorkspaceId("2"), Client(gone, 2).Workspace);
        Assert.Equal(new WorkspaceId("2"), Client(gone, 3).Workspace);
        Assert.Contains(gone.State.Hidden, client => client.Window == new WindowId(2));
        Assert.Contains(gone.State.Hidden, client => client.Window == new WindowId(3));
        Assert.True(Client(gone, 4).Pinned);
        Assert.DoesNotContain(gone.State.Hidden, client => client.Window == new WindowId(4));
        Assert.DoesNotContain(gone.State.Hidden, client => client.Window == new WindowId(1));

        var back = core.Handle(new MonitorsChanged([left, right]));

        Assert.Equal([new WindowId(2), new WindowId(3)], Assert.Single(back.State.Groups));
        Assert.Contains(new WindowId(3), back.Show);
        Assert.Single(back.State.Hidden, client => client.Window == new WindowId(2));
        var returned = Assert.Single(back.State.Screens, candidate => candidate.Monitor.Name == right.Name);
        Assert.Equal(WorkspaceId.FromNumber(1, returned.Id), Client(back, 2).Workspace);
    }

    [Fact]
    public void UnpluggingTheFocusedMonitorFocusesTheWindowShownOnTheMonitorThatRemains()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new Dispatch("focusmonitor", right.Name));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var gone = core.Handle(new MonitorsChanged([left]));

        Assert.Contains(gone.State.Hidden, client => client.Window == new WindowId(2));
        Assert.Equal(new WindowId(1), gone.Focus);
        Assert.Equal("0x1", Json(core.Execute("j/activewindow").Text).GetProperty("address").GetString());
        Assert.DoesNotContain(
            Lines(gone),
            line => line.StartsWith("workspace>>", StringComparison.Ordinal) || line.StartsWith("workspacev2>>", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplugFocusesTheShownWindowWhenTheFocusedOneIsHidden()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new Dispatch("focusmonitor", right.Name));
        core.Handle(new Dispatch("workspace", "5"));
        core.Handle(new WindowAppeared(new WindowId(5), right.WorkArea));
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var gone = core.Handle(new MonitorsChanged([left]));
        // The Guest that holds the hidden window is focused, then that Guest returns with its Monitor.
        core.Handle(new Dispatch("workspace", Client(gone, 5).Workspace.Name));

        var back = core.Handle(new MonitorsChanged([left, right]));

        var returned = Assert.Single(back.State.Screens, candidate => candidate.Monitor.Name == right.Name);
        Assert.Equal(WorkspaceId.FromNumber(5, returned.Id), Client(back, 5).Workspace);
        Assert.Contains(back.State.Hidden, client => client.Window == new WindowId(5));
        Assert.Equal(new WindowId(1), back.Focus);
        Assert.Equal("0x1", Json(core.Execute("j/activewindow").Text).GetProperty("address").GetString());
    }

    [Fact]
    public void AShownSpecialWorkspaceFromAnUnpluggedMonitorReplacesTheOneTheRemainingMonitorShows()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new Dispatch("togglespecialworkspace", "notes"));
        core.Handle(new WindowAppeared(new WindowId(4), left.WorkArea));
        core.Handle(new Dispatch("focusmonitor", right.Name));
        core.Handle(new Dispatch("togglespecialworkspace", "scratch"));
        core.Handle(new WindowAppeared(new WindowId(5), right.WorkArea));

        var gone = core.Handle(new MonitorsChanged([left]));

        var only = Assert.Single(gone.State.Screens);
        Assert.Equal(new WorkspaceId("special:scratch"), only.SpecialWorkspace);
        Assert.DoesNotContain(gone.State.Hidden, client => client.Window == new WindowId(5));
        Assert.Contains(gone.State.Hidden, client => client.Window == new WindowId(4));
    }

    [Fact]
    public void FocusingAMonitorWithASpecialWorkspaceOpenFocusesItsWindow()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new Dispatch("focusmonitor", right.Name));
        core.Handle(new WindowAppeared(new WindowId(1), right.WorkArea));
        core.Handle(new Dispatch("togglespecialworkspace", "scratch"));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        core.Handle(new Dispatch("focusmonitor", left.Name));

        var back = core.Handle(new Dispatch("focusmonitor", right.Name));

        Assert.Equal(new WindowId(2), back.Focus);
    }

    [Fact]
    public void UnpluggingCarriesAShownSpecialWorkspaceAndAFullscreenClient()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new Dispatch("focusmonitor", right.Name));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        core.Handle(new Dispatch("fullscreen"));
        core.Handle(new Dispatch("togglespecialworkspace", "scratch"));
        core.Handle(new WindowAppeared(new WindowId(5), right.WorkArea));

        var gone = core.Handle(new MonitorsChanged([left]));

        var only = Assert.Single(gone.State.Screens);
        Assert.Equal(new WorkspaceId("special:scratch"), only.SpecialWorkspace);
        Assert.DoesNotContain(gone.State.Hidden, client => client.Window == new WindowId(5));
        Assert.Equal(FullscreenMode.Fullscreen, Client(gone, 2).Fullscreen);
        Assert.Contains(gone.State.Hidden, client => client.Window == new WindowId(2));

        var back = core.Handle(new MonitorsChanged([left, right]));

        // A special workspace is not a Guest, so it stays on the Monitor that took it.
        var stayed = Assert.Single(back.State.Screens, candidate => candidate.Monitor.Name == left.Name);
        var returned = Assert.Single(back.State.Screens, candidate => candidate.Monitor.Name == right.Name);
        Assert.Equal(new WorkspaceId("special:scratch"), stayed.SpecialWorkspace);
        Assert.Null(returned.SpecialWorkspace);
        Assert.Equal(WorkspaceId.FromNumber(1, returned.Id), Client(back, 2).Workspace);
        Assert.Equal(FullscreenMode.Fullscreen, Client(back, 2).Fullscreen);
        Assert.DoesNotContain(back.State.Hidden, client => client.Window == new WindowId(2));
        Assert.Equal(new Rectangle(1920, 0, 1280, 1024), Client(back, 2).Bounds);
    }

    [Fact]
    public void SwitchingWorkspaceGivesTheKeyboardToTheWindowItFocuses()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(3), right.WorkArea));
        core.Handle(new WindowFocused(new WindowId(3), FocusCause.User));
        core.Handle(new Dispatch("movetoworkspacesilent", "2"));
        core.Handle(new WindowFocused(new WindowId(2), FocusCause.User));

        var outcome = core.Handle(new Dispatch("workspace", "2"));

        Assert.Equal(outcome.State.ActiveWorkspace, Client(outcome, 3).Workspace);
        Assert.Equal(new WindowId(3), outcome.Focus);
    }

    [Fact]
    public void WorkspaceNumberNamesTheFocusedMonitorsWorkspace()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var outcome = core.Handle(new Dispatch("workspace", "2"));

        Assert.Null(outcome.Focus);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
        Assert.Equal(new MonitorId(0), Workspace(outcome, "2").Monitor);
        Assert.Equal(new WorkspaceId("1"), Client(outcome, 1).Workspace);
        Assert.Equal(new WorkspaceId("1", 1), Client(outcome, 2).Workspace);
        var rightScreen = Assert.Single(outcome.State.Screens, screen => screen.Monitor.Name == right.Name);
        Assert.Equal(WorkspaceId.FromNumber(1, rightScreen.Id), rightScreen.ActiveWorkspace);
        Assert.Contains(outcome.Hide, window => window == new WindowId(1));
        Assert.DoesNotContain(outcome.Hide, window => window == new WindowId(2));
        Assert.Equal(
            [
                @"createworkspace>>2,\\.\DISPLAY1",
                @"createworkspacev2>>2,2,\\.\DISPLAY1",
                @"workspace>>2,\\.\DISPLAY1",
                @"workspacev2>>2,2,\\.\DISPLAY1",
                "activewindow>>,",
                "activewindowv2>>",
            ],
            Lines(outcome));
    }

    [Fact]
    public void MoveToWorkspaceUsesTheFocusedMonitorsNumber()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var outcome = core.Handle(new Dispatch("movetoworkspace", "2"));

        Assert.Equal(new WindowId(1), outcome.Focus);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 1).Workspace);
        Assert.Equal(new WorkspaceId("1", 1), Client(outcome, 2).Workspace);
        Assert.Equal(left.WorkArea, BoundsOf(outcome, 1));
        Assert.Equal(
            [
                @"createworkspace>>2,\\.\DISPLAY1",
                @"createworkspacev2>>2,2,\\.\DISPLAY1",
                @"destroyworkspace>>1,\\.\DISPLAY1",
                @"destroyworkspacev2>>1,1,\\.\DISPLAY1",
                @"movewindow>>1,2,\\.\DISPLAY1",
                @"movewindowv2>>1,2,2,\\.\DISPLAY1",
                @"workspace>>2,\\.\DISPLAY1",
                @"workspacev2>>2,2,\\.\DISPLAY1",
            ],
            Lines(outcome));
    }

    [Fact]
    public void MoveToWorkspaceSilentStaysOnTheFocusedMonitor()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var outcome = core.Handle(new Dispatch("movetoworkspacesilent", "2"));

        Assert.Null(outcome.Focus);
        Assert.Equal(new WorkspaceId("1"), outcome.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("1"), Workspace(outcome, "1").Id);
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 1).Workspace);
        Assert.Equal(new WorkspaceId("1", 1), Client(outcome, 2).Workspace);
        Assert.Contains(outcome.State.Hidden, client => client.Window == new WindowId(1));
        Assert.DoesNotContain(outcome.State.Hidden, client => client.Window == new WindowId(2));
        Assert.Equal(new WorkspaceId("1", 1), Assert.Single(outcome.State.Screens, screen => screen.Monitor.Name == right.Name).ActiveWorkspace);
        Assert.Equal(
            [
                @"createworkspace>>2,\\.\DISPLAY1",
                @"createworkspacev2>>2,2,\\.\DISPLAY1",
                @"movewindow>>1,2,\\.\DISPLAY1",
                @"movewindowv2>>1,2,2,\\.\DISPLAY1",
                "activewindow>>,",
                "activewindowv2>>",
            ],
            Lines(outcome));
    }

    private static Workspace Workspace(Outcome outcome, string name) =>
        Assert.Single(outcome.State.Workspaces, workspace => workspace.Id == new WorkspaceId(name));

    private static Workspace On(Outcome outcome, int monitor, string name) =>
        Assert.Single(outcome.State.Workspaces, workspace => workspace.Id == new WorkspaceId(name, monitor));

    private static Decoration Border(Outcome outcome, long window) =>
        Assert.Single(outcome.Decorations, decoration => decoration.Client == new ClientId(window));

    private static IReadOnlyList<string> Lines(Outcome outcome) =>
        CommandProtocol.FormatStream(outcome.Events, outcome.State);
}
