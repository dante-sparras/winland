namespace Winland.Infrastructure.Tests;

public class DeviceCacheTests
{
    [Fact]
    public void AnOpenedDeviceIsOpenedOnce()
    {
        var opened = 0;
        using var cache = new DeviceCache<Probe>(_ => { opened++; return new Probe(); }, () => 0, retryMs: 1000);

        var first = cache.For(1);

        Assert.Same(first, cache.For(1));
        Assert.Equal(1, opened);
    }

    // A failed read, such as one made while the device was still starting, is tried again later.
    [Fact]
    public void AFailedOpenIsTriedAgainAfterTheRetryDelay()
    {
        var now = 0L;
        var answers = new Queue<Probe?>([null, new Probe()]);
        var opened = 0;
        using var cache = new DeviceCache<Probe>(_ => { opened++; return answers.Dequeue(); }, () => now, retryMs: 1000);

        Assert.Null(cache.For(1));
        now = 999;
        Assert.Null(cache.For(1));
        Assert.Equal(1, opened);

        now = 1000;
        Assert.NotNull(cache.For(1));
        Assert.Equal(2, opened);
    }

    [Fact]
    public void ARemovedDeviceIsDisposedAndOpenedAfreshWhenItsHandleComesBack()
    {
        var probes = new List<Probe>();
        using var cache = new DeviceCache<Probe>(_ => { var probe = new Probe(); probes.Add(probe); return probe; }, () => 0, retryMs: 1000);
        var first = cache.For(1);

        cache.Remove(1);

        Assert.True(first!.Disposed);
        Assert.NotSame(first, cache.For(1));
    }

    [Fact]
    public void ARemovedFailureIsTriedAgainAtOnce()
    {
        var answers = new Queue<Probe?>([null, new Probe()]);
        using var cache = new DeviceCache<Probe>(_ => answers.Dequeue(), () => 0, retryMs: 1000);
        Assert.Null(cache.For(1));

        cache.Remove(1);

        Assert.NotNull(cache.For(1));
    }

    [Fact]
    public void DisposingTheCacheDisposesEveryDevice()
    {
        var cache = new DeviceCache<Probe>(_ => new Probe(), () => 0, retryMs: 1000);
        var first = cache.For(1)!;
        var second = cache.For(2)!;

        cache.Dispose();

        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    private sealed class Probe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
