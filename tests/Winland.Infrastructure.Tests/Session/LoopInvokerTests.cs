namespace Winland.Infrastructure.Tests;

[Collection("Console")]
public class LoopInvokerTests
{
    private const uint Quit = 0x0012;

    [Fact]
    public void AnActionFromAnotherThreadRunsOnTheLoopThread()
    {
        var invoker = new LoopInvoker();
        using var attached = new ManualResetEventSlim();
        var loopThread = 0;
        uint nativeThread = 0;
        var loop = new Thread(() =>
        {
            loopThread = Environment.CurrentManagedThreadId;
            nativeThread = ProbeNative.GetCurrentThreadId();
            // A thread gets a message queue the first time it asks for a message.
            _ = Peek();
            invoker.Attach(nativeThread);
            attached.Set();
            while (ProbeNative.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.message == LoopInvoker.Message)
                    invoker.Drain();
            }
        }) { IsBackground = true };
        loop.Start();
        Assert.True(attached.Wait(TimeSpan.FromSeconds(5)));

        var ranOn = 0;
        invoker.Invoke(() => ranOn = Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        Assert.Equal(loopThread, ranOn);
        ProbeNative.PostThreadMessage(nativeThread, Quit, 0, 0);
        Assert.True(loop.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ALoopThatDoesNotAnswerRunsTheActionHereOnce()
    {
        var invoker = new LoopInvoker();
        using var attached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        uint nativeThread = 0;
        var stuck = new Thread(() =>
        {
            nativeThread = ProbeNative.GetCurrentThreadId();
            _ = Peek();
            attached.Set();
            release.Wait();
        }) { IsBackground = true };
        stuck.Start();
        Assert.True(attached.Wait(TimeSpan.FromSeconds(5)));
        invoker.Attach(nativeThread);

        var runs = 0;
        invoker.Invoke(() => runs++, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5));
        Assert.Equal(1, runs);

        invoker.Drain();
        Assert.Equal(1, runs);
        release.Set();
        Assert.True(stuck.Join(TimeSpan.FromSeconds(5)));
    }

    // The console close handler returns when Invoke does, and Windows then ends the process. A cleanup
    // the loop already started must get the rest of the budget, not be cut off when the loop is slow to answer.
    [Fact]
    public void AnActionTheLoopStartedIsWaitedForPastTheAnswerTime()
    {
        var invoker = new LoopInvoker();
        using var attached = new ManualResetEventSlim();
        uint nativeThread = 0;
        var loop = new Thread(() =>
        {
            nativeThread = ProbeNative.GetCurrentThreadId();
            _ = Peek();
            invoker.Attach(nativeThread);
            attached.Set();
            while (ProbeNative.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.message == LoopInvoker.Message)
                    invoker.Drain();
            }
        }) { IsBackground = true };
        loop.Start();
        Assert.True(attached.Wait(TimeSpan.FromSeconds(5)));

        var finished = false;
        var runs = 0;
        invoker.Invoke(
            () =>
            {
                runs++;
                Thread.Sleep(500);
                finished = true;
            },
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromSeconds(5));

        Assert.True(finished);
        Assert.Equal(1, runs);
        ProbeNative.PostThreadMessage(nativeThread, Quit, 0, 0);
        Assert.True(loop.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void AnActionTheLoopStartedIsLeftWhenTheLimitPasses()
    {
        var invoker = new LoopInvoker();
        using var attached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        uint nativeThread = 0;
        var loop = new Thread(() =>
        {
            nativeThread = ProbeNative.GetCurrentThreadId();
            _ = Peek();
            invoker.Attach(nativeThread);
            attached.Set();
            while (ProbeNative.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.message == LoopInvoker.Message)
                    invoker.Drain();
            }
        }) { IsBackground = true };
        loop.Start();
        Assert.True(attached.Wait(TimeSpan.FromSeconds(5)));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        invoker.Invoke(() => release.Wait(), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(300));
        watch.Stop();

        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(3));
        release.Set();
        ProbeNative.PostThreadMessage(nativeThread, Quit, 0, 0);
        Assert.True(loop.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void WithNoLoopTheActionRunsHere()
    {
        var invoker = new LoopInvoker();
        var runs = 0;

        invoker.Invoke(() => runs++, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        Assert.Equal(1, runs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APostedFailureIsLoggedOnceAndDoesNotStopTheQueue(bool missingLoop)
    {
        var invoker = new LoopInvoker();
        _ = Peek();
        invoker.Attach(missingLoop ? uint.MaxValue : ProbeNative.GetCurrentThreadId());
        var stderr = Console.Error;
        using var log = new StringWriter();
        Console.SetError(log);
        try
        {
            var poster = new Thread(() => invoker.Post(() => throw new InvalidOperationException("posted failed")));
            poster.Start();
            Assert.True(poster.Join(TimeSpan.FromSeconds(5)));
            if (!missingLoop)
                Assert.Empty(log.ToString());

            invoker.Drain();
            invoker.Drain();

            Assert.Contains("System.InvalidOperationException: posted failed", log.ToString(), StringComparison.Ordinal);
            Assert.Contains(nameof(APostedFailureIsLoggedOnceAndDoesNotStopTheQueue), log.ToString(), StringComparison.Ordinal);
            Assert.Equal(2, log.ToString().Split("posted failed", StringSplitOptions.None).Length);
            var runs = 0;
            invoker.Detach();
            invoker.Post(() => runs++);
            Assert.Equal(1, runs);
        }
        finally
        {
            Console.SetError(stderr);
            invoker.Detach();
        }
    }

    private static bool Peek() => Winland.Infrastructure.Tests.Desktop.Peek(out _);
}
