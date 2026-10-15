using System.Runtime.InteropServices;
using System.Threading.Channels;
using Windows.Win32;
using Winland;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

public sealed class KeyboardHook : IDisposable
{
    private const uint CloseMessage = 0x0010;

    // Marks events Winland injects, so its own hook lets them through untouched.
    internal const nuint InjectedTag = 0x574C4E44;

    // An unassigned virtual key. Pressed under Super or Alt, it makes the shell treat the
    // modifier as part of a chord, so its release does not open Start or a menu bar.
    private const ushort MaskKey = 0xE8;

    // Long enough for a held key to start repeating, so a held key is never taken as stale.
    private const int SweepDelayMs = 1500;
    private const int WatchPeriodMs = 2000;
    private const int UnseenInputMs = 1000;

    private static readonly (string Name, ushort Vk)[] Modifiers =
    [
        ("Super_L", 0x5B), ("Super_R", 0x5C),
        ("Control_L", 0xA2), ("Control_R", 0xA3),
        ("Alt_L", 0xA4), ("Alt_R", 0xA5),
        ("Shift_L", 0xA0), ("Shift_R", 0xA1),
    ];

    private readonly Core _core;
    private readonly BindSession _session;
    private readonly HOOKPROC _callback;
    private readonly Channel<Queued> _queued = Channel.CreateUnbounded<Queued>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        // The callback must return before any dispatcher work. Continuations stay off this thread.
        AllowSynchronousContinuations = false,
    });

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
    private nint _layoutHandle = unchecked((nint)(-1));
    private volatile int _inhibited;
    private bool _listening;
    private volatile bool _disposed;

    internal const uint LongPressMessage = 0x8000 + 24;
    internal const uint SweepMessage = 0x8000 + 25;
    internal const uint WatchMessage = 0x8000 + 26;
    internal const uint ReconcileMessage = 0x8000 + 27;
    private const uint InputLanguageMessage = 0x0051;

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
        }

        DisarmSweep();
        DisarmLongPress();
        StopWorker();
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
            case InputLanguageMessage:
                _layoutHandle = unchecked((nint)(-1));
                return false;
            default:
                return false;
        }
    }

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
            if (!up && !info.flags.HasFlag(KBDLLHOOKSTRUCT_FLAGS.LLKHF_INJECTED))
                InputClock.NoteClickOrKey();

            var vk = (int)info.vkCode;
            if (KeyNames.PassesThrough(vk))
                return Next(code, wParam, lParam);

            var extended = info.flags.HasFlag(KBDLLHOOKSTRUCT_FLAGS.LLKHF_EXTENDED);
            var scan = info.scanCode > int.MaxValue ? 0 : (int)info.scanCode;
            var name = KeyNames.Name(vk, scan, extended, ActiveLayout(), TrackedModifiers());
            var xkb = KeyNames.XkbCode(vk, scan, extended);
            Input input = up
                ? new KeyReleased(name) { ScanCode = xkb }
                : new KeyPressed(name) { ScanCode = xkb };
            if (!up)
                SessionEffects.NoteKey();
            _session.Inhibit(_inhibited != 0);
            var decision = _core.Decide(input, _session);
            NoteTiming(decision);
            Enqueue(decision);

            // Sent now, while Super or Alt is still down, so it lands before their release.
            if (decision.Mask)
                Mask();

            if (decision.Consumed)
                return new LRESULT(1);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }

        return Next(code, wParam, lParam);
    }

    private void OnSession(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is not (Microsoft.Win32.SessionSwitchReason.SessionLock or Microsoft.Win32.SessionSwitchReason.SessionUnlock))
            return;

        var inhibited = e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock;
        _inhibited = inhibited ? 1 : 0;
        _queued.Writer.TryWrite(new Queued(QueuedKind.Inhibit, "", "", inhibited));

        // Keys released on the lock screen were never seen. The session lives on the hook thread.
        if (!inhibited)
            Post(ReconcileMessage);
    }

    private void Reconcile()
    {
        BindMatcher.Forget(_session, IsDown);
        ArmSweep();
    }

    // A modifier the OS holds down that the user is not holding: left by a release lost
    // before this daemon ran, or on a desktop the hook cannot see. Release it.
    private void ReleaseStale()
    {
        var down = new List<string>();
        foreach (var (name, vk) in Modifiers)
        {
            if (Down(vk))
                down.Add(name);
        }

        foreach (var name in BindMatcher.Stale(_session, down))
        {
            foreach (var (modifier, vk) in Modifiers)
            {
                if (!modifier.Equals(name, StringComparison.Ordinal))
                    continue;

                Console.Error.WriteLine($"Releasing {name}, which was left down.");
                Send(Key(vk, up: true));
            }
        }
    }

    internal static void Mask() => Send(Key(MaskKey, up: false), Key(MaskKey, up: true));

    private static void Send(params ReadOnlySpan<INPUT> inputs)
    {
        if (PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            Console.Error.WriteLine($"Input was not sent ({Marshal.GetLastPInvokeError()}).");
    }

    private static INPUT Key(ushort vk, bool up)
    {
        var flags = up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
        if (vk is 0x5B or 0x5C or 0xA3 or 0xA5)
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;

        return new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                ki = new KEYBDINPUT
                {
                    wVk = (VIRTUAL_KEY)vk,
                    dwFlags = flags,
                    dwExtraInfo = InjectedTag,
                },
            },
        };
    }

    private static bool Down(int vk) => (PInvoke.GetAsyncKeyState(vk) & 0x8000) != 0;

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
            handle = KeyboardLayout.ForegroundHandle();
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
        var reader = _queued.Reader;
        while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            while (reader.TryRead(out var queued))
                Perform(queued);
        }
    }

    private void Perform(Queued queued)
    {
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

            Apply(_core.Handle(new Dispatch(queued.Dispatcher, queued.Arguments)));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
        }
    }

    private void StopWorker()
    {
        _queued.Writer.TryComplete();
        _worker?.Join(TimeSpan.FromSeconds(2));
        _worker = null;
    }

    private static void Apply(Outcome outcome)
    {
        if (outcome.Close is WindowId window)
            _ = PInvoke.PostMessage(new HWND((nint)window.Value), CloseMessage, default, default);

        WindowAdapter.Apply(outcome);
    }

    private LRESULT Next(int code, WPARAM wParam, LPARAM lParam) =>
        PInvoke.CallNextHookEx(_hook, code, wParam, lParam);

    internal void NoteTiming(BindDecision decision)
    {
        if (decision.DisarmLongPress)
            DisarmLongPress();
        if (decision.ArmLongPressMs is int delay)
            ArmLongPress(delay);
    }

    internal void Enqueue(BindDecision decision)
    {
        if (decision.Dispatches.Count > 0)
        {
            foreach (var dispatch in decision.Dispatches)
            {
                if (dispatch.Dispatcher.Length == 0)
                    continue;

                PostDispatch(dispatch.Dispatcher, dispatch.Arguments);
            }

            return;
        }

        if (!string.IsNullOrEmpty(decision.Dispatcher))
            PostDispatch(decision.Dispatcher, decision.Arguments);
    }

    internal void PostDispatch(string dispatcher, string arguments) =>
        _queued.Writer.TryWrite(new Queued(QueuedKind.Dispatch, dispatcher, arguments, false));

    internal void PostGesture(string dispatcher, string arguments, Point position, PointerDragPhase phase, string button) =>
        _queued.Writer.TryWrite(new Queued(QueuedKind.Gesture, dispatcher, arguments, false, position, phase, button));

    internal void PostMove(Point position, uint time) =>
        _queued.Writer.TryWrite(new Queued(QueuedKind.Move, "", "", false, position, TimeMs: time, HasTime: true));

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
        bool HasTime = false);
}
