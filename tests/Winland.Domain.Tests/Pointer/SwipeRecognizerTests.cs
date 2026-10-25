namespace Winland.Domain.Tests;

public class SwipeRecognizerTests
{
    private static readonly TrackpadGesture Horizontal = new(3, GestureAxis.Horizontal);

    [Fact]
    public void ASwipeShorterThanThePickDistancePicksNothing()
    {
        var swipe = new SwipeRecognizer();
        swipe.Begin();

        var step = swipe.Update(new SwipeUpdated(3, 4, 0), [Horizontal]);

        Assert.Equal(default, step);
        Assert.Null(swipe.Active);
    }

    [Fact]
    public void TheUpdateThatPicksTheGestureIsItsFirstStep()
    {
        var swipe = new SwipeRecognizer();
        swipe.Begin();

        swipe.Update(new SwipeUpdated(3, 3, 0), [Horizontal]);
        var picking = swipe.Update(new SwipeUpdated(3, 3, 1), [Horizontal]);
        var next = swipe.Update(new SwipeUpdated(3, -1, 0), [Horizontal]);

        Assert.Equal(new SwipeStep(Picked: true, Along: 3), picking);
        Assert.Equal(new SwipeStep(Picked: false, Along: -1), next);
        Assert.Same(Horizontal, swipe.Active);
    }

    [Fact]
    public void ASwipeThatMatchesNoGestureIsIgnoredUntilTheNextBegins()
    {
        var swipe = new SwipeRecognizer();
        swipe.Begin();

        swipe.Update(new SwipeUpdated(4, 10, 0), [Horizontal]);
        var later = swipe.Update(new SwipeUpdated(3, 10, 0), [Horizontal]);
        swipe.Begin();
        var again = swipe.Update(new SwipeUpdated(3, 10, 0), [Horizontal]);

        Assert.Equal(default, later);
        Assert.True(again.Picked);
    }

    [Fact]
    public void AVerticalSwipeDoesNotPickAHorizontalGesture()
    {
        var swipe = new SwipeRecognizer();
        swipe.Begin();

        var step = swipe.Update(new SwipeUpdated(3, 2, 10), [Horizontal]);

        Assert.Equal(default, step);
    }

    [Fact]
    public void AVerticalGestureReadsVerticalMovement()
    {
        var vertical = new TrackpadGesture(3, GestureAxis.Vertical);
        var swipe = new SwipeRecognizer();
        swipe.Begin();

        var step = swipe.Update(new SwipeUpdated(3, 2, 10), [vertical]);

        Assert.Equal(new SwipeStep(Picked: true, Along: 10), step);
    }

    [Fact]
    public void EndReturnsTheGestureAndIgnoresLaterUpdates()
    {
        var swipe = new SwipeRecognizer();
        swipe.Begin();
        swipe.Update(new SwipeUpdated(3, 10, 0), [Horizontal]);

        var ended = swipe.End();
        var after = swipe.Update(new SwipeUpdated(3, 10, 0), [Horizontal]);

        Assert.Same(Horizontal, ended);
        Assert.Null(swipe.Active);
        Assert.Equal(default, after);
    }
}
