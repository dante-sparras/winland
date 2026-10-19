using Winland;

namespace Winland.Tests;

public class PointerRotationTests
{
    [Fact]
    public void AQuarterTurnClockwiseSendsRightDown()
    {
        var rotation = new PointerRotation();

        Assert.Equal((0, 10), rotation.Turn(10, 0, 90));
        Assert.Equal((-10, 0), rotation.Turn(0, 10, 90));
        Assert.Equal((-10, -5), rotation.Turn(10, 5, 180));
        Assert.Equal((7, 3), rotation.Turn(7, 3, 0));
    }

    [Fact]
    public void SlowMovesAddUpInsteadOfRoundingAway()
    {
        var rotation = new PointerRotation();
        var x = 0;
        var y = 0;
        for (var i = 0; i < 100; i++)
        {
            var (dx, dy) = rotation.Turn(1, 0, 30);
            x += dx;
            y += dy;
        }

        Assert.InRange(x, 86, 87);
        Assert.InRange(y, 49, 51);
    }

    [Fact]
    public void TheRotationIsReadAndClamped()
    {
        Assert.Equal(90, InputOptions.From(ConfigParser.Parse("input:rotation = 90\n")).Rotation);
        Assert.Equal(359, InputOptions.From(ConfigParser.Parse("input:rotation = 400\n")).Rotation);
        Assert.Empty(ConfigParser.Parse("input:rotation = 90\n").Warnings);
    }
}
