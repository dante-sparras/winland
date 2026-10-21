namespace Winland.Domain.Tests;

public class SwipeRecognizerTests
{
    private static readonly TrackpadGesture Horizontal = new(3, GestureDirection.Horizontal, Modifier.None, 2, "workspace");

    [Fact]
    public void ASwipeShorterThanThePickDistancePicksNothing()
    {
        var swipe = new SwipeRecognizer();
        swipe.Begin();

        var step = swipe.Update(new SwipeUpdated(3, 4, 0), [Horizontal], Modifier.None);

        Assert.Equal(default, step);
        Assert.Null(swipe.Active);
    }

    [Fact]
    public void TheUpdateThatPicksTheGestureIsItsFirstStep()
    {
        var swipe = new SwipeRecognizer();
        swipe.Begin();

        swipe.Update(new SwipeUpdated(3, 3, 0), [Horizontal], Modifier.None);
        var picking = swipe.Update(new SwipeUpdated(3, 3, 1), [Horizontal], Modifier.None);
        var next = swipe.Update(new SwipeUpdated(3, -1, 0), [Horizontal], Modifier.None);

        Assert.Equal(new SwipeStep(Picked: true, Along: 6), picking);
        Assert.Equal(new SwipeStep(Picked: false, Along: -2), next);
        Assert.Same(Horizontal, swipe.Active);
    }

    [Theory]
    [InlineData(4, Modifier.None)]
    [InlineData(3, Modifier.Super)]
    public void ASwipeThatMatchesNoGestureIsIgnoredUntilTheNextBegins(int fingers, Modifier held)
    {
        var swipe = new SwipeRecognizer();
        swipe.Begin();

        swipe.Update(new SwipeUpdated(fingers, 10, 0), [Horizontal], held);
        var later = swipe.Update(new SwipeUpdated(3, 10, 0), [Horizontal], Modifier.None);
        swipe.Begin();
        var again = swipe.Update(new SwipeUpdated(3, 10, 0), [Horizontal], Modifier.None);

        Assert.Equal(default, later);
        Assert.True(again.Picked);
    }

    [Fact]
    public void AVerticalSwipeDoesNotPickAHorizontalGesture()
    {
        var swipe = new SwipeRecognizer();
        swipe.Begin();

        var step = swipe.Update(new SwipeUpdated(3, 2, 10), [Horizontal], Modifier.None);

        Assert.Equal(default, step);
    }

    [Fact]
    public void EndReturnsTheGestureAndIgnoresLaterUpdates()
    {
        var swipe = new SwipeRecognizer();
        swipe.Begin();
        swipe.Update(new SwipeUpdated(3, 10, 0), [Horizontal], Modifier.None);

        var ended = swipe.End();
        var after = swipe.Update(new SwipeUpdated(3, 10, 0), [Horizontal], Modifier.None);

        Assert.Same(Horizontal, ended);
        Assert.Null(swipe.Active);
        Assert.Equal(default, after);
    }
}
