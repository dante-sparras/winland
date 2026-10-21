using Winland.Core;
using Winland.Windows;

namespace Winland.Windows.Tests;

public class TouchpadSwipeTests
{
    [Fact]
    public void ThreeFingersThatTravelStartASwipeAndTheirMeanMovementUpdatesIt()
    {
        var swipe = new TouchpadSwipe();

        var down = swipe.Report(3, Fingers((0, 10, 10), (1, 20, 10), (2, 30, 10)));
        var began = swipe.Report(3, Fingers((0, 8, 11), (1, 18, 11), (2, 28, 11)));
        var moved = swipe.Report(3, Fingers((0, 7, 11), (1, 17, 11), (2, 27, 11)));

        Assert.Empty(down);
        Assert.Equal(new SwipeBegan(3), began[0]);
        var first = Assert.IsType<SwipeUpdated>(began[1]);
        Assert.Equal(-2 * TouchpadSwipe.PixelsPerMillimetre, first.DeltaX, 6);
        Assert.Equal(TouchpadSwipe.PixelsPerMillimetre, first.DeltaY, 6);
        var update = Assert.IsType<SwipeUpdated>(Assert.Single(moved));
        Assert.Equal(3, update.Fingers);
        Assert.Equal(-TouchpadSwipe.PixelsPerMillimetre, update.DeltaX, 6);
        Assert.Equal(0, update.DeltaY, 6);
    }

    [Fact]
    public void TwoFingersThatTravelAreScrollNotASwipe()
    {
        var swipe = new TouchpadSwipe();

        Assert.Empty(swipe.Report(2, Fingers((0, 10, 10), (1, 20, 10))));
        Assert.Empty(swipe.Report(2, Fingers((0, 5, 10), (1, 15, 10))));
        Assert.Empty(swipe.Report(2, Fingers((0, 7, 10), (1, 25, 10))));
    }

    [Fact]
    public void AFrameSpreadOverReportsIsAssembledFirst()
    {
        var swipe = new TouchpadSwipe();
        swipe.Report(3, Fingers((0, 10, 10), (1, 20, 10), (2, 30, 10)));

        Assert.Empty(swipe.Report(3, Fingers((0, 12, 10))));
        Assert.Empty(swipe.Report(0, Fingers((1, 22, 10))));
        Assert.Equal(new SwipeBegan(3), swipe.Report(0, Fingers((2, 32, 10)))[0]);
    }

    [Fact]
    public void ALiftedFingerEndsTheSwipeAndTheNextWaitsForEveryFingerUp()
    {
        var swipe = new TouchpadSwipe();
        swipe.Report(3, Fingers((0, 10, 10), (1, 20, 10), (2, 30, 10)));
        swipe.Report(3, Fingers((0, 12, 10), (1, 22, 10), (2, 32, 10)));

        var lifted = swipe.Report(3, [new(0, true, 12, 10), new(1, true, 22, 10), new(2, false, 32, 10)]);
        var again = swipe.Report(3, Fingers((0, 10, 10), (1, 20, 10), (2, 30, 10)));
        swipe.Report(1, [new(0, false, 10, 10)]);
        swipe.Report(3, Fingers((3, 10, 10), (4, 20, 10), (5, 30, 10)));
        var fresh = swipe.Report(3, Fingers((3, 12, 10), (4, 22, 10), (5, 32, 10)));

        Assert.True(Assert.IsType<SwipeEnded>(Assert.Single(lifted)).TimeMs > 0);
        Assert.Equal([new SwipeEnded()], Untimed(lifted));
        Assert.Empty(again);
        Assert.Equal(new SwipeBegan(3), fresh[0]);
    }

    [Fact]
    public void AnotherFingerCancelsTheSwipeAndStartsOneWithMore()
    {
        var swipe = new TouchpadSwipe();
        swipe.Report(3, Fingers((0, 10, 10), (1, 20, 10), (2, 30, 10)));
        swipe.Report(3, Fingers((0, 12, 10), (1, 22, 10), (2, 32, 10)));

        var more = swipe.Report(4, Fingers((0, 12, 10), (1, 22, 10), (2, 32, 10), (3, 42, 10)));
        var moved = swipe.Report(4, Fingers((0, 14, 10), (1, 24, 10), (2, 34, 10), (3, 44, 10)));

        Assert.Equal([new SwipeEnded(Cancelled: true)], Untimed(more));
        Assert.Equal(new SwipeBegan(4), moved[0]);
    }

    [Fact]
    public void EachFinishedFrameIsHandedOnWithTheFingersDown()
    {
        var frames = new List<IReadOnlyDictionary<int, (double X, double Y)>>();
        var swipe = new TouchpadSwipe { Framed = down => frames.Add(new Dictionary<int, (double X, double Y)>(down)) };

        _ = swipe.Report(2, [new TouchContact(0, true, 1, 2)]);
        Assert.Empty(frames);
        _ = swipe.Report(0, [new TouchContact(1, false, 3, 4)]);

        Assert.Equal((1d, 2d), Assert.Single(Assert.Single(frames)).Value);
    }

    [Fact]
    public void TheMouseHookSeesTheFingersTheTouchpadLastReported()
    {
        TouchpadFingers.Note(1, 1000);
        Assert.True(TouchpadFingers.Moves(1000 + TouchpadPointer.StaleMs));
        Assert.False(TouchpadFingers.Moves(1001 + TouchpadPointer.StaleMs));
        Assert.False(TouchpadFingers.Scrolls(1000, 30));

        TouchpadFingers.Note(2, 2000);
        TouchpadFingers.Note(0, 2100);
        Assert.False(TouchpadFingers.Moves(2100));
        Assert.True(TouchpadFingers.Scrolls(2100, 30));
        Assert.False(TouchpadFingers.Scrolls(2100, 120));
    }

    // The inputs with the time of an end left out, which the clock sets.
    private static Input[] Untimed(IEnumerable<Input> inputs) =>
        [.. inputs.Select(input => input switch
        {
            SwipeEnded ended => ended with { TimeMs = 0 },
            _ => input,
        })];

    private static TouchContact[] Fingers(params (int Id, double X, double Y)[] fingers) =>
        [.. fingers.Select(finger => new TouchContact(finger.Id, true, finger.X, finger.Y))];
}
