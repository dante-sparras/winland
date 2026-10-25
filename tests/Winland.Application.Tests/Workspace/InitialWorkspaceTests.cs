namespace Winland.Application.Tests;

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
    public void ATokenExpiresAfterTenSeconds()
    {
        var now = 0L;
        var core = Loaded("");
        core.Clock = () => now;
        Start(core, pid: 500);
        core.Execute("dispatch workspace 2");
        now = 10_001;

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
    public void AWorkspaceRuleWinsOverTheToken()
    {
        var core = Loaded("windowrule = workspace 4 silent, class:^(ruled)$\n");
        Start(core, pid: 500);
        core.Execute("dispatch workspace 2");

        var opened = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            ProcessId = 500,
            Traits = WindowTraits.Normal with { Class = "ruled" },
        });

        Assert.Equal(new WorkspaceId("4"), WorkspaceOf(opened, 1));
    }

    private static WindowManager Loaded(string config)
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(config)));
        return core;
    }

    // Runs exec as a bind or the pipe does, and reports the process the adapter started for it.
    private static void Start(WindowManager core, int pid)
    {
        var launch = Assert.Single(core.Execute("dispatch exec app").Effect.Launches);
        core.NoteLaunched(pid, launch);
    }

    private static Outcome Open(WindowManager core, long window, int pid) =>
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
