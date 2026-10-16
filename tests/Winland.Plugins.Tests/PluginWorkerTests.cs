using System.Diagnostics;
using Winland.Plugins;

namespace Winland.Tests;

public class PluginWorkerTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    // A thread per call, for every event and every handler, pinned the plugin's collectible
    // context each time one was abandoned. One long-lived thread runs every call instead.
    [Fact]
    public void EveryCallRunsOnTheSameThread()
    {
        using var worker = new PluginWorker("sample");
        var threads = new HashSet<int>();

        for (var i = 0; i < 20; i++)
            Assert.True(worker.Run(Budget, () => threads.Add(Environment.CurrentManagedThreadId), out _, out _));

        var thread = Assert.Single(threads);
        Assert.NotEqual(Environment.CurrentManagedThreadId, thread);
    }

    [Fact]
    public void AnExceptionIsReturned()
    {
        using var worker = new PluginWorker("sample");

        var finished = worker.Run(Budget, () => throw new InvalidOperationException("boom"), out var error, out var timedOut);

        Assert.False(finished);
        Assert.False(timedOut);
        Assert.Equal("boom", error?.Message);
        Assert.True(worker.Run(Budget, () => { }, out _, out _));
    }

    [Fact]
    public void AHungCallIsAbandonedWithinTheBudgetAndLaterCallsRunOnceItReturns()
    {
        using var worker = new PluginWorker("sample");
        using var release = new ManualResetEventSlim();
        var abandonedRan = 0;

        var watch = Stopwatch.StartNew();
        var hung = worker.Run(TimeSpan.FromMilliseconds(50), () => release.Wait(), out _, out var timedOut);
        watch.Stop();
        var queued = worker.Run(TimeSpan.FromMilliseconds(50), () => Interlocked.Increment(ref abandonedRan), out _, out var queuedTimedOut);
        release.Set();
        var later = worker.Run(Budget, () => { }, out _, out _);

        Assert.False(hung);
        Assert.True(timedOut);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"abandoned call took {watch.Elapsed}");
        Assert.False(queued);
        Assert.True(queuedTimedOut);
        Assert.True(later);
        Assert.Equal(0, Volatile.Read(ref abandonedRan));
    }

    [Fact]
    public void DisposeEndsTheThreadAndLaterCallsFail()
    {
        var worker = new PluginWorker("sample");
        Thread? thread = null;
        Assert.True(worker.Run(Budget, () => thread = Thread.CurrentThread, out _, out _));

        worker.Dispose();

        Assert.NotNull(thread);
        Assert.True(thread.Join(Budget));
        Assert.False(worker.Run(Budget, () => { }, out var error, out var timedOut));
        Assert.False(timedOut);
        Assert.IsType<ObjectDisposedException>(error);
    }
}
