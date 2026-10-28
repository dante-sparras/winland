namespace Winland.Domain.Tests;

public class GroupBarStyleTests
{
    [Fact]
    public void TheCornersGrowWithTheMonitorScaleAndNotWithTheTextSize()
    {
        var style = GroupBarStyle.Documented with { Rounding = 4, GradientRounding = 6 };

        var scaled = style.ScaledBy(1.5, text: 2);

        Assert.Equal(6, scaled.Rounding);
        Assert.Equal(9, scaled.GradientRounding);
    }
}
