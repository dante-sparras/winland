
namespace Winland.Domain.Tests;

public class StandInMapTests
{
    private static readonly FrameInsets Shadow = new(7, 0, 7, 8);

    [Fact]
    public void APaintedWindowIsStretchedOverTheFrame()
    {
        var map = StandInMap.For(new Rectangle(50, 40, 200, 100), new Size(400, 200), Shadow, new Size(414, 208));

        Assert.Equal(new Rectangle(7, 0, 400, 200), map.Source);
        Assert.Equal(new Rectangle(0, 0, 200, 100), map.Destination);
    }

    [Fact]
    public void AWindowNotPaintedAtItsSizeYetIsScaledByTheFramesFactor()
    {
        // Painted 200x100 of a 400x200 window; the frame is 300x150 of it, so the picture is 150x75.
        var map = StandInMap.For(new Rectangle(0, 0, 300, 150), new Size(400, 200), Shadow, new Size(214, 108));

        Assert.Equal(new Rectangle(7, 0, 200, 100), map.Source);
        Assert.Equal(new Rectangle(0, 0, 150, 75), map.Destination);
    }

    [Fact]
    public void AnOversizedWindowIsSqueezedIntoTheFrame()
    {
        var map = StandInMap.For(new Rectangle(0, 0, 100, 50), new Size(400, 200), FrameInsets.None, new Size(800, 400));

        Assert.Equal(new Rectangle(0, 0, 800, 400), map.Source);
        Assert.Equal(new Rectangle(0, 0, 100, 50), map.Destination);
    }

    [Fact]
    public void ADifferenceOfOnePixelCountsAsPainted()
    {
        var map = StandInMap.For(new Rectangle(0, 0, 100, 100), new Size(400, 200), FrameInsets.None, new Size(399, 199));

        Assert.Equal(new Rectangle(0, 0, 100, 100), map.Destination);
    }

    [Fact]
    public void AnUnknownPaintedSizeIsTheSizeTheWindowWasGiven()
    {
        var map = StandInMap.For(new Rectangle(0, 0, 40, 20), new Size(400, 200), Shadow, null);

        Assert.Equal(new Rectangle(7, 0, 400, 200), map.Source);
        Assert.Equal(new Rectangle(0, 0, 40, 20), map.Destination);
    }

    [Fact]
    public void AFlatFrameKeepsAPixelSoItsDestinationIsValid()
    {
        var map = StandInMap.For(new Rectangle(0, 100, 400, 0), new Size(400, 200), FrameInsets.None, null);

        Assert.Equal(new Rectangle(0, 0, 400, 1), map.Destination);
    }
}
