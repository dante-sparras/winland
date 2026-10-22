namespace Winland.Install.Tests;

public class ManualStartElevationTests
{
    [Fact]
    public void UnelevatedStartRelaunchesOnceWithTheSameArgumentsAndALoopGuard()
    {
        var choice = ManualStartElevation.Decide(
            isElevated: false,
            optOutVariable: null,
            arguments: ["--config", @"C:\temp\winland.conf"]);

        var relaunch = Assert.IsType<ElevationChoice.Relaunch>(choice);
        Assert.Equal(
            ["--winland-elevated", "--config", @"C:\temp\winland.conf"],
            relaunch.Arguments);
    }

    [Fact]
    public void ElevatedStartRunsAndDoesNotRelaunch()
    {
        var choice = ManualStartElevation.Decide(
            isElevated: true,
            optOutVariable: null,
            arguments: ["--config", @"C:\temp\winland.conf"]);

        var run = Assert.IsType<ElevationChoice.Continue>(choice);
        Assert.Equal(["--config", @"C:\temp\winland.conf"], run.Arguments);
    }

    [Fact]
    public void LoopGuardStopsASecondRelaunchWhenElevationDidNotStick()
    {
        var choice = ManualStartElevation.Decide(
            isElevated: false,
            optOutVariable: null,
            arguments: ["--winland-elevated", "--config", @"C:\temp\winland.conf"]);

        var refused = Assert.IsType<ElevationChoice.Refuse>(choice);
        Assert.Equal("Winland could not restart with administrator rights.", refused.Message);
    }

    [Fact]
    public void ElevatedRelaunchDropsTheLoopGuardAndRunsTheOriginalArguments()
    {
        var choice = ManualStartElevation.Decide(
            isElevated: true,
            optOutVariable: null,
            arguments: ["--WINLAND-ELEVATED", "--config", @"C:\temp\winland.conf"]);

        var run = Assert.IsType<ElevationChoice.Continue>(choice);
        Assert.Equal(["--config", @"C:\temp\winland.conf"], run.Arguments);
    }

    [Fact]
    public void OptOutVariableStartsWithoutElevation()
    {
        // WINLAND_NO_ELEVATE=1 exists for tests. Elevated features then do not work.
        var choice = ManualStartElevation.Decide(
            isElevated: false,
            optOutVariable: "1",
            arguments: ["--config", @"C:\temp\winland.conf"]);

        var run = Assert.IsType<ElevationChoice.Continue>(choice);
        Assert.Equal(["--config", @"C:\temp\winland.conf"], run.Arguments);
    }

    [Fact]
    public void OptOutFlagStartsWithoutElevationAndIsNotPassedOn()
    {
        var choice = ManualStartElevation.Decide(
            isElevated: false,
            optOutVariable: null,
            arguments: ["--no-elevate", "--config", @"C:\temp\winland.conf"]);

        var run = Assert.IsType<ElevationChoice.Continue>(choice);
        Assert.Equal(["--config", @"C:\temp\winland.conf"], run.Arguments);
    }

    [Fact]
    public void APerUserInstallNeverRelaunchesThroughUac()
    {
        var choice = ManualStartElevation.Decide(
            isElevated: false,
            optOutVariable: null,
            arguments: [],
            userInstall: true);

        var run = Assert.IsType<ElevationChoice.Continue>(choice);
        Assert.Empty(run.Arguments);
    }

    [Fact]
    public void ThePerUserFlagIsNotPassedOnToTheDaemon()
    {
        var choice = ManualStartElevation.Decide(
            isElevated: false,
            optOutVariable: null,
            arguments: ["--no-elevate"],
            userInstall: true);

        var run = Assert.IsType<ElevationChoice.Continue>(choice);
        Assert.Empty(run.Arguments);
    }

    [Fact]
    public void OtherOptOutValuesStillRelaunch()
    {
        var choice = ManualStartElevation.Decide(
            isElevated: false,
            optOutVariable: "true",
            arguments: []);

        var relaunch = Assert.IsType<ElevationChoice.Relaunch>(choice);
        Assert.Equal(["--winland-elevated"], relaunch.Arguments);
    }
}
