using Winland;

namespace Winland.Tests;

public class LaunchTests
{
    [Fact]
    public void ExecRequestsTheCommandDeElevated()
    {
        var outcome = new Core().Dispatch("exec", "notepad --title hello", allowsElevation: true);

        Assert.Equal("exec", outcome.Dispatcher);
        Assert.True(outcome.AllowsElevation);
        Assert.Equal(new Launch("notepad --title hello", null, false) { InitialWorkspace = WorkspaceId.Initial }, outcome.Launch);
    }

    [Fact]
    public void ElevatedPrefixIsHonoredWhenTheCallerMayElevate()
    {
        var outcome = new Core().Dispatch("exec", "[elevated] notepad", allowsElevation: true);

        Assert.Equal(new Launch("notepad", null, true) { InitialWorkspace = WorkspaceId.Initial }, outcome.Launch);
    }

    [Fact]
    public void PipeDropsElevationEvenWhenTheCommandAsks()
    {
        var reply = new Core().Execute("dispatch exec [elevated] notepad");

        Assert.Equal("ok", reply.Text);
        Assert.False(reply.Effect.AllowsElevation);
        Assert.Equal(new Launch("notepad", null, false) { InitialWorkspace = WorkspaceId.Initial }, reply.Effect.Launch);
    }

    [Fact]
    public void AWordNamedElevatedIsPartOfTheCommand()
    {
        var outcome = new Core().Dispatch("exec", "elevated notepad", allowsElevation: true);

        Assert.Equal(new Launch("elevated notepad", null, false) { InitialWorkspace = WorkspaceId.Initial }, outcome.Launch);
    }

    [Fact]
    public void BoundExecStaysDeElevatedUntilTheCommandAsks()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = SUPER, E, exec, notepad") with { ElevationTrusted = true }));
        core.Handle(new KeyPressed("Super_L"));

        var plain = core.Handle(new KeyPressed("E"));

        Assert.True(plain.AllowsElevation);
        Assert.Equal(new Launch("notepad", null, false) { InitialWorkspace = WorkspaceId.Initial }, plain.Launch);

        core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = SUPER, E, exec, [elevated] notepad") with { ElevationTrusted = true }));
        core.Handle(new KeyPressed("Super_L"));

        var asked = core.Handle(new KeyPressed("E"));

        Assert.True(asked.AllowsElevation);
        Assert.Equal(new Launch("notepad", null, true) { InitialWorkspace = WorkspaceId.Initial }, asked.Launch);
    }

    [Fact]
    public void ExecrRequestsTheCommandWithoutAShell()
    {
        var outcome = new Core().Dispatch("execr", "[elevated] notepad", allowsElevation: true);

        Assert.Equal(new Launch("notepad", null, true) { Raw = true, InitialWorkspace = WorkspaceId.Initial }, outcome.Launch);
    }

    [Fact]
    public void EmptyExecDoesNotLaunch()
    {
        Assert.Null(new Core().Dispatch("exec", "   ", allowsElevation: true).Launch);
        Assert.Null(new Core().Dispatch("exec", "[elevated]", allowsElevation: true).Launch);
    }

    [Fact]
    public void RulesBlockLaunchesAndKeepsTheRules()
    {
        var outcome = new Core().Dispatch("exec", "[float; workspace 2] kitty", allowsElevation: true);

        Assert.Equal("exec", outcome.Dispatcher);
        Assert.NotNull(outcome.Launch);
        Assert.Equal("kitty", outcome.Launch.CommandLine);
        Assert.False(outcome.Launch.Elevated);
        Assert.Equal(
            [
                new WindowRule(WindowRuleEffect.Float, null, null, null, null),
                new WindowRule(WindowRuleEffect.Workspace, "2", null, null, null),
            ],
            outcome.Launch.Rules);
    }

    [Fact]
    public void ElevatedCombinesWithRulesInTheBlockAndAsALeadingToken()
    {
        var inside = new Core().Dispatch("exec", "[float; elevated; workspace 2 silent] kitty", allowsElevation: true);
        var leading = new Core().Dispatch("execr", "[elevated] [tile] notepad", allowsElevation: true);

        Assert.NotNull(inside.Launch);
        Assert.True(inside.Launch.Elevated);
        Assert.Equal("kitty", inside.Launch.CommandLine);
        Assert.Equal(
            [
                new WindowRule(WindowRuleEffect.Float, null, null, null, null),
                new WindowRule(WindowRuleEffect.Workspace, "2 silent", null, null, null),
            ],
            inside.Launch.Rules);

        Assert.NotNull(leading.Launch);
        Assert.True(leading.Launch.Raw);
        Assert.True(leading.Launch.Elevated);
        Assert.Equal("notepad", leading.Launch.CommandLine);
        Assert.Equal([new WindowRule(WindowRuleEffect.Tile, null, null, null, null)], leading.Launch.Rules);
    }

    [Fact]
    public void PipeDropsElevationInsideARuleBlock()
    {
        var reply = new Core().Execute("dispatch exec [elevated; float] notepad");

        Assert.Equal("ok", reply.Text);
        Assert.False(reply.Effect.AllowsElevation);
        Assert.NotNull(reply.Effect.Launch);
        Assert.False(reply.Effect.Launch.Elevated);
        Assert.Equal("notepad", reply.Effect.Launch.CommandLine);
        Assert.Equal([new WindowRule(WindowRuleEffect.Float, null, null, null, null)], reply.Effect.Launch.Rules);
    }

    [Fact]
    public void UnknownExecEffectsWarnAndTheCommandStillLaunches()
    {
        var outcome = new Core().Dispatch("exec", "[float; suppress_event maximize; confine_pointer] cmd", allowsElevation: true);

        Assert.NotNull(outcome.Launch);
        Assert.Equal("cmd", outcome.Launch.CommandLine);
        Assert.Equal([new WindowRule(WindowRuleEffect.Float, null, null, null, null)], outcome.Launch.Rules);
        Assert.Equal(
            [
                new ConfigWarned(0, "Unsupported window rule 'suppress_event'.", null),
                new ConfigWarned(0, "Unsupported window rule 'confine_pointer'.", null),
            ],
            outcome.Events.OfType<ConfigWarned>());
    }

    [Fact]
    public void ABadKnownEffectDoesNotLaunch()
    {
        var outcome = new Core().Dispatch("exec", "[float; workspace] kitty", allowsElevation: true);

        Assert.Null(outcome.Launch);
        Assert.Equal([new ConfigWarned(0, "Malformed window rule.", null)], outcome.Events.OfType<ConfigWarned>());
    }

    [Fact]
    public void UnclosedRuleBlockDoesNotLaunch()
    {
        var outcome = new Core().Dispatch("exec", "[float; workspace 2 kitty", allowsElevation: true);

        Assert.Null(outcome.Launch);
        Assert.Equal([new ConfigWarned(0, "Unclosed rule block.", null)], outcome.Events.OfType<ConfigWarned>());
    }

    [Fact]
    public void StartupLaunchesExecAndExecOnceAndReloadSkipsOnce()
    {
        var document = ConfigParser.Parse(
            "exec = every\n" +
            "exec-once = [elevated] once\n" +
            "execr = raw-every\n" +
            "execr-once = raw-once\n" +
            "exec-shutdown = bye\n") with { ElevationTrusted = true };
        var core = new Core();

        var startup = core.StartupLaunches(document);
        var reload = core.ReloadLaunches(document);
        var shutdown = core.ShutdownLaunches(document);

        Assert.Equal(
            [
                new Launch("every", null, false) { InitialWorkspace = WorkspaceId.Initial },
                new Launch("once", null, true),
                new Launch("raw-every", null, false) { Raw = true, InitialWorkspace = WorkspaceId.Initial },
                new Launch("raw-once", null, false) { Raw = true },
            ],
            startup);
        Assert.Equal(
            [
                new Launch("every", null, false) { InitialWorkspace = WorkspaceId.Initial },
                new Launch("raw-every", null, false) { Raw = true, InitialWorkspace = WorkspaceId.Initial },
            ],
            reload);
        Assert.Equal([new Launch("bye", null, false)], shutdown);
    }

    [Fact]
    public void StartupHonorsKnownRulesAndWarnsOnUnknownOnes()
    {
        var document = ConfigParser.Parse(
            "exec = [float; workspace 2; confine_pointer] kitty\n" +
            "exec-once = notepad\n" +
            "exec = [float; workspace] broken\n");

        Assert.Equal(
            [
                new ConfigWarning(1, "Unsupported window rule 'confine_pointer'."),
                new ConfigWarning(3, "Malformed window rule."),
            ],
            document.Warnings);
        var launches = new Core().StartupLaunches(document);
        Assert.Equal(2, launches.Count);
        Assert.Equal("kitty", launches[0].CommandLine);
        Assert.Equal(
            [
                new WindowRule(WindowRuleEffect.Float, null, null, null, null),
                new WindowRule(WindowRuleEffect.Workspace, "2", null, null, null),
            ],
            launches[0].Rules);
        Assert.Equal(new Launch("notepad", null, false), launches[1]);
    }

    [Fact]
    public void AnExecRuleBlockKeepsTheScrollingWidth()
    {
        var launches = new Core().StartupLaunches(ConfigParser.Parse("exec = [scrolling_width 0.25] app\n"));

        Assert.Equal(0.25, Assert.Single(launches[0].Rules).ScrollingWidth);
    }

    [Fact]
    public void EnvPairsReachEveryLaunchAndReloadReplacesThem()
    {
        var first = ConfigParser.Parse(
            "env = BROWSER,firefox\n" +
            "env = PATH_EXTRA,a,b\n" +
            "env = ,missing\n" +
            "exec = every\n" +
            "exec-once = once\n" +
            "execr = raw\n" +
            "exec-shutdown = bye\n");
        Assert.Equal(new ConfigWarning(3, "Environment variable name is empty."), Assert.Single(first.Warnings));
        Assert.Equal(
            [
                new EnvironmentAssignment("BROWSER", "firefox"),
                new EnvironmentAssignment("PATH_EXTRA", "a,b"),
            ],
            first.Environment);

        var core = new Core(first);
        var startup = core.StartupLaunches(first);
        Assert.Equal(first.Environment, startup[0].Environment);
        Assert.Equal(first.Environment, startup[1].Environment);
        Assert.Equal(first.Environment, startup[2].Environment);
        Assert.Equal(first.Environment, core.ShutdownLaunches(first)[0].Environment);
        Assert.Equal(first.Environment, core.Dispatch("exec", "now", allowsElevation: true).Launch!.Environment);

        var second = ConfigParser.Parse("env = BROWSER,chrome\nexec = every\nexec-shutdown = bye\n");
        core.Handle(new Configure(second));

        Assert.Equal([new EnvironmentAssignment("BROWSER", "chrome")], core.ReloadLaunches(second)[0].Environment);
        Assert.Equal([new EnvironmentAssignment("BROWSER", "chrome")], core.ShutdownLaunches(second)[0].Environment);
        Assert.Equal([new EnvironmentAssignment("BROWSER", "chrome")], core.Dispatch("exec", "later", allowsElevation: true).Launch!.Environment);
    }

    [Fact]
    public void ExecRulesApplyToTheFirstWindowOfTheProcessOrAChild()
    {
        var core = new Core();
        var area = new Rectangle(0, 0, 800, 600);
        var bounds = new Rectangle(12, 14, 200, 100);
        var launch = core.Dispatch("exec", "[float; workspace 2] kitty", allowsElevation: true).Launch;
        Assert.NotNull(launch);
        core.NoteLaunched(40, launch);

        var floated = core.Handle(new WindowAppeared(new WindowId(1), area)
        {
            ProcessId = 40,
            Bounds = bounds,
        });
        var client = Assert.Single(floated.State.Clients);
        Assert.True(client.Floating);
        Assert.Equal(new WorkspaceId("2"), client.Workspace);
        Assert.Equal(NewFloat.Centered(area, bounds), Assert.Single(floated.Placements).Bounds);

        var later = core.Handle(new WindowAppeared(new WindowId(2), area) { ProcessId = 40 });
        var second = Assert.Single(later.State.Clients, candidate => candidate.Window == new WindowId(2));
        Assert.False(second.Floating);
    }

    [Fact]
    public void AChildProcessInheritsTheLaunchRulesUntilTheTimeout()
    {
        var core = new Core();
        long now = 1_000;
        core.FollowClock = () => now;
        var area = new Rectangle(0, 0, 640, 480);
        var launch = core.Dispatch("exec", "[tile] tool", allowsElevation: true).Launch;
        Assert.NotNull(launch);
        core.NoteLaunched(10, launch);
        core.NoteProcess(20, 10);

        var child = core.Handle(new WindowAppeared(new WindowId(1), area)
        {
            ProcessId = 30,
            AncestorProcessIds = [20, 10],
            Traits = WindowTraits.Normal with { Dialog = true },
        });
        var client = Assert.Single(child.State.Clients);
        Assert.False(client.Floating);

        var again = core.Dispatch("exec", "[float] other", allowsElevation: true).Launch;
        Assert.NotNull(again);
        core.NoteLaunched(70, again);
        now = 1_000 + 60_001;
        var expired = core.Handle(new WindowAppeared(new WindowId(2), area) { ProcessId = 70 });
        Assert.False(Assert.Single(expired.State.Clients, candidate => candidate.Window == new WindowId(2)).Floating);
    }
}
