using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

public sealed partial class KeyboardHook : IDisposable
{

    // Marks events Winland injects, so its own hook lets them through untouched.
    internal const nuint InjectedTag = 0x574C4E44;

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

    private readonly WindowManager _core;
    private readonly Action<Outcome> _apply;
    private readonly BindSession _session;
    private readonly HOOKPROC _callback;
    // The callback must return before any dispatcher work, so it only posts here. Moves that pile
    // up while the worker is busy collapse into the newest, so they never delay a key bind.
    private readonly InputQueue<Queued> _queued = new(Replaces, Join);

    private readonly object _timerGate = new();
    private HHOOK _hook;
    private Thread? _worker;
    private Timer? _sweepTimer;
    private Timer? _watchTimer;
    private int _lastSeen;
    private long _startedAt;
    private uint _hookThread;
    private volatile KeyLayout _layout = KeyLayout.UnitedStates;
    private volatile nint _layoutHandle = unchecked((nint)(-1));
    private bool _listening;
    private volatile bool _disposed;

    internal const uint SweepMessage = 0x8000 + 25;
    internal const uint WatchMessage = 0x8000 + 26;
    internal const uint ReconcileMessage = 0x8000 + 27;
    internal const uint InputLanguageMessage = 0x0051;

    internal BindSession Session => _session;

    public KeyboardHook(WindowManager core, Action<Outcome> apply)
        : this(core, core.KeyState, apply)
    {
    }

    public KeyboardHook(WindowManager core, BindSession session, Action<Outcome> apply)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(apply);
        _core = core;
        _apply = apply;
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

            // Nothing updates it once the hook is gone, so the relay must not trust it.
            Volatile.Write(ref _held, 0);
            for (var i = 0; i < _down.Length; i++)
                Volatile.Write(ref _down[i], 0);
        }

        DisarmSweep();
        StopWorker();
    }

    /// <summary>Handles a thread message this hook posted. Call on the thread that started it.</summary>
    internal bool Service(uint message)
    {
        switch (message)
        {
            case SweepMessage:
                if (!_disposed)
                    ReleaseStale();
                return true;
            case ReconcileMessage:
                if (!_disposed)
                    Reconcile();
                return true;
            default:
                return false;
        }
    }

    /// <summary>The input language changed. The layout is read again on the next key.</summary>
    internal void ForgetLayout()
    {
        _layoutsStale = true;
        _layoutHandle = unchecked((nint)(-1));
    }

    private volatile bool _layoutsStale;

    /// <summary>
    /// True when input arrived that no hook saw. Windows removes a low-level hook without
    /// notice when its callback is too slow, so this is the only sign that it is gone.
    /// </summary>
    internal bool Lost()
    {
        // A touchpad gesture with no cursor movement is input no hook sees, and reinstalling would drop a drag.
        if (_disposed || _hook.IsNull || _session.Inhibited || TouchpadFingers.Touched(Environment.TickCount64))
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

            NoteHeld(info.vkCode, up);

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

            // A release keeps the name its press had, though a bind may have focused a window with another layout
            // in between, so the bind session finds the key it holds.
            var name = KeyName(vk, extended, up, () => KeyNames.Name(vk, scan, extended, ActiveLayout(), TrackedModifiers()));
            var xkb = KeyNames.XkbCode(vk, scan, extended);
            if (!up)
                SessionEffects.NoteKey();

            var edge = new KeyEdge(vk, scan, extended, up, name, xkb);
            if (!Deliver(edge).Passes)
                return new LRESULT(1);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }

        return Next(code, wParam, lParam);
    }

    /// <summary>One key edge from the low-level hook. <see cref="Xkb"/> is the code a bind matches.</summary>
    internal readonly record struct KeyEdge(int VirtualKey, int Scan, bool Extended, bool Up, string Name, int Xkb);

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

    // The names keys were pressed with, by virtual key, the extended ones apart. The hook thread alone reads them.
    private readonly string?[] _pressedNames = new string?[512];

    private string KeyName(int vk, bool extended, bool up, Func<string> name)
    {
        var slot = (vk & 0xFF) | (extended ? 0x100 : 0);
        if (up)
        {
            var pressed = _pressedNames[slot];
            _pressedNames[slot] = null;
            return pressed ?? name();
        }

        return _pressedNames[slot] = name();
    }

    // Each layout read so far, by its handle. Switching between programs with different layouts reads none again.
    private readonly Dictionary<nint, KeyLayout> _layouts = [];

    // The foreground thread's layout. Read once per handle.
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

        if (_layoutsStale)
        {
            _layoutsStale = false;
            _layouts.Clear();
        }

        if (_layouts.TryGetValue(handle, out var known))
        {
            _layout = known;
            _layoutHandle = handle;
            return _layout;
        }

        try
        {
            var layout = KeyboardLayout.Read(handle);
            _layouts[handle] = layout;
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
}
