using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

public sealed class WindowHost : IDisposable
{
    private const uint QuitMessage = 0x0012;

    private readonly WindowManager _core;
    private readonly WindowAdapter _windows;
    private readonly KeyboardHook _keys;
    private readonly MouseHook _mouse;
    private readonly InputThread _input;
    private readonly TouchpadInput _touchpad;
    private readonly CommandServer _commands;
    private readonly SessionPhase _phase = new();
    private readonly Lock _memory = new();
    private readonly Queue<SessionSignal> _pending = new();
    private readonly LoopInvoker _loop = new();
    // What the saver is posted: the snapshot is built when it runs, not when it is posted.
    private static readonly object Changed = new();

    private readonly Coalescer<object> _saver;
    private readonly Action<RecoverySnapshot>? _onRecovery;

    private RecoverySnapshot? _recovered;
    private RecoverySnapshot? _clients;
    private Action? _whenReady;
    private SessionEffect _deferred = SessionEffect.None;
    private bool _started;
    private bool _opened;
    private AccentChanged? _accent;
    private bool _draining;
    private bool _ownsTaskbar;

    // True while the loop runs. Cleared before it detaches, so what runs after that, here or on another thread, sees it.
    private volatile bool _looping;

    // Set once the exit begins, so a reload or a command still on its way does not hide the taskbar again.
    private volatile bool _taskbarReleased;
    private bool _markerSeen;
    private bool _disposed;
    private ITaskbarSession _taskbar = NullTaskbarSession.Instance;

    public WindowHost(WindowManager core, Action<RecoverySnapshot>? onRecovery = null, string? pipeName = null)
    {
        ArgumentNullException.ThrowIfNull(core);
        _core = core;
        // Building the snapshot, reading each Client's title and writing the state file happen off the event path,
        // at most a few times a second, with the newest state.
        _onRecovery = onRecovery;
        _saver = new Coalescer<object>(_ => Save(), TimeSpan.FromMilliseconds(250), "Winland recovery");
        _windows = new WindowAdapter(core, () => _saver.Post(Changed));
        _keys = new KeyboardHook(core, _windows.Apply);
        _mouse = new MouseHook(core, _keys);
        _input = new InputThread(StartHooks, ServiceHooks, StopHooks);
        _touchpad = new TouchpadInput(_keys.PostInput);
        _core.SessionLock = _phase;
        _commands = new CommandServer(core, _windows.Apply, pipeName);
        // A command arrives on a pipe thread. The taskbar state is the loop thread's, as a reload's is, and once the
        // loop stopped the exit owns the taskbar.
        _commands.AfterCommand = () => _loop.Post(() =>
        {
            if (_looping)
                FollowTaskbar();
        });
        _taskbar = TaskbarControl.Current;
    }

    /// <summary>The bars this host hides and restores. Tests substitute a stand-in before the session starts.</summary>
    internal ITaskbarSession TaskbarSession
    {
        get => _taskbar;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _taskbar = value;
        }
    }

    /// <summary>Runs on the message-loop thread when the session really ends (WM_ENDSESSION with wParam TRUE).</summary>
    public Action? SessionEnding { get; set; }

    /// <summary>Runs on the message-loop thread when a command is chosen from the notification-area menu.</summary>
    public Action<TrayCommand>? OnTray { get; set; }

    /// <summary>Runs on the message-loop thread when the config notification is clicked.</summary>
    public Action<string>? OnOpenFile { get; set; }

    /// <summary>Whether the log file is there, which the notification-area menu asks each time it opens.</summary>
    public Func<bool>? LogExists { get; set; }

    /// <summary>
    /// Runs <paramref name="action"/> on the message-loop thread and waits for it, or here when the loop does not
    /// answer. Windows ends a process five seconds after its console is closed, so an action the loop took is
    /// waited for until four and a half seconds have passed.
    /// </summary>
    public void OnLoop(Action action) => _loop.Invoke(action, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4.5));

    /// <summary>Runs a command as the pipe does, with its effect applied, and gives the reply's text.</summary>
    public string Command(string line)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var reply = _core.Execute(line);
        _windows.Apply(reply.Effect);
        FollowTaskbar();
        return reply.Text;
    }

    /// <summary>Reads the config again. The taskbar it follows is the loop thread's, so call it there.</summary>
    public CommandReply Reload()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var reply = _core.Execute("reload");
        _windows.Apply(reply.Effect);
        FollowTaskbar();
        return reply;
    }

    /// <summary>
    /// Reads the config again on the loop thread, once it takes its next message, and returns without waiting. A save
    /// the config watcher sees after the loop stopped is not read: the exit has begun, and a reload would start the
    /// config's programs again.
    /// </summary>
    public void PostReload() => _loop.Post(() =>
    {
        if (_looping)
            Reload();
    });

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
            return;

        _started = true;
        // The accent is known before any window is adopted, and again when Windows changes it.
        NoteAccent();
        // The work area is read once the session opens. Hiding the taskbars, or putting a crash marker back, has to
        // happen first so that read sees the strip the bars reserved or gave back.
        FollowTaskbar();
        _windows.Start();
        _input.Start();
        _commands.Start();
    }

    // The hooks live on the input thread, apart from window events, so nothing the main thread
    // waits on can delay system input. These three run there. So does the touchpad's window: reading a report takes
    // microseconds, and a swipe or the fingers the mouse hook reads is then never late behind a busy main thread.
    private void StartHooks()
    {
        _keys.Start();
        _mouse.Start();
        _touchpad.Start();
    }

    private bool ServiceHooks(uint message)
    {
        if (message != KeyboardHook.WatchMessage)
            return _keys.Service(message);

        // Both hooks share the input thread, so a stall that cost one may have cost both.
        if (_keys.Lost())
        {
            _mouse.Reinstall();
            _keys.Reinstall();
        }
        else
        {
            _keys.Watch();
        }

        return true;
    }

    private void StopHooks()
    {
        _touchpad.Dispose();
        _mouse.Dispose();
        _keys.Dispose();
    }

    public void Run(RecoverySnapshot? recovered = null, Action? whenReady = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _recovered = recovered;
        _whenReady = whenReady;

        // A message-only window does not receive power broadcasts. This hidden
        // top-level window does, and it is the target for session notifications. Windows sends it these while the
        // loop may be waiting on another program in the middle of a step, so each is handed to the loop and its step
        // starts at the top of the loop. The end of the session cannot wait: the process can end once it returns.
        using var session = new SessionWindow(
            signal => _loop.Post(() => OnSignal(signal)),
            window => _loop.Post(() => OnFlash(window)),
            () => SessionEnding?.Invoke(),
            () => _loop.Post(NoteAccent),
            WindowAdapter.ForgetSettings);
        var shell = WaitForShell();
        var thread = PInvoke.GetCurrentThreadId();
        SessionHalt.Stop = () => _ = PInvoke.PostThreadMessage(thread, QuitMessage, default, default);
        _looping = true;
        _loop.Attach(thread);
        // Armed only once the loop can take a click. Applying an outcome from a test must not add an icon.
        TrayHost.Start(
            command => _loop.Post(() =>
            {
                try
                {
                    OnTray?.Invoke(command);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    Console.Error.WriteLine(exception);
                }
            }),
            path => _loop.Post(() =>
            {
                try
                {
                    OnOpenFile?.Invoke(path);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    Console.Error.WriteLine(exception);
                }
            }),
            () => LogExists?.Invoke() ?? false);
        // CancelKeyPress is raised off the message-loop thread, so PostQuitMessage would signal the wrong queue. It is
        // taken before anything is hidden, so Ctrl+C while the session opens still ends in the cleanup.
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            _ = PInvoke.PostThreadMessage(thread, QuitMessage, default, default);
        };

        Console.CancelKeyPress += onCancel;
        try
        {
            Start();
            FlushDeferred();
            if (shell == SessionSignal.ShellTimedOut)
                session.WatchForShell();

            OnSignal(shell);

            WindowAdapter.Pump(message =>
            {
                if (message.message == LoopInvoker.Message)
                {
                    _loop.Drain();
                    return true;
                }

                // The foreground layout is read again on the next key.
                if (message.message == KeyboardHook.InputLanguageMessage)
                    _keys.ForgetLayout();

                return false;
            });
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            _looping = false;
            _loop.Detach();
            _loop.Drain();
            SessionHalt.Stop = null;
        }
    }

    /// <summary>Begins the exit: shows every hidden Client, and from now on only the exit itself changes the taskbar.</summary>
    public void RevealHidden()
    {
        _taskbarReleased = true;
        _windows.RevealHidden();
    }

    /// <summary>What a crash can still put back: hidden Clients, frames, effects, alpha and the taskbar.</summary>
    public void Salvage()
    {
        _windows.Salvage();
        RestoreTaskbar();
    }

    private void OnFlash(WindowId window) => _windows.Apply(_core.Handle(new WindowFlashed(window)));

    public void ResetDecorations() => _windows.ResetDecorations();

    public void RestoreTaskbar()
    {
        _taskbarReleased = true;
        if (!_ownsTaskbar)
            return;

        _ownsTaskbar = false;
        _taskbar.Restore();
    }

    // general:hide_taskbar, read again after a reload or a keyword. A change lays the monitors out once the session
    // is open, because hiding or restoring the bars changes the work area.
    private void FollowTaskbar()
    {
        if (_taskbarReleased)
            return;

        var choice = TaskbarPolicy.Decide(_core.HideTaskbar, _ownsTaskbar, PendingMarker(), _opened);
        if (choice.Hide)
        {
            _ownsTaskbar = true;
            _taskbar.Hide();
        }
        else if (choice.Restore)
        {
            _ownsTaskbar = false;
            _taskbar.Restore();
        }

        if (choice.Relayout && _taskbar.ChangesWorkArea)
            ApplyMonitors();
    }

    private bool PendingMarker()
    {
        if (_markerSeen)
            return false;

        _markerSeen = true;
        return _taskbar.Marker;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            _commands.Dispose();
            _input.Dispose();

            // Already done on the input thread when it ran; here for a host that never started.
            StopHooks();
            _windows.Dispose();
            _saver.Dispose();
        }
        finally
        {
            RestoreTaskbar();
        }
    }

    private static SessionSignal WaitForShell()
    {
        if (ShellBars.Present())
        {
            Console.Error.WriteLine("The shell is ready.");
            return SessionSignal.ShellReady;
        }

        Console.Error.WriteLine("Waiting for the shell.");
        var deadline = Environment.TickCount64 + (long)SessionPhase.ShellWait.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            PumpPending();
            if (ShellBars.Present())
            {
                Console.Error.WriteLine("The shell is ready.");
                return SessionSignal.ShellReady;
            }

            Thread.Sleep(50);
        }

        PumpPending();
        if (ShellBars.Present())
        {
            Console.Error.WriteLine("The shell is ready.");
            return SessionSignal.ShellReady;
        }

        var seconds = (int)SessionPhase.ShellWait.TotalSeconds;
        Console.Error.WriteLine($"The shell was not ready after {seconds} seconds.");
        return SessionSignal.ShellTimedOut;
    }

    /// <summary>
    /// Reads the Windows accent, the High Contrast colors and the text size, and tells the core, unless this read failed
    /// or matches the last one.
    /// </summary>
    internal void NoteAccent()
    {
        if (_disposed)
            return;

        AccentChanged next;
        try
        {
            if (SystemAccent.Read() is not BorderColor color)
                return;

            next = new AccentChanged(color) { Contrast = SystemAccent.Contrast(), TextScale = SystemAccent.TextScale() };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return;
        }

        if (next == _accent)
            return;

        _accent = next;
        _windows.Apply(_core.Handle(next));
    }

    private void OnSignal(SessionSignal signal)
    {
        _pending.Enqueue(signal);
        if (_draining)
            return;

        _draining = true;
        try
        {
            while (_pending.Count > 0)
            {
                var effect = _phase.Notice(_pending.Dequeue());
                if (!_started)
                {
                    if (effect != SessionEffect.None)
                        _deferred = effect;

                    continue;
                }

                Perform(effect);
            }
        }
        finally
        {
            _draining = false;
        }
    }

    private void FlushDeferred()
    {
        var effect = _deferred;
        _deferred = SessionEffect.None;
        if (_started)
            Perform(effect);
    }

    private void Perform(SessionEffect effect)
    {
        switch (effect)
        {
            case SessionEffect.None:
                return;
            case SessionEffect.LayOut:
                Open();
                return;
            case SessionEffect.Reflow:
                Console.Error.WriteLine("Laying out again.");
                Reflow();
                return;
            case SessionEffect.Recover:
                Console.Error.WriteLine("Laying out again after the session changed.");
                RecoverSession();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(effect), effect, "No handler for this effect.");
        }
    }

    private void Open()
    {
        if (_opened)
            return;

        _opened = true;
        ShellBars.Rehide();
        ApplyMonitors();
        _windows.Recover(_recovered);
        _windows.AdoptExisting();
        _whenReady?.Invoke();
        _windows.Publish();
        Taskbar.Conceal();
    }

    private void Reflow()
    {
        ShellBars.Rehide();
        ApplyMonitors();
    }

    private void RecoverSession()
    {
        _windows.DropGone();
        ShellBars.Rehide();
        var snapshot = Recall();
        if (snapshot is not null)
            SessionRecovery.Restore(_windows, snapshot);

        ApplyMonitors();
    }

    private void Save()
    {
        if (_windows.Capture() is not RecoverySnapshot snapshot)
            return;

        Remember(snapshot);

        // While the session is away its windows can look closed, so the state file keeps the last one from before.
        if (_phase.Live)
            _onRecovery?.Invoke(snapshot);
    }

    private void Remember(RecoverySnapshot snapshot)
    {
        // While the session is away its windows can look closed, so the snapshot from before is kept. Each Client's
        // process, class and title are recorded while it is alive, so a reused window is not taken for one that left.
        var captured = WindowIdentity.Annotate(snapshot);
        lock (_memory)
        {
            if (_clients is null || _phase.Live)
                _clients = captured;
        }
    }

    private RecoverySnapshot? Recall()
    {
        lock (_memory)
            return _clients;
    }

    private void ApplyMonitors()
    {
        _windows.Apply(_core.Handle(new MonitorsChanged(MonitorAdapter.Read())));
        Taskbar.Conceal();
    }

    private static void PumpPending()
    {
        while (PInvoke.PeekMessage(out var message, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
        {
            if (message.message == QuitMessage)
            {
                PInvoke.PostQuitMessage(0);
                return;
            }

            PInvoke.TranslateMessage(in message);
            PInvoke.DispatchMessage(in message);
        }
    }
}
