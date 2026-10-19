using Winland;

namespace Winland.Tests;

/// <summary>A touchpad pinch picks and drives a gesture as the pinned release matches it.</summary>
public class PinchTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void APinchGestureParsesWithoutAWarning()
    {
        var document = ConfigParser.Parse("gesture = 2, pinchin, close\ngesture = 3, pinch, close\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(2, document.Gestures.Count);
    }

    [Theory]
    [InlineData("pinchin", 1.2, true)]
    [InlineData("pinchin", 0.8, false)]
    [InlineData("pinchout", 0.8, true)]
    [InlineData("pinch", 0.8, true)]
    public void SpreadingIsPinchInAndClosingIsPinchOut(string direction, double scale, bool closes)
    {
        var core = Opened($"gesture = 2, {direction}, close\n");

        var ended = Pinch(core, 2, scale);

        Assert.Equal(closes ? [new WindowId(1)] : [], ended.Closes);
    }

    [Fact]
    public void APinchSmallerThanATenthPicksNothing()
    {
        var core = Opened("gesture = 2, pinchin, close\n");

        Assert.Empty(Pinch(core, 2, 1.09).Closes);
    }

    [Fact]
    public void ASwipeDoesNotPickAPinchGesture()
    {
        var core = Opened("gesture = 3, pinch, close\n");

        core.Handle(new SwipeBegan(3));
        for (var i = 0; i < 10; i++)
            core.Handle(new SwipeUpdated(3, 0, 20));

        Assert.Empty(core.Handle(new SwipeEnded()).Closes);
    }

    private static Outcome Pinch(Core core, int fingers, double scale)
    {
        core.Handle(new PinchBegan(fingers));
        core.Handle(new PinchUpdated(fingers, 0, 0, scale));
        return core.Handle(new PinchEnded());
    }

    private static Core Opened(string config)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(config)));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        return core;
    }
}
