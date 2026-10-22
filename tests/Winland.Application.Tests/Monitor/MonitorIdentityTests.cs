namespace Winland.Application.Tests;

public class MonitorIdentityTests
{
    [Fact]
    public void WorkspacesReturnWhenWindowsRenamesTheMonitor()
    {
        var core = new WindowManager();
        var left = Panel(@"\\.\DISPLAY1", "DEL", "U2723QE", "LEFT", 0);
        var right = Panel(@"\\.\DISPLAY2", "SAM", "S27", "RIGHT", 1920);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        core.Handle(new MonitorsChanged([left]));

        var renamed = Panel(@"\\.\DISPLAY4", "SAM", "S27", "RIGHT", 1920);
        var back = core.Handle(new MonitorsChanged([left, renamed]));

        var screen = Assert.Single(back.State.Screens, candidate => candidate.Monitor.Name == @"\\.\DISPLAY4");
        Assert.Equal("SAM S27 RIGHT", screen.Monitor.Description);
        Assert.Equal(WorkspaceId.FromNumber(1, screen.Id), screen.ActiveWorkspace);
        Assert.Equal(screen.Id, Workspace(back, WorkspaceId.FromNumber(1, screen.Id)).Monitor);
        Assert.Equal(
            WorkspaceId.FromNumber(1, screen.Id),
            Assert.Single(back.State.Clients, client => client.Window == new WindowId(2)).Workspace);
        Assert.Equal(
            new WorkspaceId("1"),
            Assert.Single(back.State.Screens, candidate => candidate.Monitor.Name == left.Name).ActiveWorkspace);
    }

    [Fact]
    public void IdenticalMonitorsAreNumberedInTheOrderTheyWereFirstSeen()
    {
        var core = new WindowManager();
        var first = Panel(@"\\.\DISPLAY1", "DEL", "U2723QE", "SAME", 0);
        core.Handle(new MonitorsChanged([first]));
        var second = Panel(@"\\.\DISPLAY2", "DEL", "U2723QE", "SAME", 1920);
        var outcome = core.Handle(new MonitorsChanged([second, first]));

        Assert.Equal("DEL U2723QE SAME", Description(outcome, @"\\.\DISPLAY1"));
        Assert.Equal("DEL U2723QE SAME #2", Description(outcome, @"\\.\DISPLAY2"));

        var third = Panel(@"\\.\DISPLAY3", "DEL", "U2723QE", "SAME", 3840);
        var three = core.Handle(new MonitorsChanged([third, second, first]));
        Assert.Equal("DEL U2723QE SAME", Description(three, @"\\.\DISPLAY1"));
        Assert.Equal("DEL U2723QE SAME #2", Description(three, @"\\.\DISPLAY2"));
        Assert.Equal("DEL U2723QE SAME #3", Description(three, @"\\.\DISPLAY3"));
    }

    [Fact]
    public void ARepluggedIdenticalMonitorKeepsItsNumberAndItsWorkspaces()
    {
        var core = new WindowManager();
        var first = Panel(@"\\.\DISPLAY1", "DEL", "U2723QE", "SAME", 0);
        var second = Panel(@"\\.\DISPLAY2", "DEL", "U2723QE", "SAME", 1920);
        core.Handle(new MonitorsChanged([first, second]));
        core.Handle(new WindowAppeared(new WindowId(1), first.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), second.WorkArea));
        core.Handle(new MonitorsChanged([first]));

        var renamed = Panel(@"\\.\DISPLAY9", "DEL", "U2723QE", "SAME", 1920);
        var back = core.Handle(new MonitorsChanged([renamed, first]));

        Assert.Equal("DEL U2723QE SAME #2", Description(back, @"\\.\DISPLAY9"));
        var screen = Assert.Single(back.State.Screens, candidate => candidate.Monitor.Name == @"\\.\DISPLAY9");
        Assert.Equal(WorkspaceId.FromNumber(1, screen.Id), screen.ActiveWorkspace);
        Assert.Equal(screen.Id, Workspace(back, WorkspaceId.FromNumber(1, screen.Id)).Monitor);
        Assert.Equal(
            WorkspaceId.FromNumber(1, screen.Id),
            Assert.Single(back.State.Clients, client => client.Window == new WindowId(2)).Workspace);
        Assert.Equal(
            new WorkspaceId("1"),
            Assert.Single(back.State.Screens, candidate => candidate.Monitor.Name == first.Name).ActiveWorkspace);
    }

    [Fact]
    public void DescriptionMatchingReadsTheIdentity()
    {
        var core = new WindowManager();
        var dell = Panel(@"\\.\DISPLAY1", "DEL", "U2723QE", "ABC", 0);
        var samsung = Panel(@"\\.\DISPLAY2", "SAM", "S27", "XYZ", 1920);
        core.Handle(new MonitorsChanged([dell, samsung]));
        core.Handle(new WindowAppeared(new WindowId(1), dell.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), samsung.WorkArea));

        var focused = core.Handle(new Dispatch("focusmonitor", "desc:SAM S27 XYZ"));
        Assert.Equal(new MonitorId(1), focused.State.ActiveMonitor);

        var numbered = core.Handle(new Dispatch("focusmonitor", "desc:friendly"));
        Assert.Equal("error: monitor not found", numbered.Error);

        var duplicate = Panel(@"\\.\DISPLAY3", "SAM", "S27", "XYZ", 3840);
        var both = core.Handle(new MonitorsChanged([dell, samsung, duplicate]));
        var byNumber = core.Handle(new Dispatch("focusmonitor", "desc:SAM S27 XYZ #2"));
        Assert.Equal(
            Assert.Single(both.State.Screens, screen => screen.Monitor.Name == @"\\.\DISPLAY3").Id,
            byNumber.State.ActiveMonitor);
    }

    [Fact]
    public void AReservedAreaSelectsTheMonitorByItsIdentity()
    {
        var core = new WindowManager(ConfigParser.Parse("""
            monitor = desc:SAM S27 XYZ, addreserved, 10, 0, 0, 0
            """));
        var dell = Panel(@"\\.\DISPLAY1", "DEL", "U2723QE", "ABC", 0);
        var samsung = Panel(@"\\.\DISPLAY2", "SAM", "S27", "XYZ", 1920);

        var outcome = core.Handle(new MonitorsChanged([dell, samsung]));

        Assert.Equal(new Rectangle(1920, 10, 1920, 1070), WorkArea(outcome, @"\\.\DISPLAY2"));
        Assert.Equal(dell.WorkArea, WorkArea(outcome, @"\\.\DISPLAY1"));
    }

    [Fact]
    public void MakeAloneDoesNotNumberMonitors()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);
        var first = new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, area, Make: "DEL");
        var second = new Monitor(
            @"\\.\DISPLAY2",
            "Dell",
            1920,
            0,
            1920,
            1080,
            1,
            true,
            new Rectangle(1920, 0, 1920, 1080),
            Make: "DEL");

        var outcome = core.Handle(new MonitorsChanged([first, second]));

        Assert.Equal("Dell", Description(outcome, @"\\.\DISPLAY1"));
        Assert.Equal("Dell", Description(outcome, @"\\.\DISPLAY2"));
    }

    [Fact]
    public void AMonitorWithoutAModelOrSerialComesBackByItsWindowsName()
    {
        var core = new WindowManager();
        var left = new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1080));
        var right = new Monitor(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1, true, new Rectangle(1920, 0, 1280, 1024));
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        core.Handle(new MonitorsChanged([left]));

        var renamed = new Monitor(@"\\.\DISPLAY4", "Samsung", 1920, 0, 1280, 1024, 1, true, new Rectangle(1920, 0, 1280, 1024));
        var back = core.Handle(new MonitorsChanged([left, renamed]));

        Assert.Equal(new WorkspaceId("2"), Assert.Single(back.State.Clients, client => client.Window == new WindowId(2)).Workspace);
        Assert.Equal(new MonitorId(0), Workspace(back, new WorkspaceId("2")).Monitor);
        Assert.Equal("Samsung", Description(back, @"\\.\DISPLAY4"));
    }

    private static Monitor Panel(string name, string make, string model, string serial, int x)
    {
        var area = new Rectangle(x, 0, 1920, 1080);
        return new Monitor(name, "friendly", x, 0, 1920, 1080, 1, true, area, Make: make, Model: model, Serial: serial);
    }

    private static string Description(Outcome outcome, string name) =>
        Assert.Single(outcome.State.Monitors, monitor => monitor.Name == name).Description;

    private static Rectangle WorkArea(Outcome outcome, string name) =>
        Assert.Single(outcome.State.Monitors, monitor => monitor.Name == name).WorkArea;

    private static Workspace Workspace(Outcome outcome, WorkspaceId id) =>
        Assert.Single(outcome.State.Workspaces, workspace => workspace.Id == id);
}
