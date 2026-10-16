using System.Text.Json;
using Winland;
using Monitor = Winland.Monitor;

namespace Winland.Tests;

public class MonitorWorkspaceTests
{
    [Fact]
    public void EachEnabledMonitorShowsItsOwnWorkspace()
    {
        var core = new Core();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1.5);
        core.Handle(new MonitorsChanged([left, right]));

        var first = core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        var second = core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        Assert.Equal(left.WorkArea, BoundsOf(first, 1));
        Assert.Equal(right.WorkArea, BoundsOf(second, 2));
        Assert.Equal(new WorkspaceId("1"), Client(second, 1).Workspace);
        Assert.Equal(new WorkspaceId("2"), Client(second, 2).Workspace);
        Assert.Equal(new MonitorId(0), Workspace(second, "1").Monitor);
        Assert.Equal(new MonitorId(1), Workspace(second, "2").Monitor);
        Assert.Equal(new MonitorId(0), second.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("1"), second.State.ActiveWorkspace);
        Assert.DoesNotContain(second.State.Hidden, client => client.Window == new WindowId(1));
        Assert.DoesNotContain(second.State.Hidden, client => client.Window == new WindowId(2));

        var monitors = Json(core.Execute("monitors").Text).EnumerateArray().ToArray();
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
        Assert.Equal(2, monitors[1].GetProperty("activeWorkspace").GetProperty("id").GetInt32());
        Assert.Equal("2", monitors[1].GetProperty("activeWorkspace").GetProperty("name").GetString());

        var workspaces = Json(core.Execute("workspaces").Text)
            .EnumerateArray()
            .ToDictionary(item => item.GetProperty("name").GetString()!);
        Assert.Equal(0, workspaces["1"].GetProperty("monitorID").GetInt32());
        Assert.Equal(@"\\.\DISPLAY1", workspaces["1"].GetProperty("monitor").GetString());
        Assert.Equal(1, workspaces["1"].GetProperty("windows").GetInt32());
        Assert.Equal(1, workspaces["2"].GetProperty("monitorID").GetInt32());
        Assert.Equal(@"\\.\DISPLAY2", workspaces["2"].GetProperty("monitor").GetString());
        Assert.Equal(1, workspaces["2"].GetProperty("windows").GetInt32());

        var clients = Json(core.Execute("clients").Text).EnumerateArray().ToArray();
        Assert.False(clients[0].GetProperty("hidden").GetBoolean());
        Assert.False(clients[1].GetProperty("hidden").GetBoolean());
        Assert.Equal(1, clients[0].GetProperty("workspace").GetProperty("id").GetInt32());
        Assert.Equal(2, clients[1].GetProperty("workspace").GetProperty("id").GetInt32());
    }

    [Fact]
    public void UnpluggingMovesEachWorkspaceIntactOntoARemainingMonitor()
    {
        var core = new Core();
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

        var workspaces = Json(core.Execute("workspaces").Text)
            .EnumerateArray()
            .ToDictionary(item => item.GetProperty("name").GetString()!);
        Assert.Equal(@"\\.\DISPLAY1", workspaces["1"].GetProperty("monitor").GetString());
        Assert.Equal(@"\\.\DISPLAY1", workspaces["2"].GetProperty("monitor").GetString());
        Assert.Equal(0, workspaces["2"].GetProperty("monitorID").GetInt32());
        Assert.Equal(1, workspaces["1"].GetProperty("windows").GetInt32());
        Assert.Equal(1, workspaces["2"].GetProperty("windows").GetInt32());

        var monitor = Assert.Single(Json(core.Execute("monitors").Text).EnumerateArray());
        Assert.Equal(@"\\.\DISPLAY1", monitor.GetProperty("name").GetString());
        Assert.True(monitor.GetProperty("focused").GetBoolean());
        Assert.Equal("1", monitor.GetProperty("activeWorkspace").GetProperty("name").GetString());
    }

    [Fact]
    public void ReplugMovesTheWorkspacesBackAndShowsTheOneTheMonitorShowed()
    {
        var core = new Core();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        core.Handle(new MonitorsChanged([left]));

        var back = core.Handle(new MonitorsChanged([left, right]));

        var screen = Assert.Single(back.State.Screens, candidate => candidate.Monitor.Name == right.Name);
        Assert.Equal(new WorkspaceId("2"), screen.ActiveWorkspace);
        Assert.Equal(screen.Id, Workspace(back, "2").Monitor);
        Assert.Equal(new WorkspaceId("1"), Assert.Single(back.State.Screens, candidate => candidate.Monitor.Name == left.Name).ActiveWorkspace);
        Assert.Contains(new WindowId(2), back.Show);
        Assert.Empty(back.State.Hidden);
        Assert.Equal(right.WorkArea, BoundsOf(back, 2));
        Assert.Equal(left.WorkArea, BoundsOf(back, 1));
    }

    [Fact]
    public void UnpluggingKeepsAGroupAndAPinnedClientWithTheirWorkspace()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = float, class:^(box)$\n"));
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
        Assert.Equal(
            Assert.Single(back.State.Screens, candidate => candidate.Monitor.Name == right.Name).Id,
            Workspace(back, "2").Monitor);
    }

    [Fact]
    public void UnpluggingTheFocusedMonitorFocusesTheWindowShownOnTheMonitorThatRemains()
    {
        var core = new Core();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new Dispatch("focusmonitor", right.Name));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var gone = core.Handle(new MonitorsChanged([left]));

        Assert.Contains(gone.State.Hidden, client => client.Window == new WindowId(2));
        Assert.Equal(new WindowId(1), gone.Focus);
        Assert.Equal("0x1", Json(core.Execute("activewindow").Text).GetProperty("address").GetString());
    }

    [Fact]
    public void ReplugFocusesTheShownWindowWhenTheFocusedOneIsHidden()
    {
        var core = new Core();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new Dispatch("focusmonitor", right.Name));
        core.Handle(new Dispatch("workspace", "5"));
        core.Handle(new WindowAppeared(new WindowId(5), right.WorkArea));
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        core.Handle(new MonitorsChanged([left]));
        core.Handle(new Dispatch("workspace", "5"));

        var back = core.Handle(new MonitorsChanged([left, right]));

        Assert.Contains(back.State.Hidden, client => client.Window == new WindowId(5));
        Assert.Equal(new WindowId(1), back.Focus);
        Assert.Equal("0x1", Json(core.Execute("activewindow").Text).GetProperty("address").GetString());
    }

    [Fact]
    public void AShownSpecialWorkspaceFromAnUnpluggedMonitorReplacesTheOneTheRemainingMonitorShows()
    {
        var core = new Core();
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
    public void UnpluggingCarriesAShownSpecialWorkspaceAndAFullscreenClient()
    {
        var core = new Core();
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

        var returned = Assert.Single(back.State.Screens, candidate => candidate.Monitor.Name == right.Name);
        Assert.Equal(new WorkspaceId("special:scratch"), returned.SpecialWorkspace);
        Assert.Null(Assert.Single(back.State.Screens, candidate => candidate.Monitor.Name == left.Name).SpecialWorkspace);
        Assert.Equal(FullscreenMode.Fullscreen, Client(back, 2).Fullscreen);
        Assert.DoesNotContain(back.State.Hidden, client => client.Window == new WindowId(2));
        Assert.Equal(new Rectangle(1920, 0, 1280, 1024), Client(back, 2).Bounds);
    }

    [Fact]
    public void WorkspaceOnAnotherMonitorFocusesThatMonitor()
    {
        var core = new Core();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var outcome = core.Handle(new Dispatch("workspace", "2"));

        Assert.Equal("workspace", outcome.Dispatcher);
        Assert.Equal(new WindowId(2), outcome.Focus);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(1), outcome.State.ActiveMonitor);
        Assert.Equal(new MonitorId(0), Workspace(outcome, "1").Monitor);
        Assert.Equal(new MonitorId(1), Workspace(outcome, "2").Monitor);
        Assert.DoesNotContain(outcome.State.Workspaces, workspace => workspace.Id == new WorkspaceId("3"));
        Assert.Equal(new WorkspaceId("1"), Client(outcome, 1).Workspace);
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 2).Workspace);
        Assert.Empty(outcome.Placements);
        Assert.Empty(outcome.Show);
        Assert.Empty(outcome.Hide);
        Assert.Empty(outcome.State.Hidden);
        Assert.Equal(new BorderColor(0xFF, 0x44, 0x44, 0x44), Border(outcome, 1).Border);
        Assert.Equal(new BorderColor(0xFF, 0xFF, 0xFF, 0xFF), Border(outcome, 2).Border);
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

        var monitors = Json(core.Execute("monitors").Text).EnumerateArray().ToArray();
        Assert.False(monitors[0].GetProperty("focused").GetBoolean());
        Assert.Equal("1", monitors[0].GetProperty("activeWorkspace").GetProperty("name").GetString());
        Assert.True(monitors[1].GetProperty("focused").GetBoolean());
        Assert.Equal(@"\\.\DISPLAY2", monitors[1].GetProperty("name").GetString());
        Assert.Equal("2", monitors[1].GetProperty("activeWorkspace").GetProperty("name").GetString());

        var opened = core.Handle(new WindowAppeared(new WindowId(3), left.WorkArea));
        Assert.Equal(new WorkspaceId("1"), Client(opened, 3).Workspace);
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(opened, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(opened, 3));
        Assert.DoesNotContain(opened.Placements, placement => placement.Client == Client(opened, 2).Id);
        Assert.Equal(new MonitorId(1), opened.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("2"), opened.State.ActiveWorkspace);
    }

    [Fact]
    public void FocusWorkspaceOnCurrentMonitorPullsItFromTheOtherMonitor()
    {
        var core = new Core();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var outcome = core.Handle(new Dispatch("focusworkspaceoncurrentmonitor", "2"));

        Assert.Equal("focusworkspaceoncurrentmonitor", outcome.Dispatcher);
        Assert.Null(outcome.Focus);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
        Assert.Equal(new MonitorId(0), Workspace(outcome, "2").Monitor);
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 2).Workspace);
        Assert.Equal(left.WorkArea, BoundsOf(outcome, 2));
        Assert.Equal([new WindowId(1)], outcome.Hide);
        Assert.Contains(outcome.Show, window => window == new WindowId(2));
        Assert.Contains(outcome.State.Hidden, client => client.Window == new WindowId(1) && client.Workspace == new WorkspaceId("1"));
        Assert.DoesNotContain(outcome.State.Hidden, client => client.Window == new WindowId(2));
        Assert.Equal(new MonitorId(1), Workspace(outcome, "3").Monitor);
        Assert.Equal(new BorderColor(0xFF, 0x44, 0x44, 0x44), Border(outcome, 1).Border);
        Assert.Equal(new BorderColor(0xFF, 0xFF, 0xFF, 0xFF), Border(outcome, 2).Border);
        Assert.Equal(
            [
                "createworkspace>>3",
                "createworkspacev2>>3,3",
                @"moveworkspace>>2,\\.\DISPLAY1",
                @"moveworkspacev2>>2,2,\\.\DISPLAY1",
                "workspace>>2",
                "workspacev2>>2,2",
                "activewindow>>,",
                "activewindowv2>>2",
            ],
            Lines(outcome));

        var monitors = Json(core.Execute("monitors").Text).EnumerateArray().ToArray();
        Assert.True(monitors[0].GetProperty("focused").GetBoolean());
        Assert.Equal("2", monitors[0].GetProperty("activeWorkspace").GetProperty("name").GetString());
        Assert.False(monitors[1].GetProperty("focused").GetBoolean());
        Assert.Equal(@"\\.\DISPLAY2", monitors[1].GetProperty("name").GetString());
        Assert.Equal(1280, monitors[1].GetProperty("width").GetInt32());
        Assert.Equal("3", monitors[1].GetProperty("activeWorkspace").GetProperty("name").GetString());

        var stayed = core.Handle(new WindowAppeared(new WindowId(3), right.WorkArea));
        Assert.Equal(new WorkspaceId("3"), Client(stayed, 3).Workspace);
        Assert.Equal(right.WorkArea, BoundsOf(stayed, 3));
        Assert.Equal(new WorkspaceId("2"), stayed.State.ActiveWorkspace);
        Assert.Contains(stayed.State.Hidden, client => client.Window == new WindowId(1));
        Assert.DoesNotContain(stayed.State.Hidden, client => client.Window == new WindowId(2));
        Assert.DoesNotContain(stayed.Placements, placement => placement.Client == Client(stayed, 2).Id);
    }

    [Fact]
    public void MoveToWorkspaceFollowsAWorkspaceShownOnAnotherMonitor()
    {
        var core = new Core();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var outcome = core.Handle(new Dispatch("movetoworkspace", "2"));

        Assert.Equal("movetoworkspace", outcome.Dispatcher);
        Assert.Equal(new WindowId(1), outcome.Focus);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(1), outcome.State.ActiveMonitor);
        Assert.Equal(new MonitorId(0), Workspace(outcome, "1").Monitor);
        Assert.Equal(new MonitorId(1), Workspace(outcome, "2").Monitor);
        Assert.DoesNotContain(outcome.State.Workspaces, workspace => workspace.Id == new WorkspaceId("3"));
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 1).Workspace);
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 2).Workspace);
        Assert.Equal(new Rectangle(1920, 0, 640, 1024), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(2560, 0, 640, 1024), BoundsOf(outcome, 1));
        Assert.Empty(outcome.Show);
        Assert.Empty(outcome.Hide);
        Assert.Empty(outcome.State.Hidden);
        Assert.Equal(new BorderColor(0xFF, 0xFF, 0xFF, 0xFF), Border(outcome, 1).Border);
        Assert.Equal(new BorderColor(0xFF, 0x44, 0x44, 0x44), Border(outcome, 2).Border);
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

        var opened = core.Handle(new WindowAppeared(new WindowId(3), left.WorkArea));
        Assert.Equal(new WorkspaceId("1"), Client(opened, 3).Workspace);
        Assert.Equal(left.WorkArea, BoundsOf(opened, 3));
        Assert.Equal(new MonitorId(1), opened.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("2"), opened.State.ActiveWorkspace);
    }

    [Fact]
    public void MoveToWorkspaceSilentLeavesAWorkspaceShownOnAnotherMonitor()
    {
        var core = new Core();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var outcome = core.Handle(new Dispatch("movetoworkspacesilent", "2"));

        Assert.Equal("movetoworkspacesilent", outcome.Dispatcher);
        Assert.Null(outcome.Focus);
        Assert.Equal(new WorkspaceId("1"), outcome.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), outcome.State.ActiveMonitor);
        Assert.Equal(new MonitorId(0), Workspace(outcome, "1").Monitor);
        Assert.Equal(new MonitorId(1), Workspace(outcome, "2").Monitor);
        Assert.DoesNotContain(outcome.State.Workspaces, workspace => workspace.Id == new WorkspaceId("3"));
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 1).Workspace);
        Assert.Equal(new WorkspaceId("2"), Client(outcome, 2).Workspace);
        Assert.Equal(new Rectangle(1920, 0, 640, 1024), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(2560, 0, 640, 1024), BoundsOf(outcome, 1));
        Assert.Empty(outcome.Show);
        Assert.Empty(outcome.Hide);
        Assert.Empty(outcome.State.Hidden);
        Assert.Equal(new BorderColor(0xFF, 0x44, 0x44, 0x44), Border(outcome, 1).Border);
        Assert.Equal(new BorderColor(0xFF, 0x44, 0x44, 0x44), Border(outcome, 2).Border);
        Assert.Equal(
            ["movewindow>>1,2", "movewindowv2>>1,2,2", "activewindow>>,", "activewindowv2>>"],
            Lines(outcome));

        var monitors = Json(core.Execute("monitors").Text).EnumerateArray().ToArray();
        Assert.True(monitors[0].GetProperty("focused").GetBoolean());
        Assert.Equal("1", monitors[0].GetProperty("activeWorkspace").GetProperty("name").GetString());
        Assert.False(monitors[1].GetProperty("focused").GetBoolean());
        Assert.Equal("2", monitors[1].GetProperty("activeWorkspace").GetProperty("name").GetString());

        var opened = core.Handle(new WindowAppeared(new WindowId(3), left.WorkArea));
        Assert.Equal(new WorkspaceId("1"), Client(opened, 3).Workspace);
        Assert.Equal(left.WorkArea, BoundsOf(opened, 3));
        Assert.Equal(new MonitorId(0), opened.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("1"), opened.State.ActiveWorkspace);
    }

    private static Monitor Display(
        string name,
        string description,
        int x,
        int y,
        int width,
        int height,
        double scale) =>
        new(name, description, x, y, width, height, scale, true, new Rectangle(x, y, width, height));

    private static Workspace Workspace(Outcome outcome, string name) =>
        Assert.Single(outcome.State.Workspaces, workspace => workspace.Id == new WorkspaceId(name));

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Client(outcome, window);
        return Assert.Single(outcome.Placements, placement => placement.Client == client.Id).Bounds;
    }

    private static Decoration Border(Outcome outcome, long window) =>
        Assert.Single(outcome.Decorations, decoration => decoration.Client == new ClientId(window));

    private static IReadOnlyList<string> Lines(Outcome outcome) =>
        CommandProtocol.FormatStream(outcome.Events, outcome.State);

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
