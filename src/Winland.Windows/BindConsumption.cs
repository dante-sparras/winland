using Winland.Core;

namespace Winland.Windows;

/// <summary>
/// One swallowed key that an auto-consuming bind may give back. The hook builds it when the press is swallowed
/// only because the bind is auto-consuming, and injects it if every dispatcher of that press fails.
/// </summary>
internal readonly record struct SwallowedKey(ushort VirtualKey, ushort Scan, bool Extended, string Name, bool Pressed);

/// <summary>
/// Counts the dispatches of one auto-consuming decision. The key is replayed once when every one of them reports
/// failure, and one success keeps it swallowed, as Winland passes the event only when the dispatcher
/// does not succeed.
/// </summary>
internal sealed class BindConsumption
{
    private readonly Action _replay;
    private readonly Lock _gate = new();
    private int _pending;
    private bool _consumed;
    private bool _done;

    internal BindConsumption(Action replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        _replay = replay;
    }

    /// <summary>One dispatcher was posted and will call <see cref="Note"/> exactly once.</summary>
    internal void Expect()
    {
        lock (_gate)
            _pending++;
    }

    /// <summary>That dispatcher finished. <paramref name="consumed"/> is true when it succeeded and did not ask to pass the key.</summary>
    internal void Note(bool consumed)
    {
        bool replay;
        lock (_gate)
        {
            if (_done)
                return;

            if (consumed)
                _consumed = true;

            if (--_pending > 0)
                return;

            _done = true;
            replay = !_consumed && _pending == 0;
        }

        if (replay)
            _replay();
    }
}
