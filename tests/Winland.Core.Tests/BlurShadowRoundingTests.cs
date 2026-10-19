using Winland;

namespace Winland.Tests;

public class BlurShadowRoundingTests
{
    private static readonly Rectangle Area = new(0, 0, 800, 600);

    [Fact]
    public void DefaultConfigBlursKeepsTheShadowAndDrawsSquareCorners()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("")));

        var decoration = For(core.Handle(Appeared(1)), 1);

        Assert.True(decoration.Blur);
        Assert.True(decoration.Shadow);
        Assert.Equal(CornerPreference.DoNotRound, decoration.Corners);
        Assert.Null(decoration.Region);
    }

    [Fact]
    public void BlurAndShadowSwitchesAreHonoredWithoutAWarningAndReloadFlipsThem()
    {
        var document = ConfigParser.Parse("""
            decoration {
                blur {
                    enabled = false
                    passes = 3
                }
                shadow {
                    enabled = false
                    range = 20
                }
            }
            """);
        var core = new Core();
        core.Handle(new Configure(document));
        core.Handle(Appeared(1));

        var off = For(core.Handle(Appeared(2)), 1);

        Assert.Equal([new ConfigWarning(4, "Unsupported keyword 'decoration:blur:passes'.")], document.Warnings);
        Assert.False(off.Blur);
        Assert.False(off.Shadow);
        Assert.Null(off.DrawnShadow);

        var reloaded = core.Handle(new Configure(ConfigParser.Parse("decoration:shadow:enabled = true\n")));

        Assert.True(For(reloaded, 1).Blur);
        Assert.True(For(reloaded, 1).Shadow);
        Assert.Empty(reloaded.ResetDecorations);
    }

    [Fact]
    public void GetOptionReportsTheBlurShadowAndRoundingPower()
    {
        var core = new Core(ConfigParser.Parse("decoration:blur:enabled = no\ndecoration:rounding_power = 4\n"));

        Assert.Equal("{\"option\":\"decoration:blur:enabled\",\"bool\":false,\"set\":true}", core.Execute("getoption decoration:blur:enabled").Text);
        Assert.Equal("{\"option\":\"decoration:shadow:enabled\",\"bool\":true,\"set\":false}", core.Execute("getoption decoration:shadow:enabled").Text);
        Assert.Equal("{\"option\":\"decoration:rounding_power\",\"float\":4,\"set\":true}", core.Execute("getoption decoration:rounding_power").Text);
    }

    [Theory]
    [InlineData(0, 2f, 1.0, CornerPreference.DoNotRound, 0, 0f)]
    [InlineData(8, 2f, 1.0, CornerPreference.Round, 0, 0f)]
    [InlineData(8, 2f, 2.0, CornerPreference.Round, 0, 0f)]
    [InlineData(4, 2f, 1.5, CornerPreference.RoundSmall, 0, 0f)]
    [InlineData(10, 2f, 1.0, CornerPreference.DoNotRound, 10, 2f)]
    [InlineData(10, 2f, 2.0, CornerPreference.DoNotRound, 20, 2f)]
    [InlineData(8, 4f, 1.0, CornerPreference.DoNotRound, 16, 4f)]
    [InlineData(5, 2f, 1.5, CornerPreference.DoNotRound, 8, 2f)]
    [InlineData(4, 1f, 1.0, CornerPreference.DoNotRound, 2, 1f)]
    [InlineData(6, 2f, double.NaN, CornerPreference.DoNotRound, 6, 2f)]
    public void DwmCornersAreUsedOnlyForTheirOwnRadiusAndAnyOtherIsCut(
        int rounding,
        float power,
        double scale,
        CornerPreference preference,
        int radius,
        float regionPower)
    {
        var choice = CornerShape.For(rounding, power, scale);

        Assert.Equal(preference, choice.Preference);
        Assert.Equal(radius == 0 ? null : new CornerRegion(radius, regionPower), choice.Region);
    }

    [Fact]
    public void ARadiusThatRoundsToNothingIsSquare()
    {
        Assert.Equal(CornerChoice.Square, CornerShape.For(1, 1f, 0.5));
    }

    [Fact]
    public void AnExactRadiusIsCutAtTheClientsMonitorScale()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("decoration:rounding = 10\n")));
        var scaled = new Monitor("DISPLAY1", "DISPLAY1", 0, 0, 1600, 1200, 2, true, new Rectangle(0, 0, 1600, 1200));
        core.Handle(new MonitorsChanged([scaled]));

        var decoration = For(core.Handle(new WindowAppeared(new WindowId(1), scaled.WorkArea)), 1);

        Assert.Equal(CornerPreference.DoNotRound, decoration.Corners);
        Assert.Equal(new CornerRegion(20, 2f), decoration.Region);
    }

    [Fact]
    public void FullscreenAndAWorkspaceRuleWithoutRoundingCutNoCorners()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("decoration:rounding = 10\nworkspace = 2, rounding:0\n")));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        var entered = core.Handle(new Dispatch("fullscreen", "0"));

        Assert.Null(For(entered, 2).Region);
        Assert.Equal(CornerPreference.DoNotRound, For(entered, 2).Corners);
        Assert.Equal(new CornerRegion(10, 2f), For(entered, 1).Region);

        core.Handle(new Dispatch("fullscreen", "0"));
        var moved = core.Execute("dispatch movetoworkspace 2").Effect;

        Assert.Null(For(moved, 2).Region);
        Assert.Equal(new CornerRegion(10, 2f), For(moved, 1).Region);
    }

    [Fact]
    public void ACircularOutlineStaysInsideTheFrameAndTouchesEveryEdge()
    {
        var outline = CornerShape.Outline(100, 60, new CornerRegion(10, 2f));

        Assert.Equal(new Point(0, 10), outline[0]);
        Assert.Contains(new Point(10, 0), outline);
        Assert.Contains(new Point(90, 0), outline);
        Assert.Contains(new Point(100, 50), outline);
        Assert.Contains(new Point(10, 60), outline);
        Assert.All(outline, point => Assert.InRange(point.X, 0, 100));
        Assert.All(outline, point => Assert.InRange(point.Y, 0, 60));
        Assert.Equal(outline.Count, outline.Distinct().Count());
        Assert.DoesNotContain(new Point(0, 0), outline);
        var mid = Math.Round(10 - (10 / Math.Sqrt(2)), MidpointRounding.AwayFromZero);
        Assert.Contains(outline, point => Math.Abs(point.X - mid) <= 1 && Math.Abs(point.Y - mid) <= 1);
    }

    [Fact]
    public void PowerOneCutsAStraightCornerAndALargerPowerIsSquarer()
    {
        var straight = CornerShape.Outline(40, 40, new CornerRegion(10, 1f));
        var squarer = CornerShape.Outline(40, 40, new CornerRegion(10, 4f));
        var circle = CornerShape.Outline(40, 40, new CornerRegion(10, 2f));

        var corner = straight.Where(point => point.X <= 10 && point.Y <= 10).ToList();
        Assert.All(corner, point => Assert.InRange(point.X + point.Y, 9, 11));
        Assert.True(Nearest(squarer) < Nearest(circle));
        Assert.True(Nearest(circle) < Nearest(straight));
    }

    [Fact]
    public void ARadiusIsCutToHalfTheShorterSideAndNothingIsTracedForAnEmptyFrame()
    {
        var pill = CornerShape.Outline(100, 20, new CornerRegion(50, 2f));

        Assert.Equal(new Point(0, 10), pill[0]);
        Assert.Contains(new Point(10, 0), pill);
        Assert.Contains(new Point(90, 0), pill);
        Assert.Empty(CornerShape.Outline(0, 20, new CornerRegion(5, 2f)));
        Assert.Equal(
            [new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1)],
            CornerShape.Outline(1, 1, new CornerRegion(5, 2f)));
    }

    [Fact]
    public void GetOptionReportsTheRoundingInEffectAfterANegativeValue()
    {
        var core = new Core(ConfigParser.Parse("decoration:rounding = 12\ndecoration:rounding = -3\n"));

        Assert.Contains("\"int\":12", core.Execute("getoption decoration:rounding").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void GetOptionReportsTheDragThresholdInEffectAfterAnUnparsableValue()
    {
        var core = new Core(ConfigParser.Parse("binds:drag_threshold = 10\nbinds:drag_threshold = no\n"));

        Assert.Contains("\"int\":10", core.Execute("getoption binds:drag_threshold").Text, StringComparison.Ordinal);
    }

    // How close the top left corner of the outline comes to the frame's own corner.
    private static double Nearest(IReadOnlyList<Point> outline) =>
        outline.Min(point => Math.Sqrt((point.X * point.X) + (point.Y * point.Y)));

    private static WindowAppeared Appeared(long window) => new(new WindowId(window), Area);

    private static Decoration For(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        return Assert.Single(outcome.Decorations, decoration => decoration.Client == client.Id);
    }
}
