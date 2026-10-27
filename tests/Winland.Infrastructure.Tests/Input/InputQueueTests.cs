namespace Winland.Infrastructure.Tests;

public class InputQueueTests
{
    private static InputQueue<string> Moves() =>
        new((waiting, next) => waiting.StartsWith("move", StringComparison.Ordinal) && next.StartsWith("move", StringComparison.Ordinal));

    [Fact]
    public void OnlyTheNewestWaitingMoveIsKept()
    {
        var queue = Moves();
        queue.Post("move 1");
        queue.Post("move 2");
        queue.Post("move 3");

        Assert.Equal(["move 3"], queue.Take());
    }

    [Fact]
    public void ItemsThatAddUpWaitAsOne()
    {
        var queue = new InputQueue<int>((_, _) => false, (waiting, next) => (waiting > 0 && next > 0, waiting + next));
        queue.Post(1);
        queue.Post(2);
        queue.Post(-5);
        queue.Post(3);

        Assert.Equal([3, -5, 3], queue.Take());
    }

    // A move never jumps ahead of a bind that was queued before it, and a bind is never dropped.
    [Fact]
    public void MovesAreKeptInOrderAroundOtherInput()
    {
        var queue = Moves();
        queue.Post("move 1");
        queue.Post("move 2");
        queue.Post("bind a");
        queue.Post("bind b");
        queue.Post("move 3");
        queue.Post("move 4");

        Assert.Equal(["move 2", "bind a", "bind b", "move 4"], queue.Take());
    }

    [Fact]
    public void AMoveTakenByTheWorkerIsNotReplaced()
    {
        var queue = Moves();
        queue.Post("move 1");
        Assert.Equal(["move 1"], queue.Take());

        queue.Post("move 2");

        Assert.Equal(["move 2"], queue.Take());
    }

    [Fact]
    public void ACompletedQueueHandsOverWhatIsLeftAndThenNothing()
    {
        var queue = Moves();
        queue.Post("bind a");
        queue.Complete();

        Assert.False(queue.Post("bind b"));
        Assert.Equal(["bind a"], queue.Take());
        Assert.Empty(queue.Take());
    }

    [Fact]
    public async Task TakeWaitsForThePoster()
    {
        var queue = Moves();
        var taken = Task.Run(() => queue.Take().ToArray());
        await Task.Delay(50);
        Assert.False(taken.IsCompleted);

        queue.Post("bind a");

        Assert.Equal(["bind a"], await taken.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
