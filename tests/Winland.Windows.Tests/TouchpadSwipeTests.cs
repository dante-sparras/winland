using Winland;
using Winland.Windows;

namespace Winland.Windows.Tests;

public class TouchpadSwipeTests
{
    [Fact]
    public void ThreeFingersStartASwipeAndTheirMeanMovementUpdatesIt()
    {
        var swipe = new TouchpadSwipe();

        var began = swipe.Report(3, Fingers((0, 10, 10), (1, 20, 10), (2, 30, 10)));
        var moved = swipe.Report(3, Fingers((0, 8, 11), (1, 18, 11), (2, 28, 11)));

        Assert.Equal([new SwipeBegan(3)], began);
        var update = Assert.IsType<SwipeUpdated>(Assert.Single(moved));
        Assert.Equal(3, update.Fingers);
        Assert.Equal(-2 * TouchpadSwipe.PixelsPerMillimetre, update.DeltaX, 6);
        Assert.Equal(TouchpadSwipe.PixelsPerMillimetre, update.DeltaY, 6);
    }

    [Fact]
    public void TwoFingersAreScrollNotASwipe()
    {
        var swipe = new TouchpadSwipe();

        Assert.Empty(swipe.Report(2, Fingers((0, 10, 10), (1, 20, 10))));
        Assert.Empty(swipe.Report(2, Fingers((0, 5, 10), (1, 15, 10))));
    }

    [Fact]
    public void AFrameSpreadOverReportsIsAssembledFirst()
    {
        var swipe = new TouchpadSwipe();

        Assert.Empty(swipe.Report(3, Fingers((0, 10, 10))));
        Assert.Empty(swipe.Report(0, Fingers((1, 20, 10))));
        Assert.Equal([new SwipeBegan(3)], swipe.Report(0, Fingers((2, 30, 10))));
    }

    [Fact]
    public void ALiftedFingerEndsTheSwipeAndTheNextWaitsForEveryFingerUp()
    {
        var swipe = new TouchpadSwipe();
        swipe.Report(3, Fingers((0, 10, 10), (1, 20, 10), (2, 30, 10)));

        var lifted = swipe.Report(3, [new(0, true, 10, 10), new(1, true, 20, 10), new(2, false, 30, 10)]);
        var again = swipe.Report(3, Fingers((0, 10, 10), (1, 20, 10), (2, 30, 10)));
        swipe.Report(1, [new(0, false, 10, 10)]);
        var fresh = swipe.Report(3, Fingers((3, 10, 10), (4, 20, 10), (5, 30, 10)));

        Assert.Equal([new SwipeEnded()], lifted);
        Assert.Empty(again);
        Assert.Equal([new SwipeBegan(3)], fresh);
    }

    [Fact]
    public void AnotherFingerCancelsTheSwipeAndStartsOneWithMore()
    {
        var swipe = new TouchpadSwipe();
        swipe.Report(3, Fingers((0, 10, 10), (1, 20, 10), (2, 30, 10)));

        var more = swipe.Report(4, Fingers((0, 10, 10), (1, 20, 10), (2, 30, 10), (3, 40, 10)));

        Assert.Equal([new SwipeEnded(Cancelled: true), new SwipeBegan(4)], more);
    }

    private static TouchContact[] Fingers(params (int Id, double X, double Y)[] fingers) =>
        [.. fingers.Select(finger => new TouchContact(finger.Id, true, finger.X, finger.Y))];
}
