namespace Winland.Application.Tests;

public class MonitorLayoutEventTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1040);
    private static readonly Monitor One = new("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area);

    [Fact]
    public void AMonitorThatComesOrChangesItsModeLaysTheMonitorsOutAgain()
    {
        var core = new WindowManager();

        Assert.Single(core.Handle(new MonitorsChanged([One])).Events, e => e is MonitorLayoutChanged);
        Assert.Single(core.Handle(new MonitorsChanged([One with { Scale = 1.25 }])).Events, e => e is MonitorLayoutChanged);
        Assert.Single(core.Handle(new MonitorsChanged([One with { Scale = 1.25, RefreshRate = 144 }])).Events, e => e is MonitorLayoutChanged);
    }

    [Fact]
    public void AWorkAreaChangeOrTheSameMonitorsAgainIsNoLayoutChange()
    {
        var core = new WindowManager();
        core.Handle(new MonitorsChanged([One]));

        Assert.DoesNotContain(core.Handle(new MonitorsChanged([One])).Events, e => e is MonitorLayoutChanged);
        Assert.DoesNotContain(core.Handle(new MonitorsChanged([One with { WorkArea = new Rectangle(0, 0, 1920, 1000) }])).Events, e => e is MonitorLayoutChanged);
    }

    [Fact]
    public void AReloadLaysTheMonitorsOutAfterItIsReadAndTheStreamSaysNothing()
    {
        var core = new WindowManager();
        core.Handle(new MonitorsChanged([One]));

        var reloaded = core.Handle(new ConfigLoaded(ConfigParser.Parse("general:gaps_in = 3\n")));

        var events = reloaded.Events.ToList();
        Assert.True(events.FindIndex(e => e is MonitorLayoutChanged) > events.FindIndex(e => e is ConfigReloaded));
        Assert.DoesNotContain(CommandProtocol.FormatStream(reloaded.Events, reloaded.State), line => line.Contains("layout", StringComparison.Ordinal));
    }
}
