namespace Winland.Infrastructure.Tests;

public class CoalescerTests
{
    [Fact]
    public void ValuesPostedWhileOneIsHandledFoldIntoTheNewest()
    {
        using var first = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var last = new ManualResetEventSlim();
        using var drained = new ManualResetEventSlim();
        var handled = new List<string>();
        using var coalescer = new Coalescer<string>(value =>
        {
            lock (handled)
                handled.Add(value);

            if (value == "1")
            {
                first.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            }

            if (value == "4")
                last.Set();
            if (value == "done")
                drained.Set();
        }, TimeSpan.Zero, "test coalescer");

        coalescer.Post("1");
        Assert.True(first.Wait(TimeSpan.FromSeconds(5)));
        coalescer.Post("2");
        coalescer.Post("3");
        coalescer.Post("4");
        release.Set();

        Assert.True(last.Wait(TimeSpan.FromSeconds(5)));
        coalescer.Post("done");
        Assert.True(drained.Wait(TimeSpan.FromSeconds(5)));
        lock (handled)
            Assert.Equal(["1", "4", "done"], handled);
    }

    // After each run the worker rests for the gap, and values posted meanwhile fold into the newest.
    [Fact]
    public void ValuesPostedDuringTheGapFoldIntoTheNewest()
    {
        using var first = new ManualResetEventSlim();
        using var last = new ManualResetEventSlim();
        var handled = new List<string>();
        using var coalescer = new Coalescer<string>(value =>
        {
            lock (handled)
                handled.Add(value);

            if (value == "1")
                first.Set();
            if (value == "3")
                last.Set();
        }, TimeSpan.FromMilliseconds(500), "test coalescer");

        coalescer.Post("1");
        Assert.True(first.Wait(TimeSpan.FromSeconds(5)));
        coalescer.Post("2");
        coalescer.Post("3");

        Assert.True(last.Wait(TimeSpan.FromSeconds(5)));
        lock (handled)
            Assert.Equal(["1", "3"], handled);
    }

    [Fact]
    public void AHandlerThatThrowsDoesNotStopTheWorker()
    {
        using var threw = new ManualResetEventSlim();
        using var handled = new ManualResetEventSlim();
        using var coalescer = new Coalescer<string>(value =>
        {
            if (value == "bad")
            {
                threw.Set();
                throw new InvalidOperationException("The value could not be handled.");
            }

            handled.Set();
        }, TimeSpan.Zero, "test coalescer");

        // "good" is posted only once "bad" threw, so it cannot replace "bad" before the handler sees it.
        coalescer.Post("bad");
        Assert.True(threw.Wait(TimeSpan.FromSeconds(5)));
        coalescer.Post("good");

        Assert.True(handled.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void AfterDisposeNothingIsHandledAndDisposingAgainDoesNothing()
    {
        var calls = 0;
        var coalescer = new Coalescer<string>(_ => Interlocked.Increment(ref calls), TimeSpan.Zero, "test coalescer");

        coalescer.Dispose();
        coalescer.Post("late");
        coalescer.Post("later");
        coalescer.Dispose();

        Assert.Equal(0, Volatile.Read(ref calls));
    }

    [Fact]
    public void NoHandlerOrNoValueIsRefused()
    {
        using var coalescer = new Coalescer<string>(_ => { }, TimeSpan.Zero, "test coalescer");

        Assert.Throws<ArgumentNullException>(() => new Coalescer<string>(null!, TimeSpan.Zero, "test coalescer"));
        Assert.Throws<ArgumentNullException>(() => coalescer.Post(null!));
    }

    [Fact]
    public void ThePosterDoesNotWaitForTheHandler()
    {
        using var release = new ManualResetEventSlim();
        using var coalescer = new Coalescer<string>(_ => release.Wait(TimeSpan.FromSeconds(5)), TimeSpan.Zero, "test coalescer");

        var started = Environment.TickCount64;
        coalescer.Post("slow");
        coalescer.Post("slower");

        Assert.True(Environment.TickCount64 - started < 1000);
        release.Set();
    }
}
