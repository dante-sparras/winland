using System.Collections.Concurrent;
using Windows.Win32;

namespace Winland.Windows.Tests;

// The hooks' thread. These tests drive it with stand-ins, so no hook sees the desktop's input.
public class InputThreadTests
{
    private const uint Probe = 0x8000 + 90;

    [Fact]
    public void StartRunsOnItsOwnThreadAndReturnsOnceStarted()
    {
        var caller = Environment.CurrentManagedThreadId;
        int? started = null;
        using var thread = new InputThread(() => started = Environment.CurrentManagedThreadId, _ => false, () => { });

        thread.Start();

        Assert.NotNull(started);
        Assert.NotEqual(caller, started);
    }

    [Fact]
    public void AFailedStartIsThrownToTheCaller()
    {
        using var thread = new InputThread(() => throw new InvalidOperationException("no hook"), _ => false, () => { });

        var error = Assert.Throws<InvalidOperationException>(thread.Start);

        Assert.Equal("no hook", error.Message);
    }

    [Fact]
    public void ThreadMessagesAreServicedOnTheInputThread()
    {
        int? startedOn = null;
        var serviced = new BlockingCollection<(uint Message, int Thread)>();
        using var thread = new InputThread(
            () => startedOn = Environment.CurrentManagedThreadId,
            message =>
            {
                serviced.Add((message, Environment.CurrentManagedThreadId));
                return true;
            },
            () => { });
        thread.Start();

        Assert.True(PInvoke.PostThreadMessage(thread.Id, Probe, default, default));

        Assert.True(serviced.TryTake(out var seen, TimeSpan.FromSeconds(10)));
        Assert.Equal(Probe, seen.Message);
        Assert.Equal(startedOn, seen.Thread);
    }

    [Fact]
    public void DisposeStopsOnTheInputThread()
    {
        int? startedOn = null;
        int? stoppedOn = null;
        var thread = new InputThread(
            () => startedOn = Environment.CurrentManagedThreadId,
            _ => false,
            () => stoppedOn = Environment.CurrentManagedThreadId);
        thread.Start();

        thread.Dispose();

        Assert.NotNull(stoppedOn);
        Assert.Equal(startedOn, stoppedOn);
    }
}
