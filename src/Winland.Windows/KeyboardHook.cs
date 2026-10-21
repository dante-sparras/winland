using System.Runtime.InteropServices;
using Windows.Win32;
using Winland;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

public sealed class KeyboardHook : IDisposable
{

    // Marks events Winland injects, so its own hook lets them through untouched.
    internal const nuint InjectedTag = 0x574C4E44;

    // A key kb_options moved, sent again as its new key. Unlike InjectedTag, the hook handles it as typed.
    internal const nuint RemapTag = 0x574C4E52;

    // A button press the mouse hook held back and sent again, or the middle button it made from the left and right
    // together. Unlike InjectedTag, the hook handles it as clicked, and does not hold it again.
    internal const nuint EmulationTag = 0x574C4E45;

    // The keys kb_options took that are held now. Used on the hook thread only.
    private readonly HashSet<int> _taken = [];

    // An unassigned virtual key. Pressed under Super or Alt, it makes the shell treat the
    // modifier as part of a chord, so its release does not open Start or a menu bar.
    internal const ushort MaskKey = 0xE8;

    // Long enough for a held key to start repeating, so a held key is never taken as stale.
    private const int EscapeKey = 0x1B;
    private const int SweepDelayMs = 1500;
    private const int WatchPeriodMs = 2000;
    private const int UnseenInputMs = 1000;

    // Input this much newer than the last the hook saw means the hook may have missed a key.
    private const int CurrentSlackMs = 100;

    // Presses the hook let through while a non-consuming device bind might still match them. The raw packet arrives
    // after the hook returns, on this thread, and pairs with the oldest of these. An older press is dropped once eight
    // are waiting. The key already reached the window, so dropping it does not swallow it.
    internal const int PassedCapacity = 8;

    // Repeats that arrive before the packet that names the keyboard. Later repeats are the hook's.
    internal const int DeviceRepeatCap = 8;

    private static readonly (string Name, ushort Vk)[] Modifiers =
    [
        ("Super_L", 0x5B), ("Super_R", 0x5C),
        ("Control_L", 0xA2), ("Control_R", 0xA3),
        ("Alt_L", 0xA4), ("Alt_R", 0xA5),
        ("Shift_L", 0xA0), ("Shift_R", 0xA1),
    ];

    // The modifier keys the user physically holds, one bit per entry of Modifiers, as the hook saw
    // them. Kept by virtual key rather than by bind name, so AltGr and the Left Ctrl Windows sends
    // with it count too. Only the hook thread writes it; the key relay reads it on the bind worker.
    private static int _held;

    // Every key the user physically holds, one bit per virtual key, as the hook saw them, a key a bind swallows too.
    // Only the hook thread writes it.
    private static readonly long[] _down = new long[4];

    // Modifiers a relay pressed and left down on purpose. Not stale.
    private static int _relayed;

    private readonly Core _core;
    private readonly BindSession _session;
    private readonly HOOKPROC _callback;
    // The callback must return before any dispatcher work, so it only posts here. Moves that pile
    // up while the worker is busy collapse into the newest, so they never delay a key bind.
    private readonly InputQueue<Queued> _queued = new(Replaces);
    private readonly KeyDeviceSource _devices;
    private readonly PassedPress[] _passed = new PassedPress[PassedCapacity];
    private int _passedSeq;

    // A press this hook swallowed and then injected, still held. Its physical repeats are swallowed: Windows repeats
    // the injected press, and passing the hardware repeats as well would deliver the key twice.
    private readonly bool[] _givenBack = new bool[256];
    private readonly Queue<(SwallowedKey Key, IReadOnlyList<string> Release, bool? StillDown)> _replays = new();
    private readonly Lock _replayGate = new();

    private readonly object _timerGate = new();
    private HHOOK _hook;
    private Thread? _worker;
    private Timer? _longPressTimer;
    private Timer? _sweepTimer;
    private Timer? _watchTimer;
    private int _lastSeen;
    private long _startedAt;
    private uint _hookThread;
    private volatile KeyLayout _layout = KeyLayout.UnitedStates;
    private volatile nint _layoutHandle = unchecked((nint)(-1));
    private volatile int _inhibited;
    private bool _listening;
    private volatile bool _disposed;

    internal const uint LongPressMessage = 0x8000 + 24;
    internal const uint SweepMessage = 0x8000 + 25;
    internal const uint WatchMessage = 0x8000 + 26;
    internal const uint ReconcileMessage = 0x8000 + 27;
    internal const uint ReplayMessage = 0x8000 + 28;
    internal const uint DeviceRefreshMessage = 0x8000 + 29;
    internal const uint InputLanguageMessage = 0x0051;

    internal BindSession Session => _session;

    public KeyboardHook(Core core)
        : this(core, core.KeyState)
    {
    }

    public KeyboardHook(Core core, BindSession session)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(session);
        _core = core;
        _session = session;
        _callback = OnKeyboard;
        _devices = new KeyDeviceSource(() => Post(DeviceRefreshMessage), (vk, scan, up, handle) => DeliverPacket(vk, scan, up, handle, out _));
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_hook.IsNull)
            return;

        _hookThread = PInvoke.GetCurrentThreadId();
        _worker = new Thread(Pump)
        {
            IsBackground = true,
            Name = "Winland binds",
        };
        _worker.Start();

        _ = PInvoke.PeekMessage(out _, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _callback, default(HINSTANCE), 0);
        if (_hook.IsNull)
        {
            StopWorker();
            throw new InvalidOperationException($"The keyboard hook was not installed ({Marshal.GetLastPInvokeError()}).");
        }

        InputClock.Watch();
        Saw();
        _startedAt = Environment.TickCount64;
        ArmSweep();
        _watchTimer = new Timer(_ => Post(WatchMessage), null, WatchPeriodMs, WatchPeriodMs);
        _devices.Start(_core.Devices);

        try
        {
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSession;
            _listening = true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException or NotSupportedException)
        {
            Console.Error.WriteLine(exception.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_listening)
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSession;
            _listening = false;
        }

        _watchTimer?.Dispose();
        _watchTimer = null;
        if (!_hook.IsNull)
        {
            _ = PInvoke.UnhookWindowsHookEx(_hook);
            _hook = default;

            // Runs on the hook thread after the last event, so the session is current. A key
            // held since before the hook started has had no time to repeat, so it is left alone.
            if (Environment.TickCount64 - _startedAt >= SweepDelayMs)
                ReleaseStale();

            // Nothing updates it once the hook is gone, so the relay must not trust it.
            Volatile.Write(ref _held, 0);
            for (var i = 0; i < _down.Length; i++)
                Volatile.Write(ref _down[i], 0);
        }

        DisarmSweep();
        DisarmLongPress();

        // The message loop has exited, so a posted replay would never be pumped. Inject what was swallowed directly.
        if (_hookThread != 0 && _hookThread == PInvoke.GetCurrentThreadId())
        {
            DisarmLongPress();
            ReplayPending();
        }

        StopWorker();
        _devices.Dispose();
    }

    /// <summary>Handles a thread message this hook posted. Call on the thread that started it.</summary>
    internal bool Service(uint message)
    {
        switch (message)
        {
            case LongPressMessage:
                CheckLongPress();
                return true;
            case SweepMessage:
                if (!_disposed)
                    ReleaseStale();
                return true;
            case ReconcileMessage:
                if (!_disposed)
                    Reconcile();
                return true;
            case ReplayMessage:
                if (!_disposed)
                    ReplayPending();
                return true;
            case DeviceRefreshMessage:
                if (!_disposed)
                    _devices.Refresh();
                return true;
            default:
                return false;
        }
    }

    /// <summary>The input language changed. The layout is read again on the next key.</summary>
    internal void ForgetLayout() => _layoutHandle = unchecked((nint)(-1));

    /// <summary>
    /// True when input arrived that no hook saw. Windows removes a low-level hook without
    /// notice when its callback is too slow, so this is the only sign that it is gone.
    /// </summary>
    internal bool Lost()
    {
        if (_disposed || _hook.IsNull || _inhibited != 0)
            return false;

        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!PInvoke.GetLastInputInfo(ref info))
            return false;

        return unchecked((int)info.dwTime - Volatile.Read(ref _lastSeen)) > UnseenInputMs;
    }

    internal void Reinstall()
    {
        if (_disposed)
            return;

        var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _callback, default(HINSTANCE), 0);
        if (hook.IsNull)
        {
            Console.Error.WriteLine($"The keyboard hook was not reinstalled ({Marshal.GetLastPInvokeError()}).");
            return;
        }

        var old = _hook;
        _hook = hook;
        if (!old.IsNull)
            _ = PInvoke.UnhookWindowsHookEx(old);

        Console.Error.WriteLine("The keyboard hook was reinstalled.");
        Saw();
        Reconcile();
    }

    internal void Saw() => Volatile.Write(ref _lastSeen, Environment.TickCount);

    // The system drops this hook if the callback is slow. Decide from the published
    // table and return; a worker runs the dispatchers under the core lock.
    private LRESULT OnKeyboard(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code < 0 || _disposed)
            return Next(code, wParam, lParam);

        try
        {
            Saw();
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (info.dwExtraInfo == InjectedTag)
                return Next(code, wParam, lParam);

            var up = info.flags.HasFlag(KBDLLHOOKSTRUCT_FLAGS.LLKHF_UP);
            var vk = (int)info.vkCode;
            var extended = info.flags.HasFlag(KBDLLHOOKSTRUCT_FLAGS.LLKHF_EXTENDED);
            var scan = info.scanCode > int.MaxValue ? 0 : (int)info.scanCode;

            // kb_options: a key an option moves goes on as the key it moves to, and a grp: combination switches layouts.
            // The raw packet names the original key, which this swallows, so a device bind does not see it.
            if (info.dwExtraInfo != RemapTag && Remap(info, up))
                return new LRESULT(1);

            var repeated = !up && IsDown((int)info.vkCode);
            NoteHeld(info.vkCode, up);

            // misc:key_press_enables_dpms: any key turns the displays back on after dpms turned them off.
            if (_core.KeyWakesDisplays)
                PostDispatch("dpms", "on");

            if (!up && !info.flags.HasFlag(KBDLLHOOKSTRUCT_FLAGS.LLKHF_INJECTED))
                InputClock.NoteClickOrKey(info.time);

            // Escape gives up a click to kill, and goes no further.
            if (vk == EscapeKey && !up && _core.ClickKillArmed)
            {
                PostInput(new ClickKillChosen(null));
                return new LRESULT(1);
            }

            if (KeyNames.PassesThrough(vk))
                return Next(code, wParam, lParam);

            var name = KeyNames.Name(vk, scan, extended, ActiveLayout(), TrackedModifiers());
            var xkb = KeyNames.XkbCode(vk, scan, extended);
            if (!up)
                SessionEffects.NoteKey();

            var edge = new KeyEdge(vk, scan, extended, up, name, xkb);
            if (!DeliverTracked(edge, repeated).Passes)
                return new LRESULT(1);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }

        return Next(code, wParam, lParam);
    }

    /// <summary>One key edge from the low-level hook. <see cref="Xkb"/> is the code a bind matches.</summary>
    internal readonly record struct KeyEdge(int VirtualKey, int Scan, bool Extended, bool Up, string Name, int Xkb);

    /// <summary>How the hook treated one edge. The decision is synchronous. A device bind waits for the raw packet.</summary>
    internal readonly record struct TrackedResult(bool Passes, BindDecision Decision);

    /// <summary>What one raw packet did for the press it paired with, including repeats and a release already seen.</summary>
    internal readonly record struct DeviceDelivery(bool Handled, BindDecision Decision, int Repeats, BindDecision? Repeated, BindDecision? Release);

    /// <summary>The hook's decision for one key edge. Every key is decided now. A device bind runs later, from its packet.</summary>
    internal TrackedResult DeliverTracked(KeyEdge edge, bool repeated)
    {
        _session.Inhibit(_inhibited != 0);
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
        var decision = BindMatcher.DispatchDevice(
            _core.Binds,
            _session,
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
        _session.Inhibit(_inhibited != 0);

        // The mouse dispatcher bound to a key drags while the key is held: its repeats are swallowed, and its release
        // drops the window.
        var draggedByKey = Drag.Current is MouseDrag.Active held && held.Button.Equals(edge.Name, StringComparison.OrdinalIgnoreCase);
        if (draggedByKey && !edge.Up)
            return new AppliedKey(false, new BindDecision(true, null));
        if (draggedByKey && Drag.Release(edge.Name) is MouseDrag.Active dropped)
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
        _session.PressedUnder.TryGetValue(edge.Name, out var under);
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
            Order = _session.Order.ToArray(),
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

    private void OnSession(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is not (Microsoft.Win32.SessionSwitchReason.SessionLock or Microsoft.Win32.SessionSwitchReason.SessionUnlock))
            return;

        var inhibited = e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock;
        _inhibited = inhibited ? 1 : 0;
        _queued.Post(new Queued(QueuedKind.Inhibit, "", "", inhibited));

        // Keys released on the lock screen were never seen. The session lives on the hook thread.
        if (!inhibited)
            Post(ReconcileMessage);
    }

    private void Reconcile()
    {
        BindMatcher.Forget(_session, IsDown);

        // A release the hook missed leaves its bit set. A key still held sets it again when it repeats.
        var held = Volatile.Read(ref _held);
        for (var i = 0; i < Modifiers.Length; i++)
        {
            if (!Down(Modifiers[i].Vk))
                held &= ~(1 << i);
        }

        Volatile.Write(ref _held, held);
        ArmSweep();
    }

    /// <summary>
    /// The watch tick, on the hook thread. A modifier can be left down at any time, for one by a
    /// relay that pressed it again just after the user let go, so the sweep runs on every tick.
    /// It waits while the hook may have missed a key, or a held key would be taken as stale.
    /// </summary>
    internal void Watch()
    {
        if (_disposed || _inhibited != 0 || Environment.TickCount64 - _startedAt < SweepDelayMs || !Current())
            return;

        ReleaseStale();
    }

    // True when the hook has seen the newest input, so what it holds is up to date.
    private bool Current()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!PInvoke.GetLastInputInfo(ref info))
            return false;

        return unchecked((int)info.dwTime - Volatile.Read(ref _lastSeen)) <= CurrentSlackMs;
    }

    // A modifier the OS holds down that the user is not holding: left by a release lost
    // before this daemon ran, on a desktop the hook cannot see, or by a relay. Release it.
    private void ReleaseStale()
    {
        var kept = Volatile.Read(ref _held) | Volatile.Read(ref _relayed);
        for (var i = 0; i < Modifiers.Length; i++)
        {
            var (name, vk) = Modifiers[i];
            if ((kept & (1 << i)) != 0 || !Down(vk))
                continue;

            Console.Error.WriteLine($"Releasing {name}, which was left down.");

            // Letting go of Super or Alt alone opens Start or a menu bar. The mask key makes it a chord.
            if (vk is 0x5B or 0x5C or 0xA4 or 0xA5)
                InputInjector.Post(Key(MaskKey, up: false), Key(MaskKey, up: true), Key(vk, up: true));
            else
                InputInjector.Post(Key(vk, up: true));
        }
    }

    /// <summary>True when the user physically holds this modifier key, as the hook last saw.</summary>
    internal static bool Held(int vk)
    {
        var bit = ModifierBit(vk);
        return bit != 0 && (Volatile.Read(ref _held) & bit) != 0;
    }

    /// <summary>A relay pressed a modifier and left it down on purpose, or let it go.</summary>
    internal static void NoteRelayed(int vk, bool down)
    {
        var bit = ModifierBit(vk);
        if (bit == 0)
            return;

        if (down)
            Interlocked.Or(ref _relayed, bit);
        else
            Interlocked.And(ref _relayed, ~bit);
    }

    /// <summary>Whether the user holds the key with this virtual key code, as the running hook saw it.</summary>
    public static bool IsDown(int virtualKey)
    {
        if (virtualKey is < 0 or > 255)
            return false;

        return (Volatile.Read(ref _down[virtualKey >> 6]) & (1L << (virtualKey & 63))) != 0;
    }

    private static void NoteHeld(uint vk, bool up)
    {
        if (vk <= 255)
        {
            var word = Volatile.Read(ref _down[vk >> 6]);
            var mask = 1L << (int)(vk & 63);
            Volatile.Write(ref _down[vk >> 6], up ? word & ~mask : word | mask);
        }

        var bit = ModifierBit((int)vk);
        if (bit == 0)
            return;

        var held = Volatile.Read(ref _held);
        Volatile.Write(ref _held, up ? held & ~bit : held | bit);
    }

    private static int ModifierBit(int vk)
    {
        for (var i = 0; i < Modifiers.Length; i++)
        {
            if (Modifiers[i].Vk == vk)
                return 1 << i;
        }

        return 0;
    }

    // Called from the mouse hook, so it is sent from the injector's thread.
    internal static void Mask() => InputInjector.Post(Key(MaskKey, up: false), Key(MaskKey, up: true));

    // True when the key event was taken: moved to another key, dropped, or used to switch layouts.
    private bool Remap(KBDLLHOOKSTRUCT info, bool up)
    {
        var options = _core.KeyOptions;
        var vk = (int)info.vkCode;

        // A key taken here never reaches Windows' key state, so its repeats are told apart by what is held here.
        var repeat = !up && (Down(vk) || _taken.Contains(vk));
        if (options.Toggle == LayoutToggle.Caps && vk == KeyboardOptions.CapsLock)
        {
            if (!up && !repeat)
                PostInput(new LayoutCycled());

            return Take(info, vk, up);
        }

        if (!up && !repeat && Toggles(options.Toggle, vk))
            PostInput(new LayoutCycled());

        if (!options.Remaps.TryGetValue(vk, out var to))
            return false;

        Take(info, vk, up);

        if (to != 0)
        {
            var moved = Key((ushort)to, up, (ushort)PInvoke.MapVirtualKey((uint)to, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC));
            moved.Anonymous.ki.dwExtraInfo = RemapTag;
            InputInjector.Post(moved);
        }

        return true;
    }

    private bool Take(KBDLLHOOKSTRUCT info, int vk, bool up)
    {
        if (up)
        {
            _taken.Remove(vk);
            return true;
        }

        _taken.Add(vk);
        if (!info.flags.HasFlag(KBDLLHOOKSTRUCT_FLAGS.LLKHF_INJECTED))
            InputClock.NoteClickOrKey(info.time);

        return true;
    }

    // The grp: combinations: the key pressed with the one already held completes the pair.
    private static bool Toggles(LayoutToggle toggle, int vk)
    {
        bool Shift() => Down(0x10);
        bool Control() => Down(0x11);
        bool Alt() => Down(0x12);
        var isShift = vk is 0xA0 or 0xA1;
        var isControl = vk is 0xA2 or 0xA3;
        var isAlt = vk is 0xA4 or 0xA5;
        return toggle switch
        {
            LayoutToggle.AltShift => (isShift && Alt()) || (isAlt && Shift()),
            LayoutToggle.CtrlShift => (isShift && Control()) || (isControl && Shift()),
            LayoutToggle.CtrlAlt => (isControl && Alt()) || (isAlt && Control()),
            LayoutToggle.WinSpace => vk == 0x20 && (Down(0x5B) || Down(0x5C)),
            LayoutToggle.AltSpace => vk == 0x20 && Alt(),
            LayoutToggle.BothAlts => (vk == 0xA4 && Down(0xA5)) || (vk == 0xA5 && Down(0xA4)),
            _ => false,
        };
    }

    internal static void Send(params ReadOnlySpan<INPUT> inputs)
    {
        if (PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            Console.Error.WriteLine($"Input was not sent ({Marshal.GetLastPInvokeError()}).");
    }

    internal static INPUT Key(ushort vk, bool up, ushort scan = 0, bool extended = false)
    {
        var flags = up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
        if (extended || vk is 0x5B or 0x5C or 0xA3 or 0xA5 or 0x90)
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;

        return new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                ki = new KEYBDINPUT
                {
                    wVk = (VIRTUAL_KEY)vk,
                    wScan = scan,
                    dwFlags = flags,
                    dwExtraInfo = InjectedTag,
                },
            },
        };
    }

    private static bool Down(int vk) => (PInvoke.GetAsyncKeyState(vk) & 0x8000) != 0;

    private Point CursorPoint() => PInvoke.GetCursorPos(out var point) ? new Point(point.X, point.Y) : _session.Pointer;

    // The drag of a mouse dispatcher a key's bind ran, if any.
    private static BindDispatch? KeyDrag(BindDecision decision)
    {
        foreach (var dispatch in decision.Dispatches)
        {
            if (dispatch.Drag && MouseHook.IsDrag(dispatch.Dispatcher, dispatch.Arguments))
                return dispatch;
        }

        return null;
    }

    private bool IsDown(string key)
    {
        if (key.StartsWith("mouse:", StringComparison.Ordinal))
        {
            return key switch
            {
                // GetAsyncKeyState reads the physical buttons, which may be swapped.
                "mouse:272" or "mouse:273" => Down(0x01) || Down(0x02),
                "mouse:274" => Down(0x04),
                "mouse:275" => Down(0x05),
                "mouse:276" => Down(0x06),
                _ => false,
            };
        }

        if (KeyNames.TryVirtualKey(key, out var vk, out _) && Down(vk))
            return true;

        return _layout.TryVirtualKey(key, out vk) && Down(vk);
    }

    // The foreground thread's layout. Rebuilt only when that handle changes.
    private KeyLayout ActiveLayout()
    {
        nint handle;
        try
        {
            // input:resolve_binds_by_sym off: binds read keys in the first layout the config names, whatever is in use.
            var first = _core.BindsInFirstLayout ? SessionEffects.FirstLayout : 0;
            handle = first != 0 ? first : KeyboardLayout.ForegroundHandle();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return _layout;
        }

        if (handle == _layoutHandle)
            return _layout;

        try
        {
            var layout = KeyboardLayout.Read(handle);
            _layout = layout;
            _layoutHandle = handle;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }

        return _layout;
    }

    private KeyModifierState TrackedModifiers()
    {
        var held = _session.Held;
        return new KeyModifierState(
            (held & Modifier.Shift) != 0,
            (held & Modifier.Ctrl) != 0,
            (held & Modifier.Alt) != 0,
            (held & Modifier.Super) != 0);
    }

    private void ArmSweep()
    {
        lock (_timerGate)
        {
            _sweepTimer?.Dispose();
            _sweepTimer = new Timer(_ => Post(SweepMessage), null, SweepDelayMs, Timeout.Infinite);
        }
    }

    private void DisarmSweep()
    {
        lock (_timerGate)
        {
            _sweepTimer?.Dispose();
            _sweepTimer = null;
        }
    }

    private void Post(uint message)
    {
        if (_disposed || _hookThread == 0)
            return;

        _ = PInvoke.PostThreadMessage(_hookThread, message, default, default);
    }

    private void Pump()
    {
        while (_queued.Take() is { Count: > 0 } batch)
        {
            foreach (var queued in batch)
                Perform(queued);
        }
    }

    // A pointer move, or a move of the same drag, makes the one still waiting pointless.
    private static bool Replaces(Queued waiting, Queued next) => (waiting.Kind, next.Kind) switch
    {
        (QueuedKind.Move, QueuedKind.Move) => true,
        (QueuedKind.Gesture, QueuedKind.Gesture) =>
            waiting.Phase == PointerDragPhase.Move
            && next.Phase == PointerDragPhase.Move
            && waiting.Button == next.Button
            && waiting.Dispatcher == next.Dispatcher
            && waiting.Arguments == next.Arguments,
        _ => false,
    };

    private void Perform(Queued queued)
    {
        var noted = false;
        try
        {
            if (queued.Kind == QueuedKind.Inhibit)
            {
                _core.Handle(new InputInhibited(queued.Inhibited));
                return;
            }

            if (queued.Kind == QueuedKind.Gesture)
            {
                Apply(_core.Handle(new PointerGesture(queued.Dispatcher, queued.Position, queued.Phase, queued.Button) { Arguments = queued.Arguments }));
                return;
            }

            if (queued.Kind == QueuedKind.Move)
            {
                var moved = new PointerMoved(queued.Position);
                if (queued.HasTime)
                    moved = moved with { TimeMs = queued.TimeMs };

                Apply(_core.Handle(moved));
                return;
            }

            if (queued.Kind == QueuedKind.Input && queued.Event is { } input)
            {
                Apply(_core.Handle(input));
                return;
            }

            var outcome = _core.Handle(new Dispatch(queued.Dispatcher, queued.Arguments) { Trigger = queued.Trigger });
            Apply(outcome);
            noted = NoteConsumption(queued, outcome.Error is null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
            if (!noted)
                NoteConsumption(queued, false);
        }
    }

    private static bool NoteConsumption(Queued queued, bool succeeded)
    {
        if (queued.Consumption is not { } note)
            return true;

        note.Note(succeeded);
        return true;
    }

    private void StopWorker()
    {
        _queued.Complete();
        _worker?.Join(TimeSpan.FromSeconds(2));
        _worker = null;
    }

    // The adapter asks the window to close, after its close animation when it has one.
    private static void Apply(Outcome outcome) => WindowAdapter.Apply(outcome);

    private LRESULT Next(int code, WPARAM wParam, LPARAM lParam) =>
        PInvoke.CallNextHookEx(_hook, code, wParam, lParam);

    internal void NoteTiming(BindDecision decision)
    {
        if (decision.DisarmLongPress)
            DisarmLongPress();
        if (decision.ArmLongPressMs is int delay)
            ArmLongPress(delay);
    }

    internal void Enqueue(BindDecision decision, SwallowedKey? replay = null)
    {
        BindConsumption? batch = null;
        if (decision.PassesOnFailure && replay is SwallowedKey key)
            batch = new BindConsumption(() => PostReplay(key, decision.ReleaseOnFailure));

        if (decision.Dispatches.Count > 0)
        {
            foreach (var dispatch in decision.Dispatches)
                PostConsumed(dispatch.Dispatcher, dispatch.Arguments, decision.Trigger, batch);

            return;
        }

        if (!string.IsNullOrEmpty(decision.Dispatcher))
            PostConsumed(decision.Dispatcher, decision.Arguments, decision.Trigger, batch);
    }

    private void PostConsumed(string dispatcher, string arguments, BindTrigger? trigger, BindConsumption? batch)
    {
        if (dispatcher.Length == 0)
            return;

        batch?.Expect();
        _queued.Post(new Queued(
            QueuedKind.Dispatch,
            dispatcher,
            arguments,
            false,
            Trigger: trigger,
            Consumption: batch));
    }

    private void PostReplay(SwallowedKey key, IReadOnlyList<string> release, bool? stillDown = null)
    {
        lock (_replayGate)
            _replays.Enqueue((key, release, stillDown));

        Post(ReplayMessage);
    }

    // On the hook thread. Keys caught for the failed bind are let go, then the key is injected from the injector's
    // thread so this hook is not re-entered while it decides.
    private void ReplayPending()
    {
        while (true)
        {
            (SwallowedKey Key, IReadOnlyList<string> Release, bool? StillDown) next;
            lock (_replayGate)
            {
                if (_replays.Count == 0)
                    return;

                next = _replays.Dequeue();
            }

            _session.CaughtKeys.Remove(next.Key.Name);
            foreach (var name in next.Release)
                _session.CaughtKeys.Remove(name);

            var stillDown = next.StillDown ?? IsDown((int)next.Key.VirtualKey);
            if (next.Key.Pressed && stillDown && next.Key.VirtualKey <= 255)
                _givenBack[next.Key.VirtualKey] = true;

            InputInjector.Post(ReplayInputs(next.Key, stillDown));
        }
    }

    /// <summary>
    /// The input that gives a swallowed key back. A press the user still holds is a press. One they have already
    /// released is a press and a release together. A release edge is the release.
    /// </summary>
    internal static INPUT[] ReplayInputs(SwallowedKey key, bool stillDown)
    {
        if (!key.Pressed)
            return [Key(key.VirtualKey, up: true, key.Scan, key.Extended)];

        var down = Key(key.VirtualKey, up: false, key.Scan, key.Extended);
        if (stillDown)
            return [down];

        return [down, Key(key.VirtualKey, up: true, key.Scan, key.Extended)];
    }

    /// <summary>The drag a mouse bind started, from a mouse button or a key, which pointer moves carry on.</summary>
    internal MouseDrag Drag { get; } = new();

    internal void PostDispatch(string dispatcher, string arguments, BindTrigger? trigger = null) =>
        _queued.Post(new Queued(QueuedKind.Dispatch, dispatcher, arguments, false, Trigger: trigger));

    internal void PostGesture(string dispatcher, string arguments, Point position, PointerDragPhase phase, string button) =>
        _queued.Post(new Queued(QueuedKind.Gesture, dispatcher, arguments, false, position, phase, button));

    internal void PostMove(Point position, uint time) =>
        _queued.Post(new Queued(QueuedKind.Move, "", "", false, position, TimeMs: time, HasTime: true));

    internal void PostInput(Input input) =>
        _queued.Post(new Queued(QueuedKind.Input, "", "", false, Event: input));

    internal void CheckLongPress()
    {
        if (_disposed)
            return;

        DisarmLongPress();
        Enqueue(_core.Decide(new LongPressElapsed(), _session));
    }

    private void ArmLongPress(int delay)
    {
        lock (_timerGate)
        {
            _longPressTimer?.Dispose();
            _longPressTimer = new Timer(OnLongPressDue, null, delay, Timeout.Infinite);
        }
    }

    private void DisarmLongPress()
    {
        lock (_timerGate)
        {
            _longPressTimer?.Dispose();
            _longPressTimer = null;
        }
    }

    private void OnLongPressDue(object? _) => Post(LongPressMessage);

    private enum QueuedKind : byte
    {
        Dispatch = 1,
        Inhibit = 2,
        Gesture = 3,
        Move = 4,
        Input = 5,
    }

    private readonly record struct Queued(
        QueuedKind Kind,
        string Dispatcher,
        string Arguments,
        bool Inhibited,
        Point Position = default,
        PointerDragPhase Phase = PointerDragPhase.Begin,
        string Button = "",
        long TimeMs = 0,
        bool HasTime = false,
        Input? Event = null,
        BindTrigger? Trigger = null,
        BindConsumption? Consumption = null);
}
