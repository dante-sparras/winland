using Winland.Windows;

namespace Winland.Tests;

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
        invoker.Invoke(() => ranOn = Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(5));

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
        invoker.Invoke(() => runs++, TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, runs);

        invoker.Drain();
        Assert.Equal(1, runs);
        release.Set();
        Assert.True(stuck.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void WithNoLoopTheActionRunsHere()
    {
        var invoker = new LoopInvoker();
        var runs = 0;

        invoker.Invoke(() => runs++, TimeSpan.FromSeconds(1));

        Assert.Equal(1, runs);
    }

    private static bool Peek() => Winland.Windows.Tests.Desktop.Peek(out _);
}
