namespace Winland.Application.Tests;

public class WorkspaceSelectorTests
{
    [Fact]
    public void MonitorStepAndEveryStepCycleTheFocusedMonitorsWorkspaces()
    {
        var core = TwoMonitors(out var left, out var right);
        Appear(core, 1, left);
        Appear(core, 2, right);
        core.Handle(new Dispatch("workspace", "3"));
        Appear(core, 3, left);
        core.Handle(new Dispatch("workspace", "1"));

        // e walks the same Workspaces as m: the ones on the focused Monitor. The other Monitor's Workspace 1 stays put.
        var monitorNext = core.Handle(new Dispatch("workspace", "m+1"));
        Assert.Equal(new WorkspaceId("3"), monitorNext.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), monitorNext.State.ActiveMonitor);

        var monitorBack = core.Handle(new Dispatch("workspace", "m-1"));
        Assert.Equal(new WorkspaceId("1"), monitorBack.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), monitorBack.State.ActiveMonitor);

        var everyNext = core.Handle(new Dispatch("workspace", "e+1"));
        Assert.Equal(new WorkspaceId("3"), everyNext.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), everyNext.State.ActiveMonitor);

        var everyBack = core.Handle(new Dispatch("workspace", "e-1"));
        Assert.Equal(new WorkspaceId("1"), everyBack.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), everyBack.State.ActiveMonitor);

        Assert.Equal(new WorkspaceId("1"), core.Handle(new Dispatch("workspace", "m~1")).State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("3"), core.Handle(new Dispatch("workspace", "m~2")).State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("3"), core.Handle(new Dispatch("workspace", "m~9")).State.ActiveWorkspace);
        var secondOpen = core.Handle(new Dispatch("workspace", "e~2"));
        Assert.Equal(new WorkspaceId("3"), secondOpen.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), secondOpen.State.ActiveMonitor);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), Client(secondOpen, 2).Workspace);
    }

    [Fact]
    public void RelativeStepWalksThisMonitorsNumbers()
    {
        var core = TwoMonitors(out var left, out var right);
        Appear(core, 1, left);
        Appear(core, 2, right);

        // r+1 is the next number on this Monitor. The same number on the other Monitor does not count.
        var stepped = core.Handle(new Dispatch("workspace", "r+1"));
        Assert.Equal(new WorkspaceId("2"), stepped.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), stepped.State.ActiveMonitor);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), Client(stepped, 2).Workspace);

        core.Handle(new Dispatch("workspace", "1"));
        Assert.Equal(new WorkspaceId("1"), core.Handle(new Dispatch("workspace", "r~1")).State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("2"), core.Handle(new Dispatch("workspace", "r~2")).State.ActiveWorkspace);

        var back = core.Handle(new Dispatch("workspace", "r-1"));
        Assert.Equal(new WorkspaceId("1"), back.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), back.State.ActiveMonitor);

        var named = core.Handle(new Dispatch("workspace", "name:notes"));
        Assert.Equal("error: no such workspace", named.Error);
        Assert.Equal(new WorkspaceId("1"), named.State.ActiveWorkspace);
    }

    [Fact]
    public void PreviousIsGlobalAndPreviousPerMonitorStaysOnThisMonitor()
    {
        var core = TwoMonitors(out var left, out var right);
        Appear(core, 1, left);
        Appear(core, 2, right);

        var none = core.Handle(new Dispatch("workspace", "previous"));
        Assert.Equal(new WorkspaceId("1"), none.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), none.State.ActiveMonitor);

        core.Handle(new Dispatch("workspace", "2"));
        var stayed = core.Handle(new Dispatch("workspace", "previous_per_monitor"));
        Assert.Equal(new WorkspaceId("1"), stayed.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), stayed.State.ActiveMonitor);

        var global = core.Handle(new Dispatch("workspace", "previous"));
        Assert.Equal(new WorkspaceId("2"), global.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), global.State.ActiveMonitor);

        var ready = VisitedBoth(out left, out right);
        var across = ready.Handle(new Dispatch("workspace", "previous"));
        Assert.Equal(WorkspaceId.FromNumber(2, new MonitorId(1)), across.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(1), across.State.ActiveMonitor);

        var local = VisitedBoth(out _, out _);
        var same = local.Handle(new Dispatch("workspace", "previous_per_monitor"));
        Assert.Equal(new WorkspaceId("4"), same.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), same.State.ActiveMonitor);
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("prev_per_monitor")]
    [InlineData("previously")]
    [InlineData("r+notes")]
    [InlineData("m-x")]
    [InlineData("e~one")]
    [InlineData("r+1x")]
    public void TextThatIsNotASelectorDoesNotNameAWorkspace(string name)
    {
        var core = TwoMonitors(out var left, out _);
        Appear(core, 1, left);
        core.Handle(new Dispatch("workspace", "2"));

        var named = core.Handle(new Dispatch("workspace", name));

        Assert.Equal("error: no such workspace", named.Error);
        Assert.Equal(new WorkspaceId("2"), named.State.ActiveWorkspace);
    }

    [Fact]
    public void EmptySelectorsHonorNextAndThisMonitor()
    {
        var core = TwoMonitors(out var left, out _);
        Appear(core, 1, left);

        Assert.Equal(new WorkspaceId("2"), core.Handle(new Dispatch("workspace", "empty")).State.ActiveWorkspace);
        core.Handle(new Dispatch("workspace", "1"));
        // emptym searches this Monitor, the same way empty does.
        Assert.Equal(new WorkspaceId("2"), core.Handle(new Dispatch("workspace", "emptym")).State.ActiveWorkspace);
        core.Handle(new Dispatch("workspace", "1"));
        Assert.Equal(new WorkspaceId("2"), core.Handle(new Dispatch("workspace", "emptyn")).State.ActiveWorkspace);
        core.Handle(new Dispatch("workspace", "1"));
        Assert.Equal(new WorkspaceId("2"), core.Handle(new Dispatch("workspace", "emptynm")).State.ActiveWorkspace);

        var later = TwoMonitors(out left, out _);
        Appear(later, 1, left);
        later.Handle(new Dispatch("workspace", "5"));
        Appear(later, 5, left);
        Assert.Equal(new WorkspaceId("6"), later.Handle(new Dispatch("workspace", "emptyn")).State.ActiveWorkspace);
        later.Handle(new Dispatch("workspace", "5"));
        Assert.Equal(new WorkspaceId("2"), later.Handle(new Dispatch("workspace", "empty")).State.ActiveWorkspace);
    }

    [Fact]
    public void BracketSelectorDoesNotSwitch()
    {
        var core = TwoMonitors(out var left, out var right);
        Appear(core, 1, left);
        Appear(core, 2, right);

        var filtered = core.Handle(new Dispatch("workspace", "r[1-3]"));
        Assert.Equal(new WorkspaceId("1"), filtered.State.ActiveWorkspace);
        Assert.Equal(new MonitorId(0), filtered.State.ActiveMonitor);
        Assert.Equal(new WorkspaceId("1"), Client(filtered, 1).Workspace);

        var moved = core.Handle(new Dispatch("movetoworkspace", "r[1-3]"));
        Assert.Equal(new WorkspaceId("1"), Client(moved, 1).Workspace);
        Assert.Equal(new WorkspaceId("1"), moved.State.ActiveWorkspace);
    }

    [Fact]
    public void UnknownDispatcherIsAnErrorOnThePipeAndAWarningInABind()
    {
        var core = TwoMonitors(out var left, out _);
        Appear(core, 1, left);

        var reply = core.Execute("dispatch nosuch");
        Assert.Equal("error: unknown dispatcher 'nosuch'", reply.Text);
        Assert.Equal("nosuch", reply.Effect.Dispatcher);
        Assert.Equal(new WorkspaceId("1"), reply.Effect.State.ActiveWorkspace);
        Assert.Null(reply.Effect.Close);

        var cased = core.Execute("dispatch NoSuch");
        Assert.Equal("error: unknown dispatcher 'NoSuch'", cased.Text);

        var loaded = core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = SUPER, Q, madeup\n")));
        var warned = Assert.Single(loaded.Events.OfType<ConfigWarned>());
        Assert.Equal(1, warned.Line);
        Assert.Equal("Unknown dispatcher 'madeup'.", warned.Message);

        core.Handle(new KeyPressed("Super_L"));
        var pressed = core.Handle(new KeyPressed("Q"));
        Assert.False(pressed.Consumed);
        Assert.Null(pressed.Close);
        Assert.Equal(new WorkspaceId("1"), pressed.State.ActiveWorkspace);
    }

    [Fact]
    public void BadLayoutMessageReturnsAnErrorAndAKnownOneStillRuns()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\ndwindle:preserve_split = true\n")));
        var area = new Rectangle(0, 0, 1920, 1080);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area));

        var unknown = core.Execute("dispatch layoutmsg nosuch");
        Assert.Equal("error: bad layoutmsg 'nosuch'", unknown.Text);
        Assert.Empty(unknown.Effect.Placements);

        var missing = core.Execute("dispatch layoutmsg");
        Assert.Equal("error: bad layoutmsg", missing.Text);

        var angle = core.Execute("dispatch rotatesplit sideways");
        Assert.Equal("error: bad layoutmsg 'rotatesplit'", angle.Text);

        var ratio = core.Execute("dispatch splitratio");
        Assert.Equal("error: splitratio requires an arg", ratio.Text);

        var ran = core.Execute("dispatch layoutmsg togglesplit");
        Assert.Equal("ok", ran.Text);
        Assert.Equal(new Rectangle(0, 0, 1920, 540), BoundsOf(ran.Effect, 1));
        Assert.Equal(new Rectangle(0, 540, 1920, 540), BoundsOf(ran.Effect, 2));
    }

    private static WindowManager VisitedBoth(out Monitor left, out Monitor right)
    {
        var core = TwoMonitors(out left, out right);
        Appear(core, 1, left);
        Appear(core, 2, right);
        core.Handle(new Dispatch("workspace", "4"));
        Appear(core, 4, left);
        core.Handle(new Dispatch("workspace", "1"));
        core.Handle(new Dispatch("focusmonitor", right.Name));
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(new Dispatch("focusmonitor", left.Name));
        return core;
    }

    private static WindowManager TwoMonitors(out Monitor left, out Monitor right)
    {
        left = new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1080));
        right = new Monitor(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1, true, new Rectangle(1920, 0, 1280, 1024));
        var core = new WindowManager();
        core.Handle(new MonitorsChanged([left, right]));
        return core;
    }

    private static void Appear(WindowManager core, long window, Monitor monitor) =>
        core.Handle(new WindowAppeared(new WindowId(window), monitor.WorkArea));

    private static Workspace Workspace(Outcome outcome, string name) =>
        Assert.Single(outcome.State.Workspaces, workspace => workspace.Id == new WorkspaceId(name));

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Client(outcome, window);
        return Assert.Single(outcome.Placements, placement => placement.Client == client.Id).Bounds;
    }
}
