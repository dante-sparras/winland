using Winland;

namespace Winland.Tests;

public class InitialWorkspaceTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void AStartedProgramOpensOnTheWorkspaceItWasStartedOn()
    {
        var core = Loaded("");
        Start(core, pid: 500);
        core.Execute("dispatch workspace 2");

        var opened = Open(core, window: 1, pid: 500);

        Assert.Equal(new WorkspaceId("1"), WorkspaceOf(opened, 1));
        Assert.Equal(new WorkspaceId("2"), opened.State.ActiveWorkspace);
    }

    [Fact]
    public void TrackingOffOpensOnTheWorkspaceShownNow()
    {
        var core = Loaded("misc:initial_workspace_tracking = 0\n");
        Start(core, pid: 500);
        core.Execute("dispatch workspace 2");

        var opened = Open(core, window: 1, pid: 500);

        Assert.Equal(new WorkspaceId("2"), WorkspaceOf(opened, 1));
        Assert.Equal("{\"option\":\"misc:initial_workspace_tracking\",\"int\":0,\"set\":true}", core.Execute("getoption misc:initial_workspace_tracking").Text);
    }

    [Fact]
    public void TheFirstWindowUsesTheTokenUp()
    {
        var core = Loaded("");
        Start(core, pid: 500);
        core.Execute("dispatch workspace 2");
        Open(core, window: 1, pid: 500);

        var second = Open(core, window: 2, pid: 500);

        Assert.Equal(new WorkspaceId("2"), WorkspaceOf(second, 2));
    }

    [Fact]
    public void ATokenExpiresAfterTheTimeout()
    {
        var now = 0L;
        var core = Loaded("misc:initial_workspace_token_timeout = 3\n");
        core.FollowClock = () => now;
        Start(core, pid: 500);
        core.Execute("dispatch workspace 2");
        now = 3_001;

        var opened = Open(core, window: 1, pid: 500);

        Assert.Equal(new WorkspaceId("2"), WorkspaceOf(opened, 1));
    }

    [Fact]
    public void AChildOfTheStartedProgramHoldsTheTokenToo()
    {
        var core = Loaded("");
        Start(core, pid: 500);
        core.Execute("dispatch workspace 2");

        var opened = core.Handle(new WindowAppeared(new WindowId(1), Area) { ProcessId = 600, AncestorProcessIds = [500] });

        Assert.Equal(new WorkspaceId("1"), WorkspaceOf(opened, 1));
    }

    [Fact]
    public void PersistentTrackingSendsEveryWindowWhereTheFirstOneIsNow()
    {
        var core = Loaded("misc:initial_workspace_tracking = 2\n");
        Start(core, pid: 500);
        core.Execute("dispatch workspace 2");
        Open(core, window: 1, pid: 500);
        Assert.Equal(new WorkspaceId("1"), WorkspaceOf(Open(core, window: 2, pid: 500), 2));

        core.Execute("dispatch movetoworkspacesilent 3,address:0x1");
        var third = Open(core, window: 3, pid: 500);

        Assert.Equal(new WorkspaceId("3"), WorkspaceOf(third, 3));
    }

    [Fact]
    public void PersistentTrackingEndsWhenTheFirstWindowCloses()
    {
        var core = Loaded("misc:initial_workspace_tracking = 2\n");
        Start(core, pid: 500);
        core.Execute("dispatch workspace 2");
        Open(core, window: 1, pid: 500);
        core.Handle(new WindowClosed(new WindowId(1)));

        var opened = Open(core, window: 2, pid: 500);

        Assert.Equal(new WorkspaceId("2"), WorkspaceOf(opened, 2));
    }

    [Fact]
    public void AWorkspaceRuleWinsOverTheToken()
    {
        var core = Loaded("windowrulev2 = workspace 4 silent, class:^(ruled)$\n");
        Start(core, pid: 500);
        core.Execute("dispatch workspace 2");

        var opened = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            ProcessId = 500,
            Traits = WindowTraits.Normal with { Class = "ruled" },
        });

        Assert.Equal(new WorkspaceId("4"), WorkspaceOf(opened, 1));
    }

    private static Core Loaded(string config)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(config)));
        return core;
    }

    // Runs exec as a bind or the pipe does, and reports the process the adapter started for it.
    private static void Start(Core core, int pid)
    {
        var launch = Assert.Single(core.Execute("dispatch exec app").Effect.Launches);
        core.NoteLaunched(pid, launch);
    }

    private static Outcome Open(Core core, long window, int pid) =>
        core.Handle(new WindowAppeared(new WindowId(window), Area) { ProcessId = pid });

    private static WorkspaceId WorkspaceOf(Outcome outcome, long window)
    {
        foreach (var client in outcome.State.Clients)
        {
            if (client.Window == new WindowId(window))
                return client.Workspace;
        }

        foreach (var client in outcome.State.Hidden)
        {
            if (client.Window == new WindowId(window))
                return client.Workspace;
        }

        throw new InvalidOperationException($"Window {window} is not a Client.");
    }
}
