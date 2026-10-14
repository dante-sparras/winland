using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Winland.Platform;

/// <summary>
/// Low-level keyboard hook. Physical Super is swallowed so Winoma owns its chords, in every window.
/// The hook is installed once. <see cref="HookRepair"/> installs it again only on evidence: Windows stopped calling it,
/// a call came late, a Super went past it, or the session unlocked.
/// A Super another hook still hands to Windows is adopted, masked so Start stays closed, and chorded.
/// A Super key-up that reaches Windows past the hook, seen through raw input, drops the press so typing resumes.
/// Any other key Windows was given is held only while Windows holds it, which the ledger asks on the next key-down (ADR 0064).
/// A stroke this process sends is marked and passes. Another program's Super is a chord like a physical one.
/// What Windows sees is decided by <see cref="ChordLedger"/>. This hook only reports the key and performs the result.
/// It tells the ledger when delivery is withheld. It does not keep a second queue.
/// A session lock drops every finger the ledger still holds. The cloak of the foreground window is sampled on the hook thread, outside the hook callback.
/// Every key on the machine waits for the hook thread. Nothing on it moves a window or lists processes, and the callback does not write the log.
/// </summary>
public sealed class KeyboardHook : IKeyboardHook
{
    private readonly ConcurrentQueue<(Chord Chord, bool Down, long QueuedAt)> _pending = new();
    private readonly ConcurrentQueue<Native.Input[]> _synth = new();
    private readonly ConcurrentQueue<(bool Warning, string Text)> _hookNotes = new();
    private readonly ChordLedger _ledger = new(EmptyBinds.Instance, OsDown);
    private bool _wake;
    private readonly Native.HookProc _proc;
    private readonly Action? _superDown;
    private readonly Thread _thread;
    private nint _hook;
    private uint _threadId;
    private int _disposed;
    private readonly object _yieldGate = new();
    private readonly PointerYield _yield = new();
    private readonly HoverFocus _hover = new();
    private int _cursorX;
    private int _cursorY;
    private bool _haveCursor;
    private bool _suspend;
    private bool _hoverMoved;
    private int _foregroundCloaked;
    private bool _rawWatching;
    private const uint TurnMs = 250;
    private const uint QuietTurnMs = 2000;
    private const long SettleMs = 2000;
    private const uint WaitTimeout = 0x102;
    private const int ImagePathChars = 520;

    // Hook thread only. The pump turns every TurnMs until this tick, then every QuietTurnMs.
    private long _settleUntil;

    // Hook thread only. A hook call, a session message, or a foreground event left work for AfterTurn.
    private bool _afterDue;

    // Hook thread only. False until the cloak is sampled in this turn of the pump.
    private bool _cloakFresh;
    private readonly HookRepair _repair = new();
    private readonly Func<bool> _hookCannotSee;
    private int _lateMs;
    private nint _foregroundSeen;
    private bool _foregroundElevated;
    private bool _inHook;
    private bool _adopted;
    private bool _superReleased;
    private int _superVk = Native.VkLWin;
    private int _superPresses;
    private bool _superStamped;
    private Native.WinEventProc? _startProc;
    private nint _startHook;
    private nint _sessionWindow;
    private nint _sessionProcPointer;
    private Native.WndProc? _sessionProc;
    private readonly object _releaseGate = new();
    private ManualResetEventSlim? _releaseDone;
    private static int _superHeld;
    private bool _escapeCancels;
    private bool _escapeTaken;
    private IBindTable? _claims;
    private IGlobalBindSink? _globalSink;
    private static readonly Chord BareEscape = new(ChordModifiers.None, ChordKey.Escape);

    private bool _fullscreenKeyTaken;
    private bool _withholdLapsed;
    private bool _droppedHeld;
    private long _beat;
    private long _stalledSince;
    private const long StallMs = 500;
    private const uint RawInputStack = 256;

    /// <summary>True while the ledger records a Super finger. A focus must not join thread input then.</summary>
    public static bool SuperIsHeld => Volatile.Read(ref _superHeld) != 0;

    /// <summary>Takes Super's state from the hook process, when the hook runs there. See <see cref="RemoteKeyboardHook"/>.</summary>
    /// <param name="held">True while a Super finger is held.</param>
    internal static void PublishRemoteSuper(bool held) => Volatile.Write(ref _superHeld, held ? 1 : 0);

    /// <summary>A chord is waiting. Raised on the hook thread after the hook has returned. Do not run the chord here.</summary>
    public event Action? ChordQueued;

    /// <summary>The last Super finger went up. Raised on the hook thread after the hook has returned; post the work elsewhere.</summary>
    public event Action? SuperReleased;

    /// <summary>Escape went down while <see cref="EscapeCancels"/> was set. Raised on the hook thread; post the work elsewhere.</summary>
    public event Action? EscapeTaken;

    /// <summary>Never raised: <see cref="TakeHover"/> answers at once in this process.</summary>
    public event Action? HoverReady
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <summary>While true, a physical Escape is kept from the focused window and raised as <see cref="EscapeTaken"/>. The desktop sets it while a plugin holds the Escape claim.</summary>
    public bool EscapeCancels
    {
        get => Volatile.Read(ref _escapeCancels);
        set => Volatile.Write(ref _escapeCancels, value);
    }

    /// <inheritdoc />
    public event Action<int>? PickerKeyTaken;

    /// <summary>
    /// Reports a picker key without waiting. The desktop sets this so the hook does not call the shell.
    /// Null raises <see cref="PickerKeyTaken"/> instead.
    /// </summary>
    public Action<int>? ReportPicker { get; set; }

    /// <summary>
    /// Reports a global bind's key edge without waiting. The desktop sets this so the hook does not call the sink.
    /// Null calls the sink, or raises <see cref="EscapeTaken"/> when there is none.
    /// </summary>
    public Action<string, bool, bool>? ReportGlobal { get; set; }

    /// <inheritdoc />
    public bool PickerTakesKeys
    {
        get => ShellBridge.Keys.Open;
        set => ShellBridge.Keys.Open = value;
    }

    /// <summary>Creates the hook thread.</summary>
    /// <param name="superDown">
    /// Runs on the hook thread, inside the hook, when Super goes from released to held. It must return at once.
    /// The desktop starts its drag poll here. Null does nothing.
    /// </param>
    public KeyboardHook(Action? superDown = null)
    {
        _superDown = superDown;
        _proc = Hook;
        _hookCannotSee = ForegroundElevated;
        // Every key on the machine waits on this thread, and Windows removes a hook that misses its timeout.
        // It does little work, so it runs ahead of ordinary threads when the machine is busy.
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "winoma-hook",
            Priority = ThreadPriority.Highest,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        ElevatedBroker.Keyed += NoteElevatedSuper;
    }

    /// <summary>The elevated helper saw the desktop's Super go up. The next key drops that press.</summary>
    private void NoteElevatedSuper(HookWire message)
    {
        if (message is HookWire.Super { Held: false })
        {
            ForgetSuper();
        }
    }

    /// <inheritdoc />
    public Func<bool>? FullscreenKey { get; set; }

    /// <summary>Uses <paramref name="chords"/> for the hook. Call this before <see cref="Install"/>.</summary>
    /// <param name="chords">The binds. A page action is already gated.</param>
    public void UseChords(IBindTable chords) => _ledger.Use(chords);

    /// <inheritdoc />
    public void UseBinds(IBindTable binds)
    {
        ArgumentNullException.ThrowIfNull(binds);
        _claims = binds;
    }

    /// <inheritdoc />
    public void UseSink(IGlobalBindSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _globalSink = sink;
    }

    /// <summary>
    /// Releases Super and the other modifiers. A crash can leave Super down, and then no window accepts typing until reboot.
    /// </summary>
    public static void ReleaseStuckModifiers()
    {
        KeyStroke.ReleaseSuper();
        KeyStroke.ReleaseModifiers();
    }

    /// <summary>Starts the hook thread.</summary>
    public void Install()
    {
        ChordKeys.EnsureComplete();
        KeyStroke.EnsureLayout();
        ReleaseStuckModifiers();

        // A new hook holds no Super. A hook process that ended with one held left it stamped, and the elevated helper would chord every key on it.
        // This also opens the shared stamp here, so the first key's hook call does not.
        HookStamp.DesktopSuper(null);
        ShellBridge.Session.Arm(ReleaseForLock);
        _thread.Start();
    }

    /// <summary>Keys the hook accepted, for the UI thread to run.</summary>
    public bool TryTake(out Chord chord, out bool down, out long queuedAt)
    {
        if (_pending.TryDequeue(out var item))
        {
            chord = item.Chord;
            down = item.Down;
            queuedAt = item.QueuedAt;
            return true;
        }

        chord = default;
        down = false;
        queuedAt = 0;
        return false;
    }

    /// <inheritdoc />
    public void Forward(Chord chord, bool down)
    {
        _pending.Enqueue((chord, down, System.Diagnostics.Stopwatch.GetTimestamp()));
        ChordQueued?.Invoke();
    }

    /// <inheritdoc />
    public void ForgetSuper()
    {
        _ledger.NoteSuperUp();
        QueueInformation("super key-up went to an administrator window; the next key drops the press");
    }

    /// <inheritdoc />
    public void KeyboardMoved()
    {
        // A Workspace switch is the shell's own call from start to finish, so no key waits for it to end.
        // The pump wakes and samples the window that has the keyboard: a quiet pump would do that up to QuietTurnMs late.
        var thread = _threadId;
        if (thread != 0 && Volatile.Read(ref _disposed) == 0)
        {
            Native.PostThreadMessage(thread, Native.WmNull, 0, 0);
        }
    }

    /// <summary>
    /// Logs when the hook thread stays in one turn. Every key on the machine waits for that thread, so a stall is a stall in typing.
    /// The beat is the tick the thread last woke at, and zero while it waits for a key, so the limit does not depend on how long it waits.
    /// Notes that thread queued, including a hook-stamp failure, are written here.
    /// Call from the UI timer.
    /// </summary>
    public void WatchStall()
    {
        DrainHookNotes();

        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        var beat = Volatile.Read(ref _beat);
        if (beat == 0)
        {
            if (_stalledSince != 0)
            {
                Log.Information("Chord", "keyboard hook thread turned again after " + (now - _stalledSince) + " ms");
                _stalledSince = 0;
            }

            return;
        }

        if (now - beat >= StallMs)
        {
            if (_stalledSince == 0)
            {
                _stalledSince = beat;
                Log.Warning("Chord", "keyboard hook thread has not turned for " + (now - beat) + " ms; typing waits on it");
            }

            return;
        }

        if (_stalledSince != 0)
        {
            Log.Information("Chord", "keyboard hook thread turned again after " + (beat - _stalledSince) + " ms");
            _stalledSince = 0;
        }
    }

    private void Run()
    {
        _threadId = Native.GetCurrentThreadId();
        Native.PeekMessage(out _, 0, 0, 0, 0);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _hook = Native.SetWindowsHookEx(Native.WhKeyboardLl, _proc, Native.GetModuleHandle(null), 0);
        var hookError = Marshal.GetLastWin32Error();
        if (Volatile.Read(ref _disposed) != 0)
        {
            if (_hook != 0)
            {
                Native.UnhookWindowsHookEx(_hook);
                _hook = 0;
            }

            return;
        }

        if (_hook == 0)
        {
            Log.Error("Chord", "keyboard hook did not install; Win32 error " + hookError);
            return;
        }

        Log.Information("Chord", "keyboard hook installed");
        Pump();
    }

    /// <summary>
    /// Drops every finger and injects the key-ups before Windows locks.
    /// A lock from outside Winoma arrives as a session notification on the hook thread.
    /// </summary>
    public void ReleaseForLock()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var thread = _threadId;
        if (thread == 0)
        {
            return;
        }

        if (thread == Native.GetCurrentThreadId())
        {
            ReleaseForLockNow();
            return;
        }

        var done = new ManualResetEventSlim(false);
        lock (_releaseGate)
        {
            _releaseDone = done;
        }

        if (!Native.PostThreadMessage(thread, Native.WmReleaseSuper, 0, 0))
        {
            lock (_releaseGate)
            {
                if (ReferenceEquals(_releaseDone, done))
                {
                    _releaseDone = null;
                }
            }

            Log.Warning("Chord", "could not ask the hook to release Super before lock; Win32 error " + Marshal.GetLastWin32Error());
            return;
        }

        if (!done.Wait(TimeSpan.FromMilliseconds(500)))
        {
            Log.Warning("Chord", "the hook did not release Super before lock");
        }

        lock (_releaseGate)
        {
            if (ReferenceEquals(_releaseDone, done))
            {
                _releaseDone = null;
            }
        }
    }

    /// <summary>
    /// Pumps messages until the thread receives a quit.
    /// The hook is installed once, before this runs. It goes in again only when <see cref="HookRepair"/> has evidence.
    /// Installing it again on a timer to sit first would fight every other hook on the machine for that place.
    /// </summary>
    public void Pump()
    {
        OpenSessionWindow();
        WatchStart();
        SampleCloak();
        Settle();
        while (true)
        {
            var turn = Quiet() ? QuietTurnMs : TurnMs;
            Volatile.Write(ref _beat, 0);

            // A hook call is a sent message. It wakes this wait and runs inside the peek below,
            // and the work it leaves runs right after, not when the next posted message arrives.
            var woke = Native.MsgWaitForMultipleObjectsEx(0, 0, turn, Native.QsAllInput, Native.MwmoInputAvailable);
            Volatile.Write(ref _beat, Environment.TickCount64);
            if (woke != WaitTimeout)
            {
                Settle();
            }

            _cloakFresh = false;
            _afterDue = true;
            if (!DrainMessages())
            {
                break;
            }

            // Each posted message was followed by its own. This one is for a hook call that ran inside the last peek, and for a wait that timed out.
            if (_afterDue)
            {
                AfterTurn();
            }
        }

        var hook = Interlocked.Exchange(ref _hook, 0);
        if (hook != 0)
        {
            Native.UnhookWindowsHookEx(hook);
        }

        CloseStartWatch();
        CloseSessionWindow();
        FlushSynth();
        FlushHeld();
        FlushYield();
    }

    /// <summary>Handles every waiting message. False once the thread receives a quit.</summary>
    private bool DrainMessages()
    {
        while (Native.PeekMessage(out var message, 0, 0, 0, Native.PmRemove))
        {
            if (message.Message == Native.WmQuit)
            {
                return false;
            }

            if (message.Message == Native.WmReleaseSuper)
            {
                FinishRelease();
            }

            Native.TranslateMessage(ref message);
            Native.DispatchMessage(ref message);
            AfterTurn();
        }

        return true;
    }

    /// <summary>A key, a message, or a foreground change just came. The pump keeps its short turn for SettleMs more.</summary>
    private void Settle() => _settleUntil = Environment.TickCount64 + SettleMs;

    /// <summary>
    /// True when a turn of the pump has nothing to find but a foreground window that changed workspace without an event.
    /// A key, a message, and a foreground change each wake the thread at once, whatever the turn.
    /// Waiting keys, a hidden window that has the keyboard, and a repair that waits out its gap keep the short turn.
    /// </summary>
    private bool Quiet() =>
        Environment.TickCount64 >= _settleUntil
        && !ForegroundCloaked()
        && !_repair.Pending
        && !_ledger.HasWithheld;

    /// <summary>The work a hook call or a message left for after the hook returned.</summary>
    private void AfterTurn()
    {
        _afterDue = false;
        FlushSynth();
        FlushHeld();
        FlushYield();
        if (_adopted)
        {
            _adopted = false;
            PublishSuper();
            _repair.SuperWentPast();
            QueueInformation("super reached Windows past the hook; adopted the press");
        }

        if (_wake)
        {
            _wake = false;
            ChordQueued?.Invoke();
        }

        if (_superReleased)
        {
            _superReleased = false;
            SuperReleased?.Invoke();
        }

        ReportNotes();
        Repair();
    }

    /// <summary>Installs the hook again when <see cref="HookRepair"/> has a reason whose gap has passed.</summary>
    private void Repair()
    {
        if (!_repair.Pending)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (_repair.Due(now) is not HookRepairReason reason)
        {
            return;
        }

        if (!Reinstall())
        {
            _repair.Failed(now);
            return;
        }

        _repair.Installed(reason, now);
        switch (reason)
        {
            case HookRepairReason.Gone:
                QueueWarning("keys reached Windows without the keyboard hook; Windows had dropped it, installed it again");
                break;
            case HookRepairReason.Late:
                QueueWarning("a key reached the keyboard hook " + _lateMs + " ms late; installed it again before Windows drops it");
                break;
            case HookRepairReason.Behind:
                QueueInformation("another hook sits ahead of the keyboard hook; installed it again");
                break;
            case HookRepairReason.Unlocked:
                QueueInformation("session unlocked; installed the keyboard hook again");
                break;
        }
    }

    /// <summary>True when the foreground window belongs to an elevated process. Windows does not call this hook for its keys.</summary>
    private bool ForegroundElevated()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd != _foregroundSeen)
        {
            _foregroundSeen = hwnd;
            _foregroundElevated = hwnd != 0 && ShellBridge.Windows.IsElevated(hwnd);
        }

        return _foregroundElevated;
    }

    /// <summary>
    /// Closes the Windows Start menu whenever it takes the foreground. Winoma replaces it.
    /// The hook keeps Super from opening it. This catches any other way it opens.
    /// The same event turns raw keyboard input off while one of Winoma's own windows has the keyboard. See <see cref="FollowOwnForeground"/>.
    /// </summary>
    private void WatchStart()
    {
        _startProc = OnForeground;
        _startHook = Native.SetWinEventHook(Native.EventSystemForeground, Native.EventSystemForeground, 0, _startProc, 0, 0, 0);
        if (_startHook == 0)
        {
            Log.Warning("Chord", "start menu watch did not install");
        }
    }

    private void CloseStartWatch()
    {
        if (_startHook != 0)
        {
            Native.UnhookWinEvent(_startHook);
            _startHook = 0;
        }
    }

    private void OnForeground(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (hwnd == 0)
        {
            return;
        }

        _afterDue = true;
        _cloakFresh = false;
        _ = Native.GetWindowThreadProcessId(hwnd, out var pid);
        FollowOwnForeground(pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId || !IsStart(pid))
        {
            return;
        }

        KeyStroke.Send(KeyStroke.Down(Native.VkEscape), KeyStroke.Up(Native.VkEscape));
        QueueInformation("closed the Windows Start menu");
    }

    /// <summary>
    /// True when the process is the Start menu's, by its image name without the extension.
    /// This runs on the hook thread for every foreground change, so it does not list every process, and it reads the name on the stack.
    /// </summary>
    private static unsafe bool IsStart(uint pid)
    {
        var process = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, pid);
        if (process == 0)
        {
            return false;
        }

        try
        {
            var name = stackalloc char[ImagePathChars];
            var size = ImagePathChars;
            return Native.QueryFullProcessImageName(process, 0, name, ref size)
                && size > 0
                && size <= ImagePathChars
                && WindowsStart.IsStart(Path.GetFileNameWithoutExtension(new ReadOnlySpan<char>(name, size)));
        }
        finally
        {
            Native.CloseHandle(process);
        }
    }

    /// <summary>Wakes the hook thread so keys kept off a hidden workspace are typed once this one has the keyboard.</summary>
    public void DeliverHeld()
    {
        if (!_ledger.HasWithheld)
        {
            return;
        }

        var thread = _threadId;
        if (thread != 0)
        {
            Native.PostThreadMessage(thread, Native.WmNull, 0, 0);
        }
    }

    /// <summary>Reads the pointer. Hover-focus returns when it moved and no key other than Super is held.</summary>
    public void SamplePointer()
    {
        if (!Native.GetCursorPos(out var point))
        {
            return;
        }

        var resume = false;
        lock (_yieldGate)
        {
            var moved = _haveCursor && (point.X != _cursorX || point.Y != _cursorY);
            _cursorX = point.X;
            _cursorY = point.Y;
            _haveCursor = true;
            // The ledger is asked only while the pointer is yielded, so a move with no key in play takes no second lock.
            if (moved && _yield.Yielded && _yield.Moved(_ledger.KeyHeld()))
            {
                resume = true;
            }

            if (moved && !_yield.Yielded)
            {
                _hoverMoved = true;
            }
        }

        if (resume)
        {
            _hover.Resume();
        }
    }

    /// <summary>True once after the pointer moves while no key other than Super is held. The next call is false until it moves again.</summary>
    public bool TakeHover()
    {
        lock (_yieldGate)
        {
            if (!_hoverMoved || _yield.Yielded)
            {
                return false;
            }

            _hoverMoved = false;
            return true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ElevatedBroker.Keyed -= NoteElevatedSuper;

        var hook = Interlocked.Exchange(ref _hook, 0);
        if (hook != 0)
        {
            Native.UnhookWindowsHookEx(hook);
        }

        _hover.Resume();
        ShellBridge.Session.Disarm(ReleaseForLock);

        KeyStroke.ReleaseSuper();
        KeyStroke.ReleaseModifiers();
        var thread = _threadId;
        if (thread != 0)
        {
            Native.PostThreadMessage(thread, Native.WmQuit, 0, 0);
        }
    }

    private nint Hook(int code, nint wParam, nint lParam)
    {
        // The Start mask is sent from inside this call and comes back through it, and while the hook
        // is installed again both copies are briefly in the chain. Each event is decided once.
        if (code < 0 || _inHook)
        {
            return Native.CallNextHookEx(_hook, code, wParam, lParam);
        }

        _inHook = true;
        _afterDue = true;
        try
        {
            return Decide(code, wParam, lParam);
        }
        finally
        {
            _inHook = false;
        }
    }

    private nint Decide(int code, nint wParam, nint lParam)
    {
        // Every key on the machine passes here. A pointer read does not box the struct the way PtrToStructure does.
        Native.KbdLlHook info;
        unsafe
        {
            info = *(Native.KbdLlHook*)lParam;
        }

        var message = (int)wParam;
        var down = message is Native.WmKeyDown or Native.WmSysKeyDown;
        var up = message is Native.WmKeyUp or Native.WmSysKeyUp;
        if (!down && !up)
        {
            return Native.CallNextHookEx(_hook, code, wParam, lParam);
        }

        var vk = (int)info.VkCode;

        // Any F11 but the one Winoma sends after the picture is up, so a script's F11 is covered too.
        if (vk == Native.VkF11 && info.DwExtraInfo != KeyStroke.OwnStroke && TakeFullscreenKey(down))
        {
            return 1;
        }

        if ((info.Flags & Native.LlkhfInjected) != 0)
        {
            // Winoma's own strokes pass. Another program's Super is a chord like a physical one,
            // and so is the key it presses with it. Anything else it types passes.
            // The key-up of a key the ledger swallowed is not passed: that key went down while Super was held,
            // and once Super is up this key-up is the one that takes the finger out of the ledger.
            // Only another program's key-up asks the ledger, so its key-downs and Winoma's own strokes take no lock here.
            if (info.DwExtraInfo == KeyStroke.OwnStroke
                || (!Super(vk) && !_ledger.HoldingSuper && !(up && _ledger.HoldsSwallowed(vk))))
            {
                return Native.CallNextHookEx(_hook, code, wParam, lParam);
            }
        }
        else
        {
            HookStamp.Took(unchecked((int)info.Time), unchecked((int)info.VkCode));

            // Both clocks are GetTickCount. A call that starts this late may already have been skipped by Windows.
            var lag = unchecked(Environment.TickCount - (int)info.Time);
            if (_repair.HookCalled(lag))
            {
                _lateMs = lag;
            }

            if (vk != Native.VkEscape && TakeForPicker(vk, down))
            {
                return 1;
            }

            if (vk == Native.VkEscape && TakeEscape(down))
            {
                return 1;
            }
        }

        var now = Environment.TickCount64;
        if (Super(vk))
        {
            if (down)
            {
                _superVk = vk;
            }

            // Windows already holds this Super: another hook handed it the press. Adopt it.
            if (OsDown(vk) && _ledger.AdoptSuper(PhysicalKey.Super(vk), now))
            {
                _adopted = true;
                MaskStart();
            }
            else if (up && _ledger.HoldingSuper)
            {
                // Windows opens Start on a Super release when it saw no other key.
                // The chord key was swallowed, so this release would be that tap.
                // Sent before this call returns, while Super is still down for the shell.
                MaskStart();
            }
        }
        else if (down && !_ledger.HoldingSuper && AdoptWindowsSuper(now))
        {
            MaskStart();
        }

        NoteWithhold();
        var effect = _ledger.OnKey(ChordKeys.ToPhysical(vk), down, Environment.TickCount64);

        // After the ledger has the finger, so a pointer move in between does not find the pointer yielded with no key held.
        TrackYield(vk, down);
        PublishSuper();
        Apply(effect);
        _droppedHeld |= effect.Dropped;

        if (!effect.Deliver)
        {
            return 1;
        }

        // Queued pulses, including a withhold backlog, go out before this real key.
        FlushSynth();
        return Native.CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>
    /// True when the shell keeps this key event. While the claim is open, every key-down but the one the claim passes is kept, and the
    /// ledger never sees a kept key, so it queues no chord for it. With the claim closed this is one read of a flag, and a set that has nothing in it.
    /// </summary>
    private bool TakeForPicker(int vk, bool down)
    {
        var keys = ShellBridge.Keys;
        if (down && !keys.Open)
        {
            return false;
        }

        var super = down && _ledger.HoldingSuper;
        if (!keys.Take(vk, down, super, down && OsDown(Native.VkControl), down && OsDown(Native.VkMenu), out var claimed))
        {
            return false;
        }

        if (claimed is int key)
        {
            if (ReportPicker is { } report)
            {
                report(key);
            }
            else
            {
                PickerKeyTaken?.Invoke(key);
            }
        }

        return true;
    }

    /// <summary>True when this Escape event belongs to the plugin holding the claim. Its repeats and its key-up are kept too.</summary>
    private bool TakeEscape(bool down)
    {
        if (_escapeTaken)
        {
            if (!down)
            {
                _escapeTaken = false;
            }

            return true;
        }

        if (!down || !EscapeClaimed() || _ledger.HoldingSuper)
        {
            return false;
        }

        _escapeTaken = true;
        if (_claims?.Resolve(BareEscape) is Dispatch.Global global && (ReportGlobal is not null || _globalSink is not null))
        {
            if (ReportGlobal is { } report)
            {
                report(global.Name, true, false);
            }
            else
            {
                _globalSink?.OnGlobal(global.Name, down: true, hold: false);
            }
        }
        else
        {
            EscapeTaken?.Invoke();
        }

        return true;
    }

    /// <summary>True when bare Escape is taken. A bind book wins over <see cref="EscapeCancels"/>, so the two cannot disagree.</summary>
    private bool EscapeClaimed() => _claims is not null ? _claims.Resolve(BareEscape) is not null : EscapeCancels;

    /// <summary>True when this F11 event belongs to a press <see cref="FullscreenKey"/> took. Its repeats and its key-up are kept too.</summary>
    private bool TakeFullscreenKey(bool down)
    {
        if (_fullscreenKeyTaken)
        {
            if (!down)
            {
                _fullscreenKeyTaken = false;
            }

            return true;
        }

        if (!down || _ledger.HoldingSuper || FullscreenKey is not { } take || !take())
        {
            return false;
        }

        _fullscreenKeyTaken = true;
        return true;
    }

    private void NoteWithhold()
    {
        if (_ledger.Withhold(ForegroundCloaked(), Environment.TickCount64))
        {
            _withholdLapsed = true;
            DeliverWithheld();
        }
    }

    /// <summary>
    /// Sends the keys that waited, at once. The ledger records a waiting key-down as given to Windows the moment it hands it over,
    /// and it asks Windows about that key on the next key-down, so the key must have reached Windows by then.
    /// </summary>
    private void DeliverWithheld()
    {
        Apply(_ledger.ReleaseWithheld());
        FlushSynth();
    }

    private void FlushHeld()
    {
        // Once for each turn of the pump and each foreground change, not once for each message of the turn.
        if (!_cloakFresh)
        {
            _cloakFresh = true;
            SampleCloak();
        }

        NoteWithhold();
        DeliverWithheld();
    }

    /// <summary>Writes what the hook noticed during its last calls. A file write never runs inside the hook.</summary>
    private void ReportNotes()
    {
        if (_withholdLapsed)
        {
            _withholdLapsed = false;
            QueueWarning("keys waited " + ChordLedger.WithholdLimitMs + " ms because the window that has the keyboard stayed hidden; typing into it");
        }

        if (_droppedHeld)
        {
            _droppedHeld = false;
            QueueWarning("dropped keys held while the window that has the keyboard was hidden");
        }
    }

    private void QueueInformation(string text) => _hookNotes.Enqueue((false, text));

    private void QueueWarning(string text) => _hookNotes.Enqueue((true, text));

    /// <summary>Writes notes the hook thread queued. The key callback itself does not touch the log.</summary>
    private void DrainHookNotes()
    {
        var shareFailure = HookStamp.TakeShareFailure();
        if (shareFailure is not null)
        {
            Log.Warning("Chord", "could not share the hook stamp", shareFailure);
        }

        while (_hookNotes.TryDequeue(out var note))
        {
            if (note.Warning)
            {
                Log.Warning("Chord", note.Text);
            }
            else
            {
                Log.Information("Chord", note.Text);
            }
        }
    }

    /// <summary>
    /// A key other than Super yields the pointer. Which keys are still down is the ledger's account, read when the pointer moves,
    /// so this keeps no set of its own to go stale when a key-up goes to an administrator window.
    /// </summary>
    private void TrackYield(int vk, bool down)
    {
        if (!down || Super(vk))
        {
            return;
        }

        lock (_yieldGate)
        {
            if (_yield.Pressed())
            {
                _suspend = true;
            }
        }
    }

    private static bool Super(int vk) => vk is Native.VkLWin or Native.VkRWin;

    /// <summary>A key Windows does not use. Sent while Super is down, it keeps the release from opening Start.</summary>
    private static void MaskStart() => KeyStroke.Send(KeyStroke.Down(0xFF), KeyStroke.Up(0xFF));

    /// <summary>
    /// Adopts a Super that Windows holds and the ledger never saw. Another hook handed Windows the press,
    /// so this key belongs to that chord. Windows saw no key with it yet, so the caller masks Start.
    /// </summary>
    private bool AdoptWindowsSuper(long now)
    {
        var adopted = false;
        foreach (var vk in (ReadOnlySpan<int>)[Native.VkLWin, Native.VkRWin])
        {
            if (OsDown(vk) && _ledger.AdoptSuper(PhysicalKey.Super(vk), now))
            {
                adopted = true;
            }
        }

        _adopted |= adopted;
        return adopted;
    }

    private void FlushYield()
    {
        var suspend = false;
        lock (_yieldGate)
        {
            if (_suspend)
            {
                _suspend = false;
                suspend = true;
            }
        }

        if (suspend)
        {
            _hover.Suspend();
        }
    }

    private void FlushSynth()
    {
        while (_synth.TryDequeue(out var inputs))
        {
            SendOrRelease(inputs);
        }
    }

    /// <summary>
    /// Installs this hook again so Windows calls it before every hook installed since.
    /// The new hook goes in before the old one comes out, so no key reaches neither.
    /// It runs on the hook thread between messages, never inside the hook.
    /// </summary>
    private bool Reinstall()
    {
        var previous = _hook;
        if (previous == 0 || Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        var next = Native.SetWindowsHookEx(Native.WhKeyboardLl, _proc, Native.GetModuleHandle(null), 0);
        if (next == 0)
        {
            QueueWarning("could not install the keyboard hook again; Win32 error " + Marshal.GetLastWin32Error());
            return false;
        }

        if (Interlocked.CompareExchange(ref _hook, next, previous) != previous)
        {
            // Dispose took the hook out while this one went in.
            Native.UnhookWindowsHookEx(next);
            return false;
        }

        // Fails when Windows already removed the old hook. Nothing is left to take out then.
        Native.UnhookWindowsHookEx(previous);
        return true;
    }

    private void ReleaseForLockNow()
    {
        Apply(_ledger.ReleaseForLock());
        PublishSuper();
        FlushSynth();
    }

    /// <summary>Shares the held Super with the elevated helper, which owns chords while an administrator window is in front.</summary>
    private void StampSuper(bool held)
    {
        if (held == _superStamped)
        {
            return;
        }

        _superStamped = held;
        if (!held)
        {
            HookStamp.DesktopSuper(null);
            return;
        }

        _superPresses = (_superPresses + 1) & 0xFFFFFF;
        HookStamp.DesktopSuper(new DesktopSuper(_superVk, _superPresses));
    }

    private void PublishSuper()
    {
        var held = _ledger.HoldingSuper;
        StampSuper(held);
        var was = Interlocked.Exchange(ref _superHeld, held ? 1 : 0) != 0;
        if (was && !held)
        {
            // Raised after the hook returns. The shell finishes a keyboard move that waited for this.
            _superReleased = true;
        }

        if (held && !was)
        {
            // Once per press. The placer keeps its own clock running while Super stays held,
            // so a key pressed with Super does not need to wake it again.
            _superDown?.Invoke();
        }
    }

    private void FinishRelease()
    {
        ReleaseForLockNow();
        ManualResetEventSlim? done;
        lock (_releaseGate)
        {
            done = _releaseDone;
            _releaseDone = null;
        }

        done?.Set();
    }

    private void SampleCloak()
    {
        var foreground = Native.GetForegroundWindow();

        // The shell's cloak, whoever put it there: Winoma on a window it hid (ADR 0059), or Windows on a window of another virtual desktop.
        // Nothing typed into either would show, so keys wait until the keyboard has moved or the window is shown. A switch moves the
        // keyboard before it hides anything, so this is never true through one. It is true when a hidden window took the keyboard itself,
        // until the shell has followed it, and after a switch to an empty Workspace made with Super held, until Super goes up:
        // the ledger starts no wait while Super is held (ChordLedger.Withhold). The hook may run in a process of its own,
        // which does not know which cloaks are Winoma's, and does not need to.
        var cloaked = Native.ShellCloaked(foreground) ? 1 : 0;
        Volatile.Write(ref _foregroundCloaked, cloaked);
        if (foreground != 0)
        {
            // The foreground event can be missed. The pump's next turn catches up.
            _ = Native.GetWindowThreadProcessId(foreground, out var pid);
            FollowOwnForeground(pid);
        }
    }

    private bool ForegroundCloaked() => Volatile.Read(ref _foregroundCloaked) != 0;

    private void OpenSessionWindow()
    {
        _sessionProc = SessionProc;
        _sessionProcPointer = Marshal.GetFunctionPointerForDelegate(_sessionProc);
        var instance = Native.GetModuleHandle(null);
        var cls = new Native.WndClass
        {
            LpfnWndProc = _sessionProcPointer,
            HInstance = instance,
            LpszClassName = "Winland.Session",
        };
        if (Native.RegisterClass(ref cls) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != Native.ErrorClassAlreadyExists)
            {
                Log.Warning("Chord", "session window did not register; Win32 error " + error);
                return;
            }
        }

        _sessionWindow = Native.CreateWindowEx(0, "Winland.Session", null, 0, 0, 0, 0, 0, Native.HwndMessage, 0, instance, 0);
        if (_sessionWindow == 0)
        {
            Log.Warning("Chord", "session window did not open; Win32 error " + Marshal.GetLastWin32Error());
            return;
        }

        WatchRawKeyboard();
        if (!Native.WTSRegisterSessionNotification(_sessionWindow, Native.NotifyForThisSession))
        {
            Log.Warning("Chord", "session notifications did not register; Win32 error " + Marshal.GetLastWin32Error());
            return;
        }

        Log.Information("Chord", "session notifications registered");
    }

    /// <summary>
    /// Raw input reports a key only when it reached Windows. A Super event there that this hook did not
    /// swallow went past it through another hook, so the ledger adopts or forgets that press.
    /// </summary>
    private void WatchRawKeyboard()
    {
        if (!RawKeyboard(true))
        {
            Log.Warning("Chord", "raw keyboard did not register; Win32 error " + Marshal.GetLastWin32Error());
            return;
        }

        Log.Information("Chord", "raw keyboard registered");
    }

    /// <summary>
    /// Raw keyboard input stops while a window of this process, such as a panel, has the keyboard, and starts again when another window does.
    /// While this process takes raw keyboard input, Windows does not call this hook for keys that go to this process's own window,
    /// so no chord works until the panel closes. Nothing goes past the hook then, so the repair it feeds has nothing to watch.
    /// Runs on the hook thread, which owns the window raw input is delivered to.
    /// </summary>
    /// <param name="foregroundPid">The process that has the foreground window. Zero when none does.</param>
    private void FollowOwnForeground(uint foregroundPid)
    {
        if (_sessionWindow == 0 || foregroundPid == 0)
        {
            return;
        }

        var own = foregroundPid == (uint)Environment.ProcessId;
        if (own != _rawWatching)
        {
            return;
        }

        if (!RawKeyboard(!own))
        {
            QueueWarning("raw keyboard did not " + (own ? "stop" : "start again") + "; Win32 error " + Marshal.GetLastWin32Error());
        }
    }

    /// <summary>Starts or stops raw keyboard input to the session window.</summary>
    /// <returns>False when Windows refused.</returns>
    private bool RawKeyboard(bool on)
    {
        var device = new Native.RawInputDevice
        {
            UsagePage = 1,
            Usage = 6,
            Flags = on ? Native.RidevInputSink : Native.RidevRemove,
            Target = on ? _sessionWindow : 0,
        };
        if (!Native.RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<Native.RawInputDevice>()))
        {
            return false;
        }

        _rawWatching = on;
        return true;
    }

    private void OnRawInput(nint handle)
    {
        // Raw input reports every key on the machine, on the thread every key waits for.
        // The keyboard record is a few dozen bytes, so it is read on the stack, without a heap buffer or a marshal.
        uint header;
        uint keyboardSize;
        unsafe
        {
            header = (uint)sizeof(Native.RawInputHeader);
            keyboardSize = (uint)sizeof(Native.RawKeyboard);
        }

        uint size = 0;
        _ = Native.GetRawInputData(handle, Native.RidInput, 0, ref size, header);
        if (size < header + keyboardSize || size > RawInputStack)
        {
            return;
        }

        Native.RawInputHeader head;
        Native.RawKeyboard keyboard;
        unsafe
        {
            var buffer = stackalloc byte[(int)size];
            if (Native.GetRawInputData(handle, Native.RidInput, (nint)buffer, ref size, header) != size)
            {
                return;
            }

            head = *(Native.RawInputHeader*)buffer;
            keyboard = *(Native.RawKeyboard*)(buffer + header);
        }

        // An injected key has no device. The hook passes those without counting them.
        if (head.Type != Native.RimTypeKeyboard || head.Device == 0)
        {
            return;
        }

        // 0xFF is the fake shift a keyboard sends around some extended keys. The hook is not called for it.
        var vk = (int)keyboard.VKey;
        if (vk is 0 or 0xFF)
        {
            return;
        }

        // The hook is called for this key before raw input reports it. A run of keys with no call means Windows dropped the hook.
        _repair.KeyReachedWindows(_hookCannotSee);
        if (!Super(vk))
        {
            return;
        }

        if ((keyboard.Flags & Native.RiKeyBreak) != 0)
        {
            if (_ledger.ForgetSuper(vk))
            {
                PublishSuper();
                _repair.SuperWentPast();
                Log.Information("Chord", "super key-up went past the hook; forgot the press");
            }

            return;
        }

        if (_ledger.AdoptSuper(PhysicalKey.Super(vk), Environment.TickCount64))
        {
            MaskStart();
            PublishSuper();
            _repair.SuperWentPast();
            Log.Information("Chord", "super went past the hook; adopted the press");
        }
    }

    private void CloseSessionWindow()
    {
        var hwnd = Interlocked.Exchange(ref _sessionWindow, 0);
        if (hwnd == 0)
        {
            return;
        }

        Native.WTSUnRegisterSessionNotification(hwnd);
        Native.DestroyWindow(hwnd);
    }

    private nint SessionProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        _afterDue = true;
        if (message == Native.WmWtsSessionChange)
        {
            var change = (int)wParam;
            if (change is Native.WtsSessionLock or Native.WtsSessionUnlock)
            {
                QueueInformation(change == Native.WtsSessionLock ? "session locked; releasing Super" : "session unlocked; releasing Super");
                ReleaseForLockNow();
                if (change == Native.WtsSessionUnlock)
                {
                    _repair.Unlocked();
                }
            }
        }
        else if (message == Native.WmInput)
        {
            try
            {
                OnRawInput(lParam);
            }
            catch (Exception ex)
            {
                Log.Error("Chord", "raw keyboard input failed", ex);
            }
        }

        return Native.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void Apply(GateEffect effect)
    {
        if (effect.Queued is { } queued)
        {
            _pending.Enqueue((queued.Chord, queued.Down, System.Diagnostics.Stopwatch.GetTimestamp()));
            _wake = true;
        }

        if (effect.Inject.Count == 0)
        {
            return;
        }

        var mask = false;
        for (var i = 0; i < effect.Inject.Count; i++)
        {
            var pulse = effect.Inject[i];
            if (!pulse.Down && pulse.Id is Native.VkLWin or Native.VkRWin)
            {
                mask = true;
                break;
            }
        }

        var inputs = new Native.Input[effect.Inject.Count + (mask ? 2 : 0)];
        var start = 0;
        if (mask)
        {
            inputs[0] = KeyStroke.Down(0xFF);
            inputs[1] = KeyStroke.Up(0xFF);
            start = 2;
        }

        for (var i = 0; i < effect.Inject.Count; i++)
        {
            var pulse = effect.Inject[i];
            inputs[start + i] = pulse.Down ? KeyStroke.Down((ushort)pulse.Id) : KeyStroke.Up((ushort)pulse.Id);
        }

        _synth.Enqueue(inputs);
    }

    private void SendOrRelease(Native.Input[] inputs)
    {
        var sent = KeyStroke.Send(inputs);
        if (sent > 0 && sent < (uint)inputs.Length)
        {
            QueueWarning("sent " + sent + " of " + inputs.Length + " synthetic keys; releasing modifiers");
            ReleaseStuckModifiers();
            KeyStroke.ReleaseSentDowns(inputs, sent);
        }
    }

    private static bool OsDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>No chord resolves until the shell installs its binds.</summary>
    private sealed class EmptyBinds : IBindTable
    {
        public static readonly EmptyBinds Instance = new();

        public Dispatch? Resolve(Chord chord) => null;

        public bool IsHold(Chord chord) => false;

        public Dispatch? ResolveHold(Chord chord) => null;
    }
}
