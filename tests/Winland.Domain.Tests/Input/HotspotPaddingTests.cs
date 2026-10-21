
namespace Winland.Domain.Tests;

public class HotspotPaddingTests
{
    private static readonly Rectangle Left = new(0, 0, 1000, 800);
    private static readonly Rectangle Right = new(1000, 0, 1000, 800);

    [Fact]
    public void APointClearOfTheEdgesStays()
    {
        Assert.Equal(new Point(500, 400), HotspotPadding.ClosestValid(new Point(500, 400), [Left], 10));
        Assert.Equal(new Point(0, 0), HotspotPadding.ClosestValid(new Point(0, 0), [Left], 0));
    }

    [Fact]
    public void APointNearAnOuterEdgeIsPushedInside()
    {
        Assert.Equal(new Point(10, 10), HotspotPadding.ClosestValid(new Point(0, 0), [Left], 10));
        Assert.Equal(new Point(989, 400), HotspotPadding.ClosestValid(new Point(999, 400), [Left], 10));
        Assert.Equal(new Point(500, 789), HotspotPadding.ClosestValid(new Point(500, 799), [Left], 10));
    }

    [Fact]
    public void TheEdgeBetweenTwoMonitorsIsNotAnOuterEdge()
    {
        Assert.Equal(new Point(999, 400), HotspotPadding.ClosestValid(new Point(999, 400), [Left, Right], 10));
        Assert.Equal(new Point(1000, 400), HotspotPadding.ClosestValid(new Point(1000, 400), [Left, Right], 10));
        Assert.Equal(new Point(1989, 10), HotspotPadding.ClosestValid(new Point(1999, 0), [Left, Right], 10));
    }

    [Fact]
    public void ThePaddingIsReadAndClamped()
    {
        Assert.Equal(5, CursorOptions.From(ConfigParser.Parse("cursor:hotspot_padding = 5\n")).Behavior().HotspotPadding);
        Assert.Equal(20, CursorOptions.From(ConfigParser.Parse("cursor:hotspot_padding = 50\n")).Behavior().HotspotPadding);
        Assert.Equal(0, CursorOptions.From(ConfigParser.Parse("")).Behavior().HotspotPadding);
        Assert.Empty(ConfigParser.Parse("cursor:hotspot_padding = 5\n").Warnings);
    }
}
