namespace Winland.Application.Tests;

public class PerMonitorWorkspaceTests
{
    [Fact]
    public void EachMonitorHasItsOwnWorkspace3()
    {
        var core = new WindowManager();
        var left = Screen(@"\\.\DISPLAY1", "Dell", "P2415Q", "A", 0);
        var right = Screen(@"\\.\DISPLAY2", "Samsung", "S27", "B", 1920);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new Dispatch("workspace", "3"));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new Dispatch("focusmonitor", right.Name));
        core.Handle(new Dispatch("workspace", "3"));
        var opened = core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var rightScreen = Assert.Single(opened.State.Screens, screen => screen.Monitor.Name == right.Name);
        Assert.Equal(new WorkspaceId("3"), Client(opened, 1).Workspace);
        Assert.Equal(WorkspaceId.FromNumber(3, rightScreen.Id), Client(opened, 2).Workspace);
        Assert.Equal(WorkspaceId.FromNumber(3, rightScreen.Id), rightScreen.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("3"), Assert.Single(opened.State.Screens, screen => screen.Monitor.Name == left.Name).ActiveWorkspace);
    }

    [Fact]
    public void UnplugNumbersGuestsAfterTheHostsOwn()
    {
        var core = new WindowManager();
        var left = Screen(@"\\.\DISPLAY1", "Dell", "P2415Q", "A", 0);
        var right = Screen(@"\\.\DISPLAY2", "Samsung", "S27", "B", 1920);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new Dispatch("focusmonitor", right.Name));
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        core.Handle(new Dispatch("workspace", "4"));
        core.Handle(new WindowAppeared(new WindowId(4), right.WorkArea));

        var gone = core.Handle(new MonitorsChanged([left]));

        Assert.Equal(new WorkspaceId("1"), Client(gone, 1).Workspace);
        Assert.Equal(new WorkspaceId("2"), Client(gone, 2).Workspace);
        Assert.Equal(new WorkspaceId("3"), Client(gone, 4).Workspace);
        Assert.Equal(new MonitorId(0), WorkspaceOf(gone, new WorkspaceId("2")).Monitor);
        Assert.Equal(new MonitorId(0), WorkspaceOf(gone, new WorkspaceId("3")).Monitor);
    }

    [Fact]
    public void UnplugNumbersGuestsAbove10When1To10AreFull()
    {
        var rules = string.Join('\n', Enumerable.Range(1, 10).Select(number => $"workspace = {number}, persistent:true"));
        var core = new WindowManager(ConfigParser.Parse(rules + "\n"));
        var left = Screen(@"\\.\DISPLAY1", "Dell", "P2415Q", "A", 0);
        var right = Screen(@"\\.\DISPLAY2", "Samsung", "S27", "B", 1920);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new Dispatch("focusmonitor", right.Name));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        core.Handle(new Dispatch("workspace", "4"));
        core.Handle(new WindowAppeared(new WindowId(4), right.WorkArea));

        var gone = core.Handle(new MonitorsChanged([left]));

        Assert.Equal(new WorkspaceId("11"), Client(gone, 2).Workspace);
        Assert.Equal(new WorkspaceId("12"), Client(gone, 4).Workspace);
        Assert.Contains(gone.State.Workspaces, workspace => workspace.Id == new WorkspaceId("10"));
        Assert.DoesNotContain(gone.State.Workspaces, workspace => workspace.Id == new WorkspaceId("4", 1));
    }

    [Fact]
    public void ReplugReturnsGuestsUnderTheirOldNumbersAndShowsThePreviousWorkspace()
    {
        var core = new WindowManager();
        var left = Screen(@"\\.\DISPLAY1", "Dell", "P2415Q", "A", 0);
        var right = Screen(@"\\.\DISPLAY2", "Samsung", "S27", "B", 1920);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new Dispatch("focusmonitor", right.Name));
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        core.Handle(new Dispatch("workspace", "4"));
        core.Handle(new WindowAppeared(new WindowId(4), right.WorkArea));
        core.Handle(new MonitorsChanged([left]));

        var back = core.Handle(new MonitorsChanged([left, right]));

        var returned = Assert.Single(back.State.Screens, screen => screen.Monitor.Name == right.Name);
        Assert.Equal(WorkspaceId.FromNumber(4, returned.Id), returned.ActiveWorkspace);
        Assert.Equal(WorkspaceId.FromNumber(2, returned.Id), Client(back, 2).Workspace);
        Assert.Equal(WorkspaceId.FromNumber(4, returned.Id), Client(back, 4).Workspace);
        Assert.Equal(new WorkspaceId("1"), Assert.Single(back.State.Screens, screen => screen.Monitor.Name == left.Name).ActiveWorkspace);
    }

    [Fact]
    public void MovingAWorkspaceKeepsItsNumberWhenTheTargetIsFree()
    {
        var core = new WindowManager();
        var left = Screen(@"\\.\DISPLAY1", "Dell", "P2415Q", "A", 0);
        var right = Screen(@"\\.\DISPLAY2", "Samsung", "S27", "B", 1920);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new Dispatch("workspace", "3"));
        core.Handle(new WindowAppeared(new WindowId(3), left.WorkArea));

        var moved = core.Handle(new Dispatch("movecurrentworkspacetomonitor", @"\\.\DISPLAY2"));

        var destination = Assert.Single(moved.State.Screens, screen => screen.Monitor.Name == right.Name);
        Assert.Equal(WorkspaceId.FromNumber(3, destination.Id), Client(moved, 3).Workspace);
        Assert.Equal(destination.Id, WorkspaceOf(moved, WorkspaceId.FromNumber(3, destination.Id)).Monitor);
        Assert.DoesNotContain(moved.State.Workspaces, workspace => workspace.Id == new WorkspaceId("3"));
    }

    [Fact]
    public void MovingAWorkspaceTakesTheFirstFreeNumberWhenItsOwnIsTaken()
    {
        var core = new WindowManager();
        var left = Screen(@"\\.\DISPLAY1", "Dell", "P2415Q", "A", 0);
        var right = Screen(@"\\.\DISPLAY2", "Samsung", "S27", "B", 1920);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new Dispatch("workspace", "3"));
        core.Handle(new WindowAppeared(new WindowId(3), left.WorkArea));
        core.Handle(new Dispatch("focusmonitor", right.Name));
        core.Handle(new Dispatch("workspace", "3"));
        core.Handle(new WindowAppeared(new WindowId(8), right.WorkArea));
        core.Handle(new Dispatch("focusmonitor", left.Name));

        var moved = core.Handle(new Dispatch("movecurrentworkspacetomonitor", @"\\.\DISPLAY2"));

        var destination = Assert.Single(moved.State.Screens, screen => screen.Monitor.Name == right.Name);
        Assert.Equal(WorkspaceId.FromNumber(1, destination.Id), Client(moved, 3).Workspace);
        Assert.Equal(WorkspaceId.FromNumber(3, destination.Id), Client(moved, 8).Workspace);
    }

    private static Monitor Screen(string name, string make, string model, string serial, int x) =>
        new(name, "", x, 0, 1920, 1080, 1, true, new Rectangle(x, 0, 1920, 1080), make, model, serial);

    private static Workspace WorkspaceOf(Outcome outcome, WorkspaceId id) =>
        Assert.Single(outcome.State.Workspaces, workspace => workspace.Id == id);
}
