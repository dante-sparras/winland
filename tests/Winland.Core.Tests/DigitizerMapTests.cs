using Winland;

namespace Winland.Tests;

public class DigitizerMapTests
{
    private static readonly Rectangle Screen = new(0, 0, 1000, 1000);
    private static readonly DigitizerSample Full = new(Screen, 0, 0);

    [Fact]
    public void ThePenAndTouchKeysAreReadAndClamped()
    {
        var document = ConfigParser.Parse(
            "input:tablet:enabled = false\n"
            + "input:tablet:transform = 9\n"
            + "input:tablet:left_handed = true\n"
            + "input:tablet:output = current\n"
            + "input:tablet:region_position = -90000 90000\n"
            + "input:tablet:region_size = -500 9000\n"
            + "input:tablet:absolute_region_position = yes\n"
            + "input:tablet:relative_input = on\n"
            + "input:tablet:active_area_position = -5 40\n"
            + "input:tablet:active_area_size = 600 50\n"
            + "input:touchdevice:enabled = 0\n"
            + "input:touchdevice:output = [[Auto]]\n"
            + "input:touchdevice:transform = -3\n"
            + "input:tablet:transform = no\n");

        Assert.Empty(document.Warnings);
        var options = DigitizerOptions.From(document);
        Assert.False(options.Tablet.Enabled);
        Assert.Equal(6, options.Tablet.Transform);
        Assert.True(options.Tablet.LeftHanded);
        Assert.Equal("current", options.Tablet.Output);
        Assert.Equal(new Vec2(-20000, 20000), options.Tablet.RegionPosition);
        Assert.Equal(new Vec2(-100, 4000), options.Tablet.RegionSize);
        Assert.True(options.Tablet.AbsoluteRegion);
        Assert.True(options.Tablet.Relative);
        Assert.Equal(new Vec2(0, 40), options.Tablet.ActiveAreaPosition);
        Assert.Equal(new Vec2(500, 50), options.Tablet.ActiveAreaSize);
        Assert.False(options.Touch.Enabled);
        Assert.Equal("[[Auto]]", options.Touch.Output);
        Assert.Equal(0, options.Touch.Transform);
    }

    [Fact]
    public void ABadValueLeavesThePreviousOne()
    {
        var options = DigitizerOptions.From(ConfigParser.Parse(
            "input:tablet:transform = 2\ninput:tablet:transform = sideways\ninput:tablet:region_position = 10 20 30\n"));

        Assert.Equal(2, options.Tablet.Transform);
        Assert.Equal(new Vec2(0, 0), options.Tablet.RegionPosition);
    }

    [Theory]
    [InlineData(0, 0.2, 0.3, 0.2, 0.3)]
    [InlineData(1, 0.2, 0.3, 0.7, 0.2)]
    [InlineData(2, 0.2, 0.3, 0.8, 0.7)]
    [InlineData(3, 0.2, 0.3, 0.3, 0.8)]
    [InlineData(4, 0.2, 0.3, 0.8, 0.3)]
    [InlineData(5, 0.2, 0.3, 0.3, 0.2)]
    [InlineData(6, 0.2, 0.3, 0.2, 0.7)]
    public void TransformTurnsThePoint(int transform, double x, double y, double turnedX, double turnedY)
    {
        Assert.Equal((turnedX, turnedY), DigitizerMap.Calibrate(x, y, transform, false));
    }

    [Fact]
    public void LeftHandedTurnsTheMappedPointAHalfTurnMore()
    {
        Assert.Equal((0.25, 0.5), DigitizerMap.Calibrate(0.25, 0.5, 2, true));
        Assert.Equal((0.75, 0.5), DigitizerMap.Calibrate(0.25, 0.5, 0, true));
    }

    [Fact]
    public void TheActiveAreaUsesMillimetresAndAnOddTransformSwapsTheAxes()
    {
        var position = new Vec2(0, 0);
        var size = new Vec2(100, 100);

        Assert.Equal((0.5, 0.5), DigitizerMap.Active(0.25, 0.5, position, size, 200, 100, 0));
        Assert.Equal((0.25, 0.5), DigitizerMap.Active(0.25, 0.5, position, size, 0, 0, 0));
        Assert.Equal((0.25, 0.5), DigitizerMap.Active(0.25, 0.5, position, new Vec2(0, 100), 200, 100, 0));

        var swapped = DigitizerMap.Active(0.5, 0.25, position, size, 200, 100, 1);
        Assert.Equal(0.5, swapped.X, 6);
        Assert.Equal(0.5, swapped.Y, 6);
    }

    [Fact]
    public void APenMapsOntoTheNamedMonitor()
    {
        var monitors = new[]
        {
            new DigitizerMonitor("DISPLAY1", "Dell", 0, 0, 1000, 1000),
            new DigitizerMonitor("DISPLAY2", "Dell", 1000, 0, 1000, 1000),
        };
        var cursor = new DigitizerCursor();

        var moved = cursor.Move(DigitizerKind.Pen, Space(new TabletSettings { Output = "DISPLAY2" }, monitors), Full, 100, 200);

        Assert.Equal(new DigitizerOutcome(true, 1100, 200, true), moved);
    }

    [Fact]
    public void TransformAndTheRegionAndTheActiveAreaMoveThePen()
    {
        var monitors = new[] { new DigitizerMonitor("DISPLAY1", "", 0, 0, 1000, 1000) };
        var cursor = new DigitizerCursor();
        var turned = cursor.Move(DigitizerKind.Pen, Space(new TabletSettings { Transform = 2 }, monitors), Full, 100, 200);
        Assert.Equal(new DigitizerOutcome(true, 900, 800, true), turned);

        var region = new TabletSettings { Output = "DISPLAY1", RegionPosition = new Vec2(10, 20), RegionSize = new Vec2(100, 100) };
        var boxed = cursor.Move(DigitizerKind.Pen, Space(region, monitors), Full, 500, 500);
        Assert.Equal(new DigitizerOutcome(true, 60, 70, true), boxed);

        var sample = new DigitizerSample(Screen, 200, 100);
        var area = new TabletSettings { ActiveAreaPosition = new Vec2(0, 0), ActiveAreaSize = new Vec2(100, 100) };
        var narrowed = cursor.Move(DigitizerKind.Pen, Space(area, monitors), sample, 250, 500);
        Assert.Equal(new DigitizerOutcome(true, 500, 500, true), narrowed);
    }

    [Fact]
    public void AnUnboundPenUsesTheLayoutAndAnAbsoluteOrigin()
    {
        var monitors = new[]
        {
            new DigitizerMonitor("DISPLAY1", "", 0, 0, 1000, 1000),
            new DigitizerMonitor("DISPLAY2", "", 1000, 0, 1000, 1000),
        };
        var cursor = new DigitizerCursor();
        var absolute = new TabletSettings { AbsoluteRegion = true, RegionPosition = new Vec2(500, 0) };

        Assert.Equal(new DigitizerOutcome(true, 500, 0, true), cursor.Move(DigitizerKind.Pen, Space(absolute, monitors), Full, 0, 0));
        var empty = new TabletSettings { Output = "[[EMPTY]]", AbsoluteRegion = true, RegionPosition = new Vec2(500, 0) };
        Assert.Equal(new DigitizerOutcome(true, 500, 0, true), cursor.Move(DigitizerKind.Pen, Space(empty, monitors), Full, 0, 0));
    }

    [Fact]
    public void AbsoluteZeroDiffersWhenTheLayoutDoesNotStartAtZero()
    {
        var monitors = new[] { new DigitizerMonitor("DISPLAY1", "", 100, 0, 1000, 1000) };
        var display = new Rectangle(100, 0, 1000, 1000);
        var cursor = new DigitizerCursor();
        var plain = new TabletSettings();
        Assert.Equal(DigitizerOutcome.Leave, cursor.Move(DigitizerKind.Pen, Space(plain, monitors), new DigitizerSample(display, 0, 0), 200, 100));

        var absolute = new TabletSettings { AbsoluteRegion = true };
        Assert.Equal(new DigitizerOutcome(true, 100, 100, true), cursor.Move(DigitizerKind.Pen, Space(absolute, monitors), new DigitizerSample(display, 0, 0), 200, 100));
    }

    [Fact]
    public void ABoundOutputIgnoresTheAbsoluteOriginAndAnUnknownNameUsesTheLayout()
    {
        var monitors = new[] { new DigitizerMonitor("DISPLAY1", "Dell U2720", 100, 0, 1000, 1000) };
        var display = new Rectangle(100, 0, 1000, 1000);
        var sample = new DigitizerSample(display, 0, 0);
        var cursor = new DigitizerCursor();
        var bound = new TabletSettings { Output = "desc:Dell", AbsoluteRegion = true, RegionPosition = new Vec2(10, 0) };

        Assert.Equal(new DigitizerOutcome(true, 110, 0, true), cursor.Move(DigitizerKind.Pen, Space(bound, monitors), sample, 100, 0));

        var shifted = new[]
        {
            new DigitizerMonitor("DISPLAY1", "", 0, 0, 100, 100),
            new DigitizerMonitor("DISPLAY2", "", 100, 0, 100, 100),
        };
        var unknown = cursor.Move(
            DigitizerKind.Pen,
            Space(new TabletSettings { Output = "missing" }, shifted),
            new DigitizerSample(new Rectangle(0, 0, 100, 100), 0, 0),
            50,
            50);
        Assert.Equal(new DigitizerOutcome(true, 100, 50, true), unknown);
    }

    [Fact]
    public void CurrentFollowsTheFocusedMonitor()
    {
        var monitors = new[]
        {
            new DigitizerMonitor("DISPLAY1", "", 0, 0, 1000, 1000),
            new DigitizerMonitor("DISPLAY2", "", 1000, 0, 1000, 1000),
        };
        var cursor = new DigitizerCursor();
        var space = new DigitizerSpace(new DigitizerOptions { Tablet = new TabletSettings { Output = "current" } }, monitors, "DISPLAY2");

        Assert.Equal(new DigitizerOutcome(true, 1100, 200, true), cursor.Move(DigitizerKind.Pen, space, Full, 100, 200));
    }

    [Fact]
    public void TheDefaultsLeaveTheCursorWhereWindowsPutIt()
    {
        var monitors = new[] { new DigitizerMonitor("DISPLAY1", "", 0, 0, 1000, 1000) };
        var cursor = new DigitizerCursor();

        Assert.Equal(DigitizerOutcome.Leave, cursor.Move(DigitizerKind.Pen, Space(new TabletSettings(), monitors), Full, 100, 200));
        Assert.Equal(DigitizerOutcome.Leave, cursor.Move(DigitizerKind.Touch, Space(new TabletSettings(), monitors, new TouchSettings { Output = "[[Auto]]" }), Full, 100, 200));
        Assert.Equal(DigitizerOutcome.Leave, cursor.Button(DigitizerKind.Pen, Space(new TabletSettings(), monitors), Full, 100, 200));
    }

    [Fact]
    public void ADisabledPenIsSwallowedAndTheCursorReturnsToTheMouse()
    {
        var monitors = new[] { new DigitizerMonitor("DISPLAY1", "", 0, 0, 1000, 1000) };
        var cursor = new DigitizerCursor();
        cursor.Note(5, 6);
        var space = Space(new TabletSettings { Enabled = false }, monitors);

        Assert.Equal(new DigitizerOutcome(true, 5, 6, false), cursor.Move(DigitizerKind.Pen, space, Full, 100, 200));
        Assert.Equal(new DigitizerOutcome(true, 5, 6, false), cursor.Button(DigitizerKind.Pen, space, Full, 100, 200));
    }

    [Fact]
    public void RelativeMotionIsTheChangeInTheMappedPixel()
    {
        var monitors = new[] { new DigitizerMonitor("DISPLAY1", "", 0, 0, 1000, 1000) };
        var cursor = new DigitizerCursor();
        cursor.Note(10, 10);
        var space = Space(new TabletSettings { Relative = true, Transform = 2 }, monitors);

        // The first sample has not moved yet, so the cursor stays where the mouse left it.
        Assert.Equal(new DigitizerOutcome(true, 10, 10, true), cursor.Move(DigitizerKind.Pen, space, Full, 100, 200));
        Assert.Equal(new DigitizerOutcome(true, 0, 10, true), cursor.Move(DigitizerKind.Pen, space, Full, 110, 200));
        Assert.Equal(new DigitizerOutcome(true, 0, 10, true), cursor.Button(DigitizerKind.Pen, space, Full, 110, 200));
        Assert.Equal(DigitizerOutcome.Leave, cursor.Button(DigitizerKind.Pen, space, Full, 0, 10));
    }

    [Fact]
    public void AMouseClearsTheRelativeStrokeAndInjectedInputDoesNot()
    {
        var monitors = new[] { new DigitizerMonitor("DISPLAY1", "", 0, 0, 1000, 1000) };
        var cursor = new DigitizerCursor();
        var space = Space(new TabletSettings { Relative = true }, monitors);
        cursor.Note(10, 10);
        Assert.Equal(DigitizerOutcome.Leave, cursor.Move(DigitizerKind.Pen, space, Full, 100, 200));
        Assert.Equal(DigitizerOutcome.Leave, cursor.Move(DigitizerKind.Other, space, Full, 400, 400));
        Assert.Equal(new DigitizerOutcome(true, 110, 200, true), cursor.Move(DigitizerKind.Pen, space, Full, 110, 200));

        cursor.Move(DigitizerKind.Mouse, space, Full, 3, 4);
        Assert.Equal(DigitizerOutcome.Leave, cursor.Move(DigitizerKind.Pen, space, Full, 110, 200));
    }

    [Fact]
    public void ATouchTransformStaysOnWindowsDisplayAndAnOutputMovesIt()
    {
        var monitors = new[]
        {
            new DigitizerMonitor("DISPLAY1", "", 0, 0, 1000, 1000),
            new DigitizerMonitor("DISPLAY2", "", 1000, 0, 1000, 1000),
        };
        var cursor = new DigitizerCursor();
        var turned = Space(new TabletSettings(), monitors, new TouchSettings { Transform = 4 });
        Assert.Equal(new DigitizerOutcome(true, 900, 200, true), cursor.Move(DigitizerKind.Touch, turned, Full, 100, 200));

        var bound = Space(new TabletSettings(), monitors, new TouchSettings { Output = "DISPLAY2" });
        Assert.Equal(new DigitizerOutcome(true, 1100, 200, true), cursor.Move(DigitizerKind.Touch, bound, Full, 100, 200));
        Assert.Equal(new DigitizerOutcome(true, 900, 200, true), cursor.Button(DigitizerKind.Touch, turned, Full, 100, 200));
    }

    [Fact]
    public void APenMappingIsPublishedForTheHook()
    {
        var core = new Core();
        core.Handle(new MonitorsChanged([
            new Monitor("DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1080)),
            new Monitor("DISPLAY2", "", 1920, 0, 1920, 1080, 1, false, new Rectangle(1920, 0, 1920, 1080)),
        ]));
        core.Handle(new Configure(ConfigParser.Parse("input:tablet:transform = 2\ninput:tablet:output = DISPLAY1\n")));

        Assert.Equal(2, core.Digitizer.Options.Tablet.Transform);
        Assert.Equal("DISPLAY1", core.Digitizer.Focused);
        var monitor = Assert.Single(core.Digitizer.Monitors);
        Assert.Equal("DISPLAY1", monitor.Name);
        Assert.Equal("Dell", monitor.Description);
    }

    private static DigitizerSpace Space(TabletSettings tablet, DigitizerMonitor[] monitors, TouchSettings? touch = null) =>
        new(new DigitizerOptions { Tablet = tablet, Touch = touch ?? new TouchSettings() }, monitors, monitors.Length == 0 ? "" : monitors[0].Name);
}
