using Winland;

namespace Winland.Tests;

public class LaunchTests
{
    [Fact]
    public void ExecLaunchesTheCommand()
    {
        var outcome = new Core().Handle(new Dispatch("exec", "notepad --title hello"));

        Assert.Equal("exec", outcome.Dispatcher);
        Assert.Equal(new Launch("notepad --title hello", null) { InitialWorkspace = WorkspaceId.Initial }, outcome.Launch);
    }

    [Fact]
    public void AWordNamedElevatedIsPartOfTheCommand()
    {
        var outcome = new Core().Handle(new Dispatch("exec", "elevated notepad"));

        Assert.Equal(new Launch("elevated notepad", null) { InitialWorkspace = WorkspaceId.Initial }, outcome.Launch);
    }

    [Fact]
    public void BoundExecLaunchesTheCommand()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = SUPER, E, exec, notepad")));
        core.Handle(new KeyPressed("Super_L"));

        var plain = core.Handle(new KeyPressed("E"));

        Assert.Equal(new Launch("notepad", null) { InitialWorkspace = WorkspaceId.Initial }, plain.Launch);
    }

    [Fact]
    public void ExecrRequestsTheCommandWithoutAShell()
    {
        var outcome = new Core().Handle(new Dispatch("execr", "notepad"));

        Assert.Equal(new Launch("notepad", null) { Raw = true, InitialWorkspace = WorkspaceId.Initial }, outcome.Launch);
    }

    [Fact]
    public void EmptyExecDoesNotLaunch()
    {
        Assert.Null(new Core().Handle(new Dispatch("exec", "   ")).Launch);
        var block = new Core().Handle(new Dispatch("exec", "[elevated]"));
        Assert.Null(block.Launch);
        Assert.Equal([new ConfigWarned(0, "Unsupported window rule 'elevated'.", null)], block.Events.OfType<ConfigWarned>());
    }

    [Fact]
    public void RulesBlockLaunchesAndKeepsTheRules()
    {
        var outcome = new Core().Handle(new Dispatch("exec", "[float; workspace 2] kitty"));

        Assert.Equal("exec", outcome.Dispatcher);
        Assert.NotNull(outcome.Launch);
        Assert.Equal("kitty", outcome.Launch.CommandLine);
        Assert.Equal(
            [
                new WindowRule(WindowRuleEffect.Float, null, null, null, null),
                new WindowRule(WindowRuleEffect.Workspace, "2", null, null, null),
            ],
            outcome.Launch.Rules);
    }

    [Fact]
    public void UnknownExecEffectsWarnAndTheCommandStillLaunches()
    {
        var outcome = new Core().Handle(new Dispatch("exec", "[float; suppress_event maximize; confine_pointer] cmd"));

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
        var outcome = new Core().Handle(new Dispatch("exec", "[float; workspace] kitty"));

        Assert.Null(outcome.Launch);
        Assert.Equal([new ConfigWarned(0, "Malformed window rule.", null)], outcome.Events.OfType<ConfigWarned>());
    }

    [Fact]
    public void UnclosedRuleBlockDoesNotLaunch()
    {
        var outcome = new Core().Handle(new Dispatch("exec", "[float; workspace 2 kitty"));

        Assert.Null(outcome.Launch);
        Assert.Equal([new ConfigWarned(0, "Unclosed rule block.", null)], outcome.Events.OfType<ConfigWarned>());
    }

    [Fact]
    public void StartupLaunchesExecAndExecOnceAndReloadSkipsOnce()
    {
        var document = ConfigParser.Parse(
            "exec = every\n" +
            "exec-once = once\n" +
            "execr = raw-every\n" +
            "execr-once = raw-once\n" +
            "exec-shutdown = bye\n");
        var core = new Core();

        var startup = core.StartupLaunches(document);
        var reload = core.ReloadLaunches(document);
        var shutdown = Core.ShutdownLaunches(document);

        Assert.Equal(
            [
                new Launch("every", null) { InitialWorkspace = WorkspaceId.Initial },
                new Launch("once", null),
                new Launch("raw-every", null) { Raw = true, InitialWorkspace = WorkspaceId.Initial },
                new Launch("raw-once", null) { Raw = true },
            ],
            startup);
        Assert.Equal(
            [
                new Launch("every", null) { InitialWorkspace = WorkspaceId.Initial },
                new Launch("raw-every", null) { Raw = true, InitialWorkspace = WorkspaceId.Initial },
            ],
            reload);
        Assert.Equal([new Launch("bye", null)], shutdown);
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
        Assert.Equal(new Launch("notepad", null), launches[1]);
    }

    [Fact]
    public void AnExecRuleBlockKeepsTheScrollingWidth()
    {
        var launches = new Core().StartupLaunches(ConfigParser.Parse("exec = [scrolling_width 0.25] app\n"));

        Assert.Equal(0.25, Assert.Single(launches[0].Rules).ScrollingWidth);
    }

    [Fact]
    public void ExecRulesApplyToTheFirstWindowOfTheProcessOrAChild()
    {
        var core = new Core();
        var area = new Rectangle(0, 0, 800, 600);
        var bounds = new Rectangle(12, 14, 200, 100);
        var launch = core.Handle(new Dispatch("exec", "[float; workspace 2] kitty")).Launch;
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
        var launch = core.Handle(new Dispatch("exec", "[tile] tool")).Launch;
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

        var again = core.Handle(new Dispatch("exec", "[float] other")).Launch;
        Assert.NotNull(again);
        core.NoteLaunched(70, again);
        now = 1_000 + 60_001;
        var expired = core.Handle(new WindowAppeared(new WindowId(2), area) { ProcessId = 70 });
        Assert.False(Assert.Single(expired.State.Clients, candidate => candidate.Window == new WindowId(2)).Floating);
    }
}
