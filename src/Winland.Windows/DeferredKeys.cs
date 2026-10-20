namespace Winland.Windows;

/// <summary>
/// Keys the hook swallowed because a device bind might match them, waiting for the raw packet that names the
/// keyboard. That packet is posted only after the hook returns, so it cannot be paired inside the callback. The
/// hook thread owns this. A press and the release that arrives before the packet share one slot, and repeats during
/// the wait are counted rather than decided early.
/// </summary>
internal sealed class DeferredKeys
{
    internal const int WaitMs = 50;
    internal const int Capacity = 8;
    private const int RepeatCap = 8;

    internal readonly record struct Edge(int VirtualKey, int Scan, bool Extended, bool Up, string Name, int Xkb);

    internal readonly record struct Pending(Edge Key, bool HasRelease, Edge Release, int Repeats);

    private struct Slot
    {
        public bool Live;
        public int Seq;
        public long Due;
        public Edge Key;
        public bool HasRelease;
        public Edge Release;
        public byte Repeats;
    }

    private readonly Slot[] _slots = new Slot[Capacity];
    private int _seq;

    /// <summary>Remembers a press. False when eight presses are already waiting, so the caller decides with no device.</summary>
    internal bool Hold(Edge edge, long now)
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Live)
                continue;

            _slots[i] = new Slot
            {
                Live = true,
                Seq = ++_seq,
                Due = now + WaitMs,
                Key = edge,
            };
            return true;
        }

        return false;
    }

    internal bool IsWaiting(int virtualKey) => FindPress(virtualKey) >= 0;

    /// <summary>A repeat of a press that is still waiting. At most eight are counted, and they are decided with the press.</summary>
    internal bool NoteRepeat(int virtualKey)
    {
        var index = FindPress(virtualKey);
        if (index < 0)
            return false;

        ref var slot = ref _slots[index];
        if (slot.Repeats < RepeatCap)
            slot.Repeats++;

        return true;
    }

    /// <summary>The release of a press that is still waiting. Both edges are then decided from the same packet.</summary>
    internal bool AttachRelease(Edge release)
    {
        var index = FindPress(release.VirtualKey);
        if (index < 0)
            return false;

        ref var slot = ref _slots[index];
        if (!slot.HasRelease)
        {
            slot.HasRelease = true;
            slot.Release = release;
        }

        return true;
    }

    /// <summary>
    /// The oldest waiting edge this packet is for. A release packet does not complete a waiting press: the press
    /// packet completes the press and a release that was attached to it.
    /// </summary>
    internal bool Offer(int virtualKey, int scan, bool up, out Pending pending)
    {
        var found = -1;
        var seq = int.MaxValue;
        for (var i = 0; i < _slots.Length; i++)
        {
            ref var slot = ref _slots[i];
            if (!slot.Live || slot.Key.Up != up || slot.Key.VirtualKey != virtualKey || !ScansMatch(slot.Key.Scan, scan))
                continue;
            if (slot.Seq >= seq)
                continue;

            found = i;
            seq = slot.Seq;
        }

        if (found < 0)
        {
            pending = default;
            return false;
        }

        return Take(found, out pending);
    }

    /// <summary>The oldest edge whose wait has ended, or false when none is due.</summary>
    internal bool Expire(long now, out Pending pending)
    {
        var found = -1;
        var due = long.MaxValue;
        var seq = int.MaxValue;
        for (var i = 0; i < _slots.Length; i++)
        {
            ref var slot = ref _slots[i];
            if (!slot.Live || slot.Due > now)
                continue;
            if (slot.Due > due || (slot.Due == due && slot.Seq >= seq))
                continue;

            found = i;
            due = slot.Due;
            seq = slot.Seq;
        }

        if (found < 0)
        {
            pending = default;
            return false;
        }

        return Take(found, out pending);
    }

    internal bool TryNextDue(out long due)
    {
        due = long.MaxValue;
        var any = false;
        for (var i = 0; i < _slots.Length; i++)
        {
            ref var slot = ref _slots[i];
            if (!slot.Live || slot.Due >= due)
                continue;

            due = slot.Due;
            any = true;
        }

        return any;
    }

    private bool Take(int index, out Pending pending)
    {
        ref var slot = ref _slots[index];
        pending = new Pending(slot.Key, slot.HasRelease, slot.Release, slot.Repeats);
        slot = default;
        return true;
    }

    private int FindPress(int virtualKey)
    {
        var found = -1;
        var seq = int.MaxValue;
        for (var i = 0; i < _slots.Length; i++)
        {
            ref var slot = ref _slots[i];
            if (!slot.Live || slot.Key.Up || slot.Key.VirtualKey != virtualKey || slot.Seq >= seq)
                continue;

            found = i;
            seq = slot.Seq;
        }

        return found;
    }

    // A scan of zero was not reported. It matches the other side's scan.
    private static bool ScansMatch(int waiting, int packet) => waiting == 0 || packet == 0 || waiting == packet;
}
