using Winland.Core;
using Monitor = Winland.Core.Monitor;

namespace Winland.Core.Tests;

public class MonitorTests
{
    [Fact]
    public void DisplayNameSelectsTheMonitorAndAppliesItsPosition()
    {
        var document = ConfigParser.Parse("""
            monitor = \\.\DISPLAY2, preferred, 0x1080, auto
            """);
        var core = new WindowManager(document);
        var left = Display(@"\\.\DISPLAY1", "Samsung", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Dell U2720Q", 1920, 0, 1920, 1080, 1, new Rectangle(1920, 40, 1920, 1040));

        var listed = core.Handle(new MonitorsChanged([left, right]));

        Assert.Equal(new ConfigWarning(1, MonitorRules.NotHonored), Assert.Single(document.Warnings));
        var moved = Assert.Single(listed.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY2");
        Assert.Equal(1920, moved.X);
        Assert.Equal(0, moved.Y);
        Assert.Equal(new Rectangle(1920, 40, 1920, 1040), moved.WorkArea);
        Assert.True(moved.Enabled);
        var stayed = Assert.Single(listed.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY1");
        Assert.Equal(left, stayed);

        var placed = core.Handle(new WindowAppeared(new WindowId(1), right.WorkArea));

        Assert.Equal(moved.WorkArea, BoundsOf(placed, 1));
    }

    [Fact]
    public void DescriptionPrefixSelectsTheMonitor()
    {
        var document = ConfigParser.Parse("""
            monitor = desc:Dell, preferred, 10x20, auto
            """);
        var core = new WindowManager(document);
        var dell = Display(@"\\.\DISPLAY1", "Dell U2720Q", 0, 0, 1920, 1080, 1);
        var samsung = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1920, 1080, 1);

        var outcome = core.Handle(new MonitorsChanged([dell, samsung]));

        Assert.Equal(new ConfigWarning(1, MonitorRules.NotHonored), Assert.Single(document.Warnings));
        var moved = Assert.Single(outcome.State.Monitors, monitor => monitor.Description == "Dell U2720Q");
        Assert.Equal(0, moved.X);
        Assert.Equal(0, moved.Y);
        Assert.Equal(samsung, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY2"));
    }

    [Fact]
    public void DisplayNameDoesNotMatchTheFriendlyName()
    {
        var core = new WindowManager(ConfigParser.Parse("""
            monitor = Dell U2720Q, preferred, 10x20, auto
            """));

        var outcome = core.Handle(new MonitorsChanged([Display(@"\\.\DISPLAY1", "Dell U2720Q", 0, 0, 800, 600, 1)]));

        Assert.Equal(0, Assert.Single(outcome.State.Monitors).X);
    }

    [Fact]
    public void LastRuleWinsAndAFallbackCoversTheRest()
    {
        var core = new WindowManager(ConfigParser.Parse("""
            monitor = , preferred, 5x6, auto
            monitor = \\.\DISPLAY1, preferred, 1x2, auto
            monitor = \\.\display1, preferred, 3x4, auto
            """));

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 0, 0, 100, 100, 1),
            Display(@"\\.\DISPLAY2", "Samsung", 100, 0, 100, 100, 1),
        ]));

        Assert.Equal(0, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY1").X);
        Assert.Equal(0, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY1").Y);
        Assert.Equal(100, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY2").X);
        Assert.Equal(0, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY2").Y);
    }

    [Fact]
    public void NegativePositionShiftsTheWorkArea()
    {
        var core = new WindowManager(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, -1920x-40, auto
            """));
        var display = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, new Rectangle(0, 40, 1920, 1040));

        var outcome = core.Handle(new MonitorsChanged([display]));

        var monitor = Assert.Single(outcome.State.Monitors);
        Assert.Equal(0, monitor.X);
        Assert.Equal(0, monitor.Y);
        Assert.Equal(new Rectangle(0, 40, 1920, 1040), monitor.WorkArea);
    }

    [Fact]
    public void AMonitorWindowsReportsDisabledMovesItsClientsToOneThatRemains()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1, enabled: false);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));

        var moved = core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        Assert.False(Assert.Single(moved.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY2").Enabled);
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(moved, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(moved, 2));
    }

    [Fact]
    public void AWindowOpensOnTheFallbackMonitorWhenEveryMonitorIsDisabled()
    {
        var core = new WindowManager(ConfigParser.Parse("""
            general:gaps_out = 0
            general:border_size = 0
            """));
        var display = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 2560, 1440, 1, enabled: false);
        core.Handle(new MonitorsChanged([display]));

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), display.WorkArea));

        Assert.Single(outcome.State.Clients);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), BoundsOf(outcome, 1));
    }

    [Fact]
    public void EachMonitorOwnsItsOwnLayout()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));

        var first = core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        var second = core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        Assert.Equal(left.WorkArea, BoundsOf(first, 1));
        Assert.Equal(right.WorkArea, BoundsOf(second, 2));
        Assert.Equal(2, second.State.Clients.Count);
        Assert.DoesNotContain(second.Placements, placement => placement.Client == first.State.Clients[0].Id);
    }

    [Fact]
    public void TwoClientsOnOneMonitorStillSplit()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), left.WorkArea));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void HotplugReflowsTheMonitorThatChanged()
    {
        var core = new WindowManager();
        var display = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        core.Handle(new MonitorsChanged([display]));
        core.Handle(new WindowAppeared(new WindowId(1), display.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), display.WorkArea));

        var outcome = core.Handle(new MonitorsChanged([
            display with { Width = 2000, Height = 1000, WorkArea = new Rectangle(0, 0, 2000, 1000) },
        ]));

        Assert.Equal(new Rectangle(0, 0, 1000, 1000), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(1000, 0, 1000, 1000), BoundsOf(outcome, 2));
    }

    [Fact]
    public void UnpluggingAMonitorMovesItsClients()
    {
        var core = new WindowManager();
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var outcome = core.Handle(new MonitorsChanged([left]));

        Assert.Single(outcome.State.Monitors);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), BoundsOf(outcome, 1));
        Assert.Contains(outcome.State.Hidden, client => client.Window == new WindowId(2) && client.Workspace == new WorkspaceId("2"));
        Assert.Contains(outcome.State.Workspaces, workspace => workspace.Id == new WorkspaceId("2") && workspace.Monitor == new MonitorId(0));
    }

    [Fact]
    public void MixedDpiPlacementsUseThePhysicalWorkAreaAndScaleGaps()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("""
            general:gaps_in = 0
            general:gaps_out = 10
            general:border_size = 0
            """)));
        var low = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var high = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 3840, 2160, 2);
        var mid = Display(@"\\.\DISPLAY3", "LG", 5760, 0, 1920, 1080, 1.5);
        core.Handle(new MonitorsChanged([low, high, mid]));

        var first = core.Handle(new WindowAppeared(new WindowId(1), low.WorkArea));
        var second = core.Handle(new WindowAppeared(new WindowId(2), high.WorkArea));
        var third = core.Handle(new WindowAppeared(new WindowId(3), mid.WorkArea));

        Assert.Equal(new Rectangle(10, 10, 1900, 1060), BoundsOf(first, 1));
        Assert.Equal(new Rectangle(1940, 20, 3800, 2120), BoundsOf(second, 2));
        Assert.Equal(new Rectangle(5775, 15, 1890, 1050), BoundsOf(third, 3));
    }

    [Fact]
    public void ARuleAsksForItsModeAndScaleAndKeepsTheScaleWindowsShows()
    {
        var document = ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, 1920x1080@60, 30x40, 1.5
            general:gaps_in = 0
            general:gaps_out = 10
            general:border_size = 0
            """);
        Assert.Equal(new ConfigWarning(1, MonitorRules.NotHonored), Assert.Single(document.Warnings));

        var core = new WindowManager();
        core.Handle(new Configure(document));
        var display = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 3840, 2160, 2);
        var listed = core.Handle(new MonitorsChanged([display]));
        var placed = core.Handle(new WindowAppeared(new WindowId(1), display.WorkArea));

        var monitor = Assert.Single(listed.State.Monitors);
        Assert.Equal(2, monitor.Scale);
        Assert.Equal(0, monitor.X);
        Assert.Equal(0, monitor.Y);
        Assert.Equal(new Rectangle(20, 20, 3800, 2120), BoundsOf(placed, 1));
    }

    [Theory]
    [InlineData(3840, 2, 1920)]
    [InlineData(1920, 1.5, 1280)]
    [InlineData(5, 2, 3)]
    [InlineData(-5, 2, -3)]
    [InlineData(100, 0, 100)]
    public void LogicalPixelsDividePhysicalPixelsByTheMonitorScale(int physical, double scale, int logical)
    {
        Assert.Equal(logical, MonitorPixels.Logical(physical, scale));
    }

    [Fact]
    public void APhysicalPositionIsTheLogicalPositionTimesTheMonitorScale()
    {
        Assert.Equal(3840, MonitorPixels.Physical(1920, 2));
        Assert.Equal(1920, MonitorPixels.Physical(1280, 1.5));
    }

    [Fact]
    public void AScaledMonitorPacksAtItsLogicalWidth()
    {
        var core = new WindowManager(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, 0x0, auto
            monitor = \\.\DISPLAY2, preferred, auto-right, auto
            """));

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 0, 0, 3840, 2160, 2),
            Display(@"\\.\DISPLAY2", "LG", 100, 50, 800, 600, 1),
        ]));

        var packed = Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY2");
        Assert.Equal(100, packed.X);
        Assert.Equal(50, packed.Y);
        Assert.Equal(3840, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY1").Width);
    }

    [Fact]
    public void AutomaticPlacementIgnoresTheReportedOrigin()
    {
        var core = new WindowManager(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, auto-right, auto
            """));
        var display = Display(@"\\.\DISPLAY1", "Dell", 40, 50, 800, 600, 1, new Rectangle(40, 70, 800, 560));

        var outcome = core.Handle(new MonitorsChanged([display]));

        var monitor = Assert.Single(outcome.State.Monitors);
        Assert.Equal(40, monitor.X);
        Assert.Equal(50, monitor.Y);
        Assert.Equal(new Rectangle(40, 70, 800, 560), monitor.WorkArea);
    }

    [Fact]
    public void ADirectionDoesNotMoveTheMonitor()
    {
        var document = ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, 0x0, auto
            monitor = \\.\DISPLAY2, preferred, 1000x0, auto
            monitor = \\.\DISPLAY3, preferred, auto-right, auto
            """);
        var core = new WindowManager(document);

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "A", 0, 0, 1000, 400, 1),
            Display(@"\\.\DISPLAY2", "B", 0, 0, 500, 900, 1),
            Display(@"\\.\DISPLAY3", "C", 9, 9, 200, 100, 1),
        ]));

        Assert.Equal(3, document.Warnings.Count);
        Assert.All(document.Warnings, warning => Assert.Equal(MonitorRules.NotHonored, warning.Message));
        Assert.Equal(0, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY1").X);
        Assert.Equal(0, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY2").X);
        var stayed = Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY3");
        Assert.Equal(9, stayed.X);
        Assert.Equal(9, stayed.Y);
    }

    [Theory]
    [InlineData("addreserved")]
    [InlineData("reserved_area")]
    public void ReservedAreaShrinksTheWorkArea(string keyword)
    {
        var core = new WindowManager(ConfigParser.Parse($"""
            monitor = \\.\DISPLAY1, preferred, 0x0, auto
            monitor = \\.\DISPLAY1, {keyword}, 10, 20, 30, 40
            """));
        var display = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, new Rectangle(0, 40, 1920, 1040));

        var outcome = core.Handle(new MonitorsChanged([display]));

        Assert.Equal(new Rectangle(30, 50, 1850, 1010), Assert.Single(outcome.State.Monitors).WorkArea);
    }

    [Fact]
    public void ReservedAreaOnAScaledMonitorUsesLogicalPixels()
    {
        var core = new WindowManager(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, reserved_area, 10, 20, 30, 40
            """));

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 0, 0, 3840, 2160, 2),
        ]));

        Assert.Equal(new Rectangle(60, 20, 3700, 2100), Assert.Single(outcome.State.Monitors).WorkArea);
    }

    [Fact]
    public void NegativeReservedEdgesCountAsZero()
    {
        var core = new WindowManager(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, addreserved, -5, 10, 0, 0
            """));

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1),
        ]));

        Assert.Equal(new Rectangle(0, 0, 1920, 1070), Assert.Single(outcome.State.Monitors).WorkArea);
    }

    [Fact]
    public void ReservedAreaSurvivesALaterPositionRule()
    {
        var core = new WindowManager(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, addreserved, 1, 2, 3, 4
            monitor = \\.\DISPLAY1, preferred, 10x20, auto
            """));

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 0, 0, 100, 80, 1),
        ]));

        var monitor = Assert.Single(outcome.State.Monitors);
        Assert.Equal(0, monitor.X);
        Assert.Equal(0, monitor.Y);
        Assert.Equal(new Rectangle(3, 1, 93, 77), monitor.WorkArea);
    }

    [Fact]
    public void ATransformIsNotApplied()
    {
        var document = ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, 4x5, auto, transform, 2
            """);
        var core = new WindowManager(document);

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 8, 9, 800, 600, 1),
        ]));

        Assert.Equal(new ConfigWarning(1, MonitorRules.NotHonored), Assert.Single(document.Warnings));
        var monitor = Assert.Single(outcome.State.Monitors);
        Assert.Null(monitor.Orientation);
        Assert.Equal(8, monitor.X);
        Assert.Equal(9, monitor.Y);
    }

    [Fact]
    public void UnsupportedMonitorOptionsWarnAndThePositionStillApplies()
    {
        var document = ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, 3x4, auto, mirror, \\.\DISPLAY2, bitdepth, 10, cm, hdr, vrr, 1, transform, 5
            """);
        var core = new WindowManager(document);

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 0, 0, 100, 100, 1),
        ]));

        Assert.Equal(new ConfigWarning(1, MonitorRules.NotHonored), Assert.Single(document.Warnings));
        Assert.Equal(0, Assert.Single(outcome.State.Monitors).X);
        Assert.Equal(0, Assert.Single(outcome.State.Monitors).Y);
    }

    [Fact]
    public void AConfigDisableDoesNotRemoveTheMonitor()
    {
        var document = ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, disable
            """);
        var core = new WindowManager(document);

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 0, 0, 100, 100, 1),
        ]));

        Assert.Equal(new ConfigWarning(1, MonitorRules.NotHonored), Assert.Single(document.Warnings));
        var monitor = Assert.Single(outcome.State.Monitors);
        Assert.True(monitor.Enabled);
        Assert.Equal(@"\\.\DISPLAY1", monitor.Name);
        Assert.DoesNotContain(outcome.Events, static item => item is MonitorRemoved);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("disabled")]
    public void DisableWarnsAndLeavesTheMonitorEnabled(string disabled)
    {
        var document = ConfigParser.Parse($"""
            monitor = \\.\DISPLAY1, {disabled}
            """);
        var core = new WindowManager(document);

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 0, 0, 100, 100, 1),
        ]));

        Assert.Equal(new ConfigWarning(1, MonitorRules.NotHonored), Assert.Single(document.Warnings));
        Assert.True(Assert.Single(outcome.State.Monitors).Enabled);
    }

    [Fact]
    public void APositionRuleWarnsAndIsNotApplied()
    {
        var document = ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, auto, auto
            """);

        Assert.Equal(new ConfigWarning(1, MonitorRules.NotHonored), Assert.Single(document.Warnings));
    }

    [Fact]
    public void AMalformedPositionWarnsAndIsNotApplied()
    {
        var document = ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, nope, auto
            """);
        var core = new WindowManager(document);

        var outcome = core.Handle(new MonitorsChanged([Display(@"\\.\DISPLAY1", "Dell", 8, 9, 100, 100, 1)]));

        Assert.Equal(new ConfigWarning(1, MonitorRules.NotHonored), Assert.Single(document.Warnings));
        Assert.Equal(8, Assert.Single(outcome.State.Monitors).X);
        Assert.Equal(9, Assert.Single(outcome.State.Monitors).Y);
    }

    [Fact]
    public void AMalformedReservedAreaWarnsAndIsNotApplied()
    {
        var document = ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, addreserved, 1
            """);
        var core = new WindowManager(document);

        var outcome = core.Handle(new MonitorsChanged([Display(@"\\.\DISPLAY1", "Dell", 0, 0, 100, 80, 1)]));

        Assert.Equal(new ConfigWarning(1, "Malformed monitor rule."), Assert.Single(document.Warnings));
        Assert.Equal(new Rectangle(0, 0, 100, 80), Assert.Single(outcome.State.Monitors).WorkArea);
    }

    [Fact]
    public void LoadingConfigAppliesPositionToMonitorsAlreadyKnown()
    {
        var core = new WindowManager();
        core.Handle(new MonitorsChanged([Display(@"\\.\DISPLAY1", "Dell", 0, 0, 100, 100, 1)]));

        var outcome = core.Handle(new ConfigLoaded(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, addreserved, 1, 2, 3, 4
            """)));

        var monitor = Assert.Single(outcome.State.Monitors);
        Assert.Equal(0, monitor.X);
        Assert.Equal(0, monitor.Y);
        Assert.Equal(new Rectangle(3, 1, 93, 97), monitor.WorkArea);
    }

    private static Monitor Display(
        string name,
        string description,
        int x,
        int y,
        int width,
        int height,
        double scale,
        Rectangle? workArea = null,
        bool enabled = true) =>
        new(name, description, x, y, width, height, scale, enabled, workArea ?? new Rectangle(x, y, width, height));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }
}
