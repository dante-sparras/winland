namespace Winland.Application.Tests;

public class BlurShadowRoundingTests
{
    private static readonly Rectangle Area = new(0, 0, 800, 600);

    [Fact]
    public void DefaultConfigBlursAndUsesRoundCorners()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("")));

        var decoration = For(core.Handle(Appeared(1, Area)), 1);

        Assert.True(decoration.Blur);
        Assert.Equal(CornerPreference.Round, decoration.Corners);
        Assert.Null(decoration.Region);
    }

    [Fact]
    public void TheBlurSwitchIsHonoredAndTheOtherBlurAndShadowKeywordsWarn()
    {
        var document = ConfigParser.Parse("""
            decoration {
                blur {
                    enabled = false
                    popups = true
                }
                shadow {
                    enabled = false
                    range = 20
                }
            }
            """);
        var core = new WindowManager();
        core.Handle(new Configure(document));
        core.Handle(Appeared(1, Area));

        var off = For(core.Handle(Appeared(2, Area)), 1);

        Assert.Equal(
            [
                new ConfigWarning(4, "Unsupported keyword 'decoration:blur:popups'."),
                new ConfigWarning(7, "Unsupported keyword 'decoration:shadow:enabled'."),
                new ConfigWarning(8, "Unsupported keyword 'decoration:shadow:range'."),
            ],
            document.Warnings);
        Assert.False(off.Blur);

        var reloaded = core.Handle(new Configure(ConfigParser.Parse("decoration:blur:enabled = true\n")));

        Assert.True(For(reloaded, 1).Blur);
        Assert.Empty(reloaded.ResetDecorations);
    }

    [Fact]
    public void GetOptionReportsTheBlur()
    {
        var core = new WindowManager(ConfigParser.Parse("decoration:blur:enabled = no\n"));

        Assert.Equal("{\"option\":\"decoration:blur:enabled\",\"bool\":false,\"set\":true}", core.Execute("getoption decoration:blur:enabled").Text);
    }

    [Theory]
    [InlineData(0, 1.0, CornerPreference.DoNotRound, 0)]
    [InlineData(8, 1.0, CornerPreference.Round, 0)]
    [InlineData(8, 2.0, CornerPreference.Round, 0)]
    [InlineData(4, 1.5, CornerPreference.RoundSmall, 0)]
    [InlineData(10, 1.0, CornerPreference.DoNotRound, 10)]
    [InlineData(10, 2.0, CornerPreference.DoNotRound, 20)]
    [InlineData(5, 1.5, CornerPreference.DoNotRound, 8)]
    [InlineData(6, double.NaN, CornerPreference.DoNotRound, 6)]
    public void DwmCornersAreUsedOnlyForTheirOwnRadiusAndAnyOtherIsCut(
        int rounding,
        double scale,
        CornerPreference preference,
        int radius)
    {
        var choice = CornerShape.For(rounding, scale);

        Assert.Equal(preference, choice.Preference);
        Assert.Equal(radius == 0 ? null : new CornerRegion(radius), choice.Region);
    }

    [Fact]
    public void ARadiusThatRoundsToNothingIsSquare()
    {
        Assert.Equal(CornerChoice.Square, CornerShape.For(1, 0.2));
    }

    [Fact]
    public void AnExactRadiusIsCutAtTheClientsMonitorScale()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("decoration:rounding = 10\n")));
        var scaled = new Monitor("DISPLAY1", "DISPLAY1", 0, 0, 1600, 1200, 2, true, new Rectangle(0, 0, 1600, 1200));
        core.Handle(new MonitorsChanged([scaled]));

        var decoration = For(core.Handle(new WindowAppeared(new WindowId(1), scaled.WorkArea)), 1);

        Assert.Equal(CornerPreference.DoNotRound, decoration.Corners);
        Assert.Equal(new CornerRegion(20), decoration.Region);
    }

    [Fact]
    public void ACircularOutlineStaysInsideTheFrameAndTouchesEveryEdge()
    {
        var outline = CornerShape.Outline(100, 60, new CornerRegion(10));

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
    public void ARadiusIsCutToHalfTheShorterSideAndNothingIsTracedForAnEmptyFrame()
    {
        var pill = CornerShape.Outline(100, 20, new CornerRegion(50));

        Assert.Equal(new Point(0, 10), pill[0]);
        Assert.Contains(new Point(10, 0), pill);
        Assert.Contains(new Point(90, 0), pill);
        Assert.Empty(CornerShape.Outline(0, 20, new CornerRegion(5)));
        Assert.Equal(
            [new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1)],
            CornerShape.Outline(1, 1, new CornerRegion(5)));
    }

    [Fact]
    public void GetOptionReportsTheRoundingInEffectAfterANegativeValue()
    {
        var core = new WindowManager(ConfigParser.Parse("decoration:rounding = 12\ndecoration:rounding = -3\n"));

        Assert.Contains("\"int\":12", core.Execute("getoption decoration:rounding").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void GetOptionReportsTheDragThresholdInEffectAfterAnUnparsableValue()
    {
        var core = new WindowManager(ConfigParser.Parse("binds:drag_threshold = 10\nbinds:drag_threshold = no\n"));

        Assert.Contains("\"int\":10", core.Execute("getoption binds:drag_threshold").Text, StringComparison.Ordinal);
    }

    private static Decoration For(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        return Assert.Single(outcome.Decorations, decoration => decoration.Client == client.Id);
    }
}
