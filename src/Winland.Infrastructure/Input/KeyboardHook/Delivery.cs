namespace Winland.Infrastructure;

/// <summary>How the hook decides a key edge, and the raw input packets that later name its keyboard for a device bind.</summary>
public sealed partial class KeyboardHook : IDisposable
{
    /// <summary>How the hook treated one edge. The decision is synchronous. A device bind waits for the raw packet.</summary>
    internal readonly record struct TrackedResult(bool Passes, BindDecision Decision);

    /// <summary>What one raw packet did for the press it paired with, including repeats and a release already seen.</summary>
    internal readonly record struct DeviceDelivery(bool Handled, BindDecision Decision, int Repeats, BindDecision? Repeated, BindDecision? Release);

    /// <summary>The hook's decision for one key edge. Every key is decided now. A device bind runs later, from its packet.</summary>
    internal TrackedResult DeliverTracked(KeyEdge edge, bool repeated)
    {
        var vk = edge.VirtualKey;
        if ((uint)vk <= 255 && (edge.Up || !repeated))
            _givenBack[vk] = false;

        var applied = ApplyKey(edge);
        var passes = applied.ReachesWindows && !(repeated && (uint)vk <= 255 && _givenBack[vk]);
        if (_core.Binds.HasDeviceBind && (uint)vk <= 255)
        {
            if (edge.Up)
                NoteDeviceRelease(vk);
            else if (!repeated && passes)
                TrackPassed(edge);
            else if (repeated)
            {
                var open = NewestOpen(vk);
                if (open >= 0 && !_passed[open].PressFired)
                    NoteDeviceRepeat(open);
            }
        }

        return new TrackedResult(passes, applied.Decision);
    }

    /// <summary>The raw packet for a key the hook already passed. An unnamed keyboard matches no device bind.</summary>
    internal bool DeliverPacket(int virtualKey, int scan, bool up, long handle, out DeviceDelivery delivery)
    {
        delivery = default;
        try
        {
            if (_disposed)
                return false;

            string? device = null;
            if (_devices.TryName(handle, out var named))
                device = named;
            else
                _devices.AskRefresh();

            return DeliverDevice(virtualKey, scan, up, device, out delivery);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
            return false;
        }
    }

    /// <summary>
    /// Pairs a key-down packet with the oldest press of that key the hook has passed and not yet named. A key-up
    /// packet does nothing: the hook sees the release first. An empty device still closes the press, and matches nothing.
    /// </summary>
    internal bool DeliverDevice(int virtualKey, int scan, bool up, string? device, out DeviceDelivery delivery)
    {
        delivery = default;
        if (up || (uint)virtualKey > 255)
            return false;

        var index = OldestUnfired(virtualKey, scan);
        if (index < 0)
            return false;

        ref var slot = ref _passed[index];
        var named = !string.IsNullOrEmpty(device);
        var press = RunDevice(slot, device, up: false, repeat: false, remember: named && !slot.ReleaseSeen);
        BindDecision? repeated = null;
        var repeats = slot.Repeats;
        for (var i = 0; i < repeats; i++)
            repeated = RunDevice(slot, device, up: false, repeat: true, remember: false);

        slot.PressFired = true;
        slot.Repeats = 0;
        BindDecision? release = null;
        if (slot.ReleaseSeen)
        {
            release = RunDevice(slot, device, up: true, repeat: false, remember: false);
            slot.Live = false;
        }

        delivery = new DeviceDelivery(true, press, repeats, repeated, release);
        return true;
    }

    internal bool DeviceRefreshPending => _devices.RefreshPending;

    private BindDecision RunDevice(in PassedPress slot, string? device, bool up, bool repeat, bool remember)
    {
        var decision = _session.DispatchDevice(
            _core.Binds,
            slot.Name,
            slot.Xkb,
            up,
            repeat,
            device,
            remember,
            submap: slot.Submap,
            pressedUnder: slot.PressedUnder,
            anchor: slot.Anchor,
            order: slot.Order);
        if (!up && !repeat && BeginKeyDrag(slot.Name, decision))
        {
            NoteTiming(decision);
            return decision;
        }

        Enqueue(decision);
        NoteTiming(decision);
        return decision;
    }

    // A mouse dispatcher bound to the key. The key already reached the window. With nothing under the pointer and no
    // other dispatcher, there is no drag to start.
    private bool BeginKeyDrag(string name, BindDecision decision)
    {
        if (KeyDrag(decision) is not BindDispatch drag)
            return false;
        if (Drag.Current is null && MouseHook.OnlyDrag(decision) && !_core.HasDragTarget(CursorPoint()))
            return true;

        if (!Drag.Begin(name, drag.Dispatcher, drag.Arguments))
            return false;

        PostGesture(drag.Dispatcher, drag.Arguments, CursorPoint(), PointerDragPhase.Begin, name);
        foreach (var dispatch in decision.Dispatches)
        {
            if (dispatch.Dispatcher.Length > 0 && !dispatch.Drag)
                PostDispatch(dispatch.Dispatcher, dispatch.Arguments, decision.Trigger);
        }

        return true;
    }

    private readonly record struct AppliedKey(bool ReachesWindows, BindDecision Decision);

    // Decides the edge with no keyboard. A release uses the keyboard its press packet remembered.
    private AppliedKey ApplyKey(KeyEdge edge)
    {
        Input input = edge.Up
            ? new KeyReleased(edge.Name) { ScanCode = edge.Xkb }
            : new KeyPressed(edge.Name) { ScanCode = edge.Xkb };

        // The mouse dispatcher bound to a key drags while the key is held: its repeats are swallowed, and its release
        // drops the window.
        var draggedByKey = Drag.Current is BindDrag.Active held && held.Button.Equals(edge.Name, StringComparison.OrdinalIgnoreCase);
        if (draggedByKey && !edge.Up)
            return new AppliedKey(false, new BindDecision(true, null));
        if (draggedByKey && Drag.Release(edge.Name) is BindDrag.Active dropped)
            PostGesture(dropped.Dispatcher, dropped.Arguments, CursorPoint(), PointerDragPhase.End, edge.Name);

        var decision = _core.Decide(input, _session);
        NoteTiming(decision);

        // With no window under the pointer a key whose bind only drags goes on to the focused window, as the pinned
        // release lets the event through.
        var keyDrag = edge.Up ? null : KeyDrag(decision);
        if (keyDrag is not null && Drag.Current is null && MouseHook.OnlyDrag(decision) && !_core.HasDragTarget(CursorPoint()))
            return new AppliedKey(true, decision);

        if (keyDrag is BindDispatch drag && Drag.Begin(edge.Name, drag.Dispatcher, drag.Arguments))
        {
            PostGesture(drag.Dispatcher, drag.Arguments, CursorPoint(), PointerDragPhase.Begin, edge.Name);
            foreach (var dispatch in decision.Dispatches)
            {
                if (dispatch.Dispatcher.Length > 0 && !dispatch.Drag)
                    PostDispatch(dispatch.Dispatcher, dispatch.Arguments, decision.Trigger);
            }

            return new AppliedKey(false, decision);
        }

        // A key drag is swallowed for the drag and is not given back.
        SwallowedKey? replay = null;
        if (decision.PassesOnFailure && edge.VirtualKey is >= 0 and <= ushort.MaxValue)
        {
            var scan = edge.Scan is >= 0 and <= ushort.MaxValue ? (ushort)edge.Scan : (ushort)0;
            replay = new SwallowedKey((ushort)edge.VirtualKey, scan, edge.Extended, edge.Name, !edge.Up);
        }

        Enqueue(decision, replay);
        if (draggedByKey)
            return new AppliedKey(false, decision);

        // Sent now, while Super or Alt is still down, so it lands before their release.
        if (decision.Mask)
            Mask();

        return new AppliedKey(!decision.Consumed, decision);
    }

    // A press the hook passed. The packet that names its keyboard is posted after the hook returns, and a later edge
    // can run before that packet is pumped, so several presses stay here in order.
    private struct PassedPress
    {
        public bool Live;
        public int Seq;
        public int VirtualKey;
        public bool PressFired;
        public bool ReleaseSeen;
        public byte Repeats;
        public int Scan;
        public int Xkb;
        public string Name;
        public Modifier PressedUnder;
        public Point Anchor;
        public string Submap;
        public (string Key, int ScanCode)[] Order;
    }

    private void TrackPassed(KeyEdge edge)
    {
        var slot = FreeSlot();
        var under = _session.PressedUnderOf(edge.Name);
        _passed[slot] = new PassedPress
        {
            Live = true,
            Seq = ++_passedSeq,
            VirtualKey = edge.VirtualKey,
            Scan = edge.Scan,
            Xkb = edge.Xkb,
            Name = edge.Name,
            PressedUnder = under,
            Anchor = _session.Pointer,
            Submap = _session.Remembered(edge.Name, mouse: false),
            Order = _session.HeldInOrder(),
        };
    }

    private int FreeSlot()
    {
        var free = -1;
        var oldest = 0;
        var oldestSeq = int.MaxValue;
        for (var i = 0; i < _passed.Length; i++)
        {
            if (!_passed[i].Live)
            {
                free = i;
                break;
            }

            if (_passed[i].Seq < oldestSeq)
            {
                oldestSeq = _passed[i].Seq;
                oldest = i;
            }
        }

        return free >= 0 ? free : oldest;
    }

    private int NewestOpen(int virtualKey)
    {
        var found = -1;
        var seq = int.MinValue;
        for (var i = 0; i < _passed.Length; i++)
        {
            ref var slot = ref _passed[i];
            if (!slot.Live || slot.ReleaseSeen || slot.VirtualKey != virtualKey || slot.Seq < seq)
                continue;

            seq = slot.Seq;
            found = i;
        }

        return found;
    }

    // The oldest press of this key that no packet has named. A scan that does not match that oldest press waits,
    // rather than pairing with a newer one.
    private int OldestUnfired(int virtualKey, int scan)
    {
        var found = -1;
        var seq = int.MaxValue;
        for (var i = 0; i < _passed.Length; i++)
        {
            ref var slot = ref _passed[i];
            if (!slot.Live || slot.PressFired || slot.VirtualKey != virtualKey || slot.Seq >= seq)
                continue;

            seq = slot.Seq;
            found = i;
        }

        if (found < 0 || !ScansMatch(_passed[found].Scan, scan))
            return -1;

        return found;
    }

    private static bool ScansMatch(int waiting, int packet) => waiting == 0 || packet == 0 || waiting == packet;

    private void NoteDeviceRelease(int virtualKey)
    {
        var open = NewestOpen(virtualKey);
        if (open < 0)
            return;

        ref var slot = ref _passed[open];
        if (slot.PressFired)
        {
            slot.Live = false;
            return;
        }

        slot.ReleaseSeen = true;
    }

    private void NoteDeviceRepeat(int open)
    {
        ref var slot = ref _passed[open];
        if (slot.PressFired || slot.Repeats >= DeviceRepeatCap)
            return;

        slot.Repeats++;
    }
}
