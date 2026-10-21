using Winland.Core;

namespace Winland.Windows;

/// <summary>
/// What was read about each raw input device, by its handle. A device that could not be read is
/// tried again after a delay rather than written off, and a removed device is forgotten, since
/// Windows can give its handle to the next device. Used on one thread.
/// </summary>
internal sealed class DeviceCache<T> : IDisposable
    where T : class, IDisposable
{
    private readonly Func<nint, T?> _open;
    private readonly Func<long> _clock;
    private readonly long _retryMs;
    private readonly Dictionary<nint, T> _known = [];
    private readonly Dictionary<nint, long> _failedAt = [];

    internal DeviceCache(Func<nint, T?> open, Func<long> clock, long retryMs)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(clock);
        _open = open;
        _clock = clock;
        _retryMs = retryMs;
    }

    internal T? For(nint device)
    {
        if (_known.TryGetValue(device, out var known))
            return known;

        var now = _clock();
        if (_failedAt.TryGetValue(device, out var failed) && now - failed < _retryMs)
            return null;

        var opened = _open(device);
        if (opened is null)
        {
            _failedAt[device] = now;
            return null;
        }

        _failedAt.Remove(device);
        _known[device] = opened;
        return opened;
    }

    internal void Remove(nint device)
    {
        _failedAt.Remove(device);
        if (_known.Remove(device, out var known))
            known.Dispose();
    }

    public void Dispose()
    {
        foreach (var known in _known.Values)
            known.Dispose();

        _known.Clear();
        _failedAt.Clear();
    }
}
