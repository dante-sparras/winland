using Winland.Windows;

namespace Winland.Tests;

public class CoalescerTests
{
    [Fact]
    public void ValuesPostedWhileOneIsHandledFoldIntoTheNewest()
    {
        using var first = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var last = new ManualResetEventSlim();
        var handled = new List<string>();
        using var coalescer = new Coalescer<string>(value =>
        {
            lock (handled)
                handled.Add(value);

            if (value == "1")
            {
                first.Set();
                release.Wait();
            }

            if (value == "4")
                last.Set();
        }, TimeSpan.Zero, "test coalescer");

        coalescer.Post("1");
        Assert.True(first.Wait(TimeSpan.FromSeconds(5)));
        coalescer.Post("2");
        coalescer.Post("3");
        coalescer.Post("4");
        release.Set();

        Assert.True(last.Wait(TimeSpan.FromSeconds(5)));
        lock (handled)
            Assert.Equal(["1", "4"], handled);
    }

    [Fact]
    public void ThePosterDoesNotWaitForTheHandler()
    {
        using var release = new ManualResetEventSlim();
        using var coalescer = new Coalescer<string>(_ => release.Wait(), TimeSpan.Zero, "test coalescer");

        var started = Environment.TickCount64;
        coalescer.Post("slow");
        coalescer.Post("slower");

        Assert.True(Environment.TickCount64 - started < 1000);
        release.Set();
    }
}
