namespace Winland.Windows;

/// <summary>
/// Pairs a low-level key event with the raw-input packet of the same key. Raw input carries the keyboard and the
/// hook does not, and neither carries the other's clock, so a packet is the event's when the key and the direction
/// match and the times are within <see cref="SlackMs"/>. A packet that is older is dropped. One that is newer is
/// kept for a later event. The oldest match is taken, and when none fits the key has no device: an inclusive bind
/// misses it, which is preferred to naming the wrong keyboard.
/// </summary>
internal sealed class KeyDeviceCorrelator
{
    internal const int SlackMs = 30;
    private const int Cap = 32;

    private readonly Queue<Packet> _packets = new();

    internal readonly record struct Packet(long Device, int VirtualKey, int Scan, bool Up, uint Time);

    internal void Offer(Packet packet)
    {
        if (_packets.Count >= Cap)
            _packets.Dequeue();

        _packets.Enqueue(packet);
    }

    /// <summary>The device handle of the oldest packet that is this key, or false when none is close enough.</summary>
    internal bool Take(int virtualKey, int scan, bool up, uint time, out long device)
    {
        device = 0;
        long? found = null;
        var kept = new List<Packet>(_packets.Count);
        foreach (var packet in _packets)
        {
            var delta = unchecked((int)(packet.Time - time));
            if (delta < -SlackMs)
                continue;

            if (found is null && delta <= SlackMs && packet.VirtualKey == virtualKey && packet.Up == up && ScansMatch(packet.Scan, scan))
            {
                found = packet.Device;
                continue;
            }

            kept.Add(packet);
        }

        _packets.Clear();
        foreach (var packet in kept)
            _packets.Enqueue(packet);

        if (found is not long handle)
            return false;

        device = handle;
        return true;
    }

    internal int Count => _packets.Count;

    // A scan of zero is a key whose code was not reported. It matches the other side's scan.
    private static bool ScansMatch(int packet, int hook) => packet == 0 || hook == 0 || packet == hook;
}
