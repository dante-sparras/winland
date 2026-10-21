
namespace Winland.Domain.Tests;

public class BarShapesTests
{
    private static readonly BorderColor Red = new(0xFF, 0xFF, 0, 0);
    private static readonly BorderColor Blue = new(0xFF, 0, 0, 0xFF);

    [Fact]
    public void PriorityIsKeptWithoutAWarning()
    {
        Assert.Equal(3, GroupOptions.From(ConfigParser.Parse("")).Bar.Priority);
        var document = ConfigParser.Parse("group:groupbar:priority = 5\ngroup:groupbar:priority = 9\n");
        Assert.Equal(6, GroupOptions.From(document).Bar.Priority);
        Assert.Empty(document.Warnings);
    }

    [Fact]
    public void ShapeKeysAreHonoredWithTheirDefaults()
    {
        var plain = GroupOptions.From(ConfigParser.Parse("")).Bar;
        Assert.False(plain.Gradients);
        Assert.Equal(1, plain.Rounding);
        Assert.Equal(2, plain.RoundingPower);
        Assert.True(plain.RoundOnlyEdges);
        Assert.Equal(2, plain.GradientRounding);
        Assert.Equal(2, plain.GradientRoundingPower);
        Assert.True(plain.GradientRoundOnlyEdges);

        var set = GroupOptions.From(ConfigParser.Parse("""
            group {
                groupbar {
                    gradients = true
                    rounding = 50
                    rounding_power = 0.5
                    round_only_edges = false
                    gradient_rounding = 4
                    gradient_rounding_power = 3
                    gradient_round_only_edges = false
                    render_titles = false
                    col.active = rgba(ff0000ff) rgba(0000ffff) 45deg
                }
            }
            """)).Bar;
        Assert.True(set.Gradients);
        Assert.Equal(40, set.Rounding);
        Assert.Equal(1, set.RoundingPower);
        Assert.False(set.RoundOnlyEdges);
        Assert.Equal(4, set.GradientRounding);
        Assert.Equal(3, set.GradientRoundingPower);
        Assert.False(set.GradientRoundOnlyEdges);
        Assert.Equal(Red, set.Active);
        Assert.Equal([Red, Blue], set.GradientColors(active: true, locked: false));
        Assert.Equal([set.Inactive], set.GradientColors(active: false, locked: false));

        // The title row stays for the gradient with titles off.
        Assert.Equal(set.Height, set.Reserved - ((set.GapsOut * 2) + set.IndicatorHeight + set.IndicatorGap));
    }

    [Fact]
    public void OnlyTheOuterCornersOfTheOuterTabsAreRound()
    {
        Assert.Equal(Corners.All, BarShapes.Rounded(0, 1, 2, onlyEdges: true));
        Assert.Equal(Corners.TopLeft | Corners.BottomLeft, BarShapes.Rounded(0, 3, 2, onlyEdges: true));
        Assert.Equal(Corners.None, BarShapes.Rounded(1, 3, 2, onlyEdges: true));
        Assert.Equal(Corners.TopRight | Corners.BottomRight, BarShapes.Rounded(2, 3, 2, onlyEdges: true));
        Assert.Equal(Corners.All, BarShapes.Rounded(1, 3, 2, onlyEdges: false));
        Assert.Equal(Corners.None, BarShapes.Rounded(0, 1, 0, onlyEdges: false));
    }

    [Fact]
    public void CoverageCutsOnlyTheRoundCorners()
    {
        var box = new Rectangle(10, 10, 20, 10);

        Assert.Equal(0, BarShapes.Coverage(9, 10, box, 4, 2, Corners.All));
        Assert.Equal(1, BarShapes.Coverage(20, 15, box, 4, 2, Corners.All));
        Assert.Equal(1, BarShapes.Coverage(10, 10, box, 0, 2, Corners.All));
        Assert.Equal(1, BarShapes.Coverage(10, 10, box, 4, 2, Corners.TopRight));

        var corner = BarShapes.Coverage(10, 10, box, 4, 2, Corners.All);
        Assert.InRange(corner, 0, 0.25);
        Assert.Equal(corner, BarShapes.Coverage(29, 19, box, 4, 2, Corners.All));

        // A higher power is squarer, so it covers more of the corner pixel.
        Assert.True(BarShapes.Coverage(10, 10, box, 4, 6, Corners.All) > corner);
    }

    [Fact]
    public void RadiusIsAtMostHalfTheShorterSide()
    {
        var thin = new Rectangle(0, 0, 40, 4);

        Assert.Equal(1, BarShapes.Coverage(20, 0, thin, 40, 2, Corners.All));
        Assert.True(BarShapes.Coverage(0, 0, thin, 40, 2, Corners.All) < 1);
    }

    [Fact]
    public void GradientRunsFromTheLastColorAtTheTopToTheFirstAtTheBottom()
    {
        Assert.Equal(Red, BarShapes.GradientAt([Red], 0));
        Assert.Equal(Red, BarShapes.GradientAt([Red], 1));

        // Two colors stop at 2/3 (the first) and 1/3 (the second).
        Assert.Equal(Blue, BarShapes.GradientAt([Red, Blue], 0.2));
        Assert.Equal(Red, BarShapes.GradientAt([Red, Blue], 0.9));
        Assert.Equal(new BorderColor(0xFF, 64, 0, 191), BarShapes.GradientAt([Red, Blue], 5.0 / 12));
    }
}
