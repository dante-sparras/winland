using Winland;
using Monitor = Winland.Monitor;

namespace Winland.Tests;

public class MonitorTests
{
    [Fact]
    public void DisplayNameSelectsTheMonitorAndAppliesItsPosition()
    {
        var core = new Core(ConfigParser.Parse("""
            monitor = \\.\DISPLAY2, preferred, 0x1080, auto
            """));
        var left = Display(@"\\.\DISPLAY1", "Samsung", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Dell U2720Q", 1920, 0, 1920, 1080, 1, new Rectangle(1920, 40, 1920, 1040));

        var listed = core.Handle(new MonitorsChanged([left, right]));

        var moved = Assert.Single(listed.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY2");
        Assert.Equal(0, moved.X);
        Assert.Equal(1080, moved.Y);
        Assert.Equal(new Rectangle(0, 1120, 1920, 1040), moved.WorkArea);
        Assert.True(moved.Enabled);
        var stayed = Assert.Single(listed.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY1");
        Assert.Equal(left, stayed);

        var placed = core.Handle(new WindowAppeared(new WindowId(1), right.WorkArea));

        Assert.Equal(moved.WorkArea, BoundsOf(placed, 1));
    }

    [Fact]
    public void DescriptionPrefixSelectsTheMonitor()
    {
        var core = new Core(ConfigParser.Parse("""
            monitor = desc:Dell, preferred, 10x20, auto
            """));
        var dell = Display(@"\\.\DISPLAY1", "Dell U2720Q", 0, 0, 1920, 1080, 1);
        var samsung = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1920, 1080, 1);

        var outcome = core.Handle(new MonitorsChanged([dell, samsung]));

        var moved = Assert.Single(outcome.State.Monitors, monitor => monitor.Description == "Dell U2720Q");
        Assert.Equal(10, moved.X);
        Assert.Equal(20, moved.Y);
        Assert.Equal(samsung, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY2"));
    }

    [Fact]
    public void DisplayNameDoesNotMatchTheFriendlyName()
    {
        var core = new Core(ConfigParser.Parse("""
            monitor = Dell U2720Q, preferred, 10x20, auto
            """));

        var outcome = core.Handle(new MonitorsChanged([Display(@"\\.\DISPLAY1", "Dell U2720Q", 0, 0, 800, 600, 1)]));

        Assert.Equal(0, Assert.Single(outcome.State.Monitors).X);
    }

    [Fact]
    public void LastRuleWinsAndAFallbackCoversTheRest()
    {
        var core = new Core(ConfigParser.Parse("""
            monitor = , preferred, 5x6, auto
            monitor = \\.\DISPLAY1, preferred, 1x2, auto
            monitor = \\.\display1, preferred, 3x4, auto
            """));

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 0, 0, 100, 100, 1),
            Display(@"\\.\DISPLAY2", "Samsung", 100, 0, 100, 100, 1),
        ]));

        Assert.Equal(3, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY1").X);
        Assert.Equal(4, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY1").Y);
        Assert.Equal(5, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY2").X);
        Assert.Equal(6, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY2").Y);
    }

    [Fact]
    public void NegativePositionShiftsTheWorkArea()
    {
        var core = new Core(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, -1920x-40, auto
            """));
        var display = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, new Rectangle(0, 40, 1920, 1040));

        var outcome = core.Handle(new MonitorsChanged([display]));

        var monitor = Assert.Single(outcome.State.Monitors);
        Assert.Equal(-1920, monitor.X);
        Assert.Equal(-40, monitor.Y);
        Assert.Equal(new Rectangle(-1920, 0, 1920, 1040), monitor.WorkArea);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("disabled")]
    public void DisablingAMonitorMovesItsClientsToOneThatRemains(string disabled)
    {
        var core = new Core(ConfigParser.Parse($"""
            monitor = \\.\DISPLAY2, {disabled}
            """));
        var left = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1);
        var right = Display(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1);
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
        var core = new Core(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, disable
            general:gaps_out = 0
            general:border_size = 0
            """));
        var display = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 2560, 1440, 1);
        core.Handle(new MonitorsChanged([display]));

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), display.WorkArea));

        Assert.Single(outcome.State.Clients);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), BoundsOf(outcome, 1));
    }

    [Fact]
    public void EachMonitorOwnsItsOwnLayout()
    {
        var core = new Core();
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
        var core = new Core();
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
        var core = new Core();
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
        var core = new Core();
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
        var core = new Core();
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
        Assert.Empty(document.Warnings);

        var core = new Core();
        core.Handle(new Configure(document));
        var display = Display(@"\\.\DISPLAY1", "Dell", 0, 0, 3840, 2160, 2);
        var listed = core.Handle(new MonitorsChanged([display]));
        var placed = core.Handle(new WindowAppeared(new WindowId(1), display.WorkArea));

        Assert.Equal([new DisplayModeRequest(@"\\.\DISPLAY1", "1920x1080@60")], listed.DisplayModes);
        Assert.Equal([new DisplayScaleRequest(@"\\.\DISPLAY1", 1.5)], listed.DisplayScales);
        var monitor = Assert.Single(listed.State.Monitors);
        Assert.Equal(2, monitor.Scale);
        Assert.Equal(60, monitor.X);
        Assert.Equal(80, monitor.Y);
        Assert.Equal(new Rectangle(80, 100, 3800, 2120), BoundsOf(placed, 1));
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
        var core = new Core(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, 0x0, auto
            monitor = \\.\DISPLAY2, preferred, auto-right, auto
            """));

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 0, 0, 3840, 2160, 2),
            Display(@"\\.\DISPLAY2", "LG", 100, 50, 800, 600, 1),
        ]));

        var packed = Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY2");
        Assert.Equal(1920, packed.X);
        Assert.Equal(0, packed.Y);
        Assert.Equal(3840, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY1").Width);
    }

    [Fact]
    public void AutomaticPlacementIgnoresTheReportedOrigin()
    {
        var core = new Core(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, auto-right, auto
            """));
        var display = Display(@"\\.\DISPLAY1", "Dell", 40, 50, 800, 600, 1, new Rectangle(40, 70, 800, 560));

        var outcome = core.Handle(new MonitorsChanged([display]));

        var monitor = Assert.Single(outcome.State.Monitors);
        Assert.Equal(0, monitor.X);
        Assert.Equal(0, monitor.Y);
        Assert.Equal(new Rectangle(0, 20, 800, 560), monitor.WorkArea);
    }

    [Theory]
    [InlineData("auto", 1500, 0)]
    [InlineData("auto-right", 1500, 0)]
    [InlineData("auto-left", -200, 0)]
    [InlineData("auto-down", 0, 900)]
    [InlineData("auto-up", 0, -100)]
    [InlineData("auto-center-right", 1500, 400)]
    [InlineData("auto-center-left", -200, 400)]
    [InlineData("auto-center-down", 650, 900)]
    [InlineData("auto-center-up", 650, -100)]
    public void AutomaticDirectionsPackAgainstThePlacedMonitors(string direction, int x, int y)
    {
        var core = new Core(ConfigParser.Parse($"""
            monitor = \\.\DISPLAY1, preferred, 0x0, auto
            monitor = \\.\DISPLAY2, preferred, 1000x0, auto
            monitor = \\.\DISPLAY3, preferred, {direction}, auto
            """));

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "A", 0, 0, 1000, 400, 1),
            Display(@"\\.\DISPLAY2", "B", 0, 0, 500, 900, 1),
            Display(@"\\.\DISPLAY3", "C", 9, 9, 200, 100, 1),
        ]));

        Assert.Equal(0, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY1").X);
        Assert.Equal(1000, Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY2").X);
        var packed = Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY3");
        Assert.Equal(x, packed.X);
        Assert.Equal(y, packed.Y);
    }

    [Theory]
    [InlineData("addreserved")]
    [InlineData("reserved_area")]
    public void ReservedAreaShrinksTheWorkArea(string keyword)
    {
        var core = new Core(ConfigParser.Parse($"""
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
        var core = new Core(ConfigParser.Parse("""
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
        var core = new Core(ConfigParser.Parse("""
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
        var core = new Core(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, addreserved, 1, 2, 3, 4
            monitor = \\.\DISPLAY1, preferred, 10x20, auto
            """));

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 0, 0, 100, 80, 1),
        ]));

        var monitor = Assert.Single(outcome.State.Monitors);
        Assert.Equal(10, monitor.X);
        Assert.Equal(20, monitor.Y);
        Assert.Equal(new Rectangle(13, 21, 93, 77), monitor.WorkArea);
    }

    [Theory]
    [InlineData("0", DisplayOrientation.Identity)]
    [InlineData("1", DisplayOrientation.Rotate90)]
    [InlineData("2", DisplayOrientation.Rotate180)]
    [InlineData("3", DisplayOrientation.Rotate270)]
    public void TransformZeroThroughThreeRequestsThatRotation(string transform, DisplayOrientation orientation)
    {
        var core = new Core(ConfigParser.Parse($"""
            monitor = \\.\DISPLAY1, preferred, 4x5, auto, transform, {transform}
            """));

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 0, 0, 800, 600, 1),
        ]));

        var request = Assert.Single(outcome.Orientations);
        Assert.Equal(@"\\.\DISPLAY1", request.Device);
        Assert.Equal(orientation, request.Orientation);
        Assert.Equal(orientation, Assert.Single(outcome.State.Monitors).Orientation);
        Assert.Equal(4, Assert.Single(outcome.State.Monitors).X);
    }

    [Fact]
    public void ALaterTransformLineUpdatesTheMonitor()
    {
        var core = new Core(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, 4x5, auto
            monitor = \\.\DISPLAY1, transform, 2
            """));

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 0, 0, 100, 100, 1),
        ]));

        Assert.Equal(DisplayOrientation.Rotate180, Assert.Single(outcome.Orientations).Orientation);
        Assert.Equal(4, Assert.Single(outcome.State.Monitors).X);
        Assert.Equal(5, Assert.Single(outcome.State.Monitors).Y);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("7")]
    public void FlippedTransformsWarnAndAreNotApplied(string transform)
    {
        var document = ConfigParser.Parse($"""
            monitor = \\.\DISPLAY1, preferred, 0x0, auto, transform, {transform}
            """);
        var core = new Core(document);

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 8, 9, 100, 100, 1),
        ]));

        Assert.Equal(new ConfigWarning(1, "Flipped monitor transforms are not honored."), Assert.Single(document.Warnings));
        Assert.Empty(outcome.Orientations);
        Assert.Null(Assert.Single(outcome.State.Monitors).Orientation);
        Assert.Equal(0, Assert.Single(outcome.State.Monitors).X);
    }

    [Fact]
    public void UnsupportedMonitorOptionsWarnAndThePositionStillApplies()
    {
        var document = ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, 3x4, auto, mirror, \\.\DISPLAY2, bitdepth, 10, cm, hdr, vrr, 1, transform, 5
            """);
        var core = new Core(document);

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 0, 0, 100, 100, 1),
        ]));

        Assert.Equal(
            [
                new ConfigWarning(1, "Changing a monitor's bit depth is not honored."),
                new ConfigWarning(1, "Variable refresh rate is not honored."),
                new ConfigWarning(1, "Flipped monitor transforms are not honored."),
            ],
            document.Warnings);
        Assert.Empty(outcome.Orientations);
        Assert.Equal(3, Assert.Single(outcome.State.Monitors).X);
        Assert.Equal(4, Assert.Single(outcome.State.Monitors).Y);
    }

    [Fact]
    public void DisablingAMonitorDoesNotRemoveIt()
    {
        var core = new Core(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, disable
            """));

        var outcome = core.Handle(new MonitorsChanged([
            Display(@"\\.\DISPLAY1", "Dell", 0, 0, 100, 100, 1),
        ]));

        Assert.False(outcome.State.Monitors[0].Enabled);
        Assert.Equal("FALLBACK", outcome.State.Monitors[1].Name);
        Assert.Contains(outcome.Events, static item => item is MonitorAdded);
        Assert.DoesNotContain(outcome.Events, static item => item is MonitorRemoved);
    }

    [Fact]
    public void APreferredAutomaticRuleDoesNotWarn()
    {
        var document = ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, auto, auto
            """);

        Assert.Empty(document.Warnings);
    }

    [Fact]
    public void AMalformedPositionWarnsAndIsNotApplied()
    {
        var document = ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, nope, auto
            """);
        var core = new Core(document);

        var outcome = core.Handle(new MonitorsChanged([Display(@"\\.\DISPLAY1", "Dell", 8, 9, 100, 100, 1)]));

        Assert.Equal(new ConfigWarning(1, "Malformed monitor rule."), Assert.Single(document.Warnings));
        Assert.Equal(8, Assert.Single(outcome.State.Monitors).X);
    }

    [Fact]
    public void LoadingConfigAppliesPositionToMonitorsAlreadyKnown()
    {
        var core = new Core();
        core.Handle(new MonitorsChanged([Display(@"\\.\DISPLAY1", "Dell", 0, 0, 100, 100, 1)]));

        var outcome = core.Handle(new ConfigLoaded(ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, 4x5, auto
            """)));

        var monitor = Assert.Single(outcome.State.Monitors);
        Assert.Equal(4, monitor.X);
        Assert.Equal(5, monitor.Y);
    }

    private static Monitor Display(
        string name,
        string description,
        int x,
        int y,
        int width,
        int height,
        double scale,
        Rectangle? workArea = null) =>
        new(name, description, x, y, width, height, scale, true, workArea ?? new Rectangle(x, y, width, height));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }
}
