using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

public sealed class WindowHost : IDisposable
{
    private const uint QuitMessage = 0x0012;

    private readonly Core _core;
    private readonly WindowAdapter _windows;
    private readonly KeyboardHook _keys;
    private readonly MouseHook _mouse;
    private readonly TouchpadInput _touchpad;
    private readonly CommandServer _commands;
    private readonly SessionPhase _phase = new();
    private readonly Lock _memory = new();
    private readonly Queue<SessionSignal> _pending = new();

    private RecoverySnapshot? _recovered;
    private RecoverySnapshot? _clients;
    private Action? _whenReady;
    private SessionEffect _deferred = SessionEffect.None;
    private bool _started;
    private bool _opened;
    private bool _draining;
    private bool _ownsTaskbar;
    private bool _disposed;

    public WindowHost(Core core, Action<RecoverySnapshot>? onRecovery = null, string? pipeName = null)
    {
        ArgumentNullException.ThrowIfNull(core);
        _core = core;
        _windows = new WindowAdapter(core, snapshot =>
        {
            Remember(snapshot);
            onRecovery?.Invoke(snapshot);
        });
        _keys = new KeyboardHook(core);
        _mouse = new MouseHook(core, _keys);
        _touchpad = new TouchpadInput(_keys.PostInput);
        _core.SessionLock = _phase;
        _commands = new CommandServer(core, pipeName);
    }

    public CommandReply Reload()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var reply = _core.Execute("reload");
        WindowAdapter.Apply(reply.Effect);
        return reply;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
            return;

        _started = true;
        _ownsTaskbar = true;
        Taskbar.Hide();
        _windows.Start();
        _keys.Start();
        _mouse.Start();
        _touchpad.Start();
        _commands.Start();
    }

    public void Run(RecoverySnapshot? recovered = null, Action? whenReady = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _recovered = recovered;
        _whenReady = whenReady;

        // A message-only window does not receive power broadcasts. This hidden
        // top-level window does, and it is the target for session notifications.
        using var session = new SessionWindow(OnSignal, OnFlash);
        var shell = WaitForShell();
        var thread = PInvoke.GetCurrentThreadId();
        SessionHalt.Stop = () => _ = PInvoke.PostThreadMessage(thread, QuitMessage, default, default);
        try
        {
            Start();
            FlushDeferred();
            if (shell == SessionSignal.ShellTimedOut)
                session.WatchForShell();

            OnSignal(shell);

            // CancelKeyPress is raised off the message-loop thread, so PostQuitMessage
            // would signal the wrong queue.
            ConsoleCancelEventHandler onCancel = (_, e) =>
            {
                e.Cancel = true;
                _ = PInvoke.PostThreadMessage(thread, QuitMessage, default, default);
            };

            Console.CancelKeyPress += onCancel;
            try
            {
                _windows.Pump(message =>
                {
                    if (message.message == KeyboardHook.WatchMessage)
                    {
                        // Both hooks share this thread, so a stall that cost one may have cost both.
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

                    return _keys.Service(message.message);
                });
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
            }
        }
        finally
        {
            SessionHalt.Stop = null;
        }
    }

    public void RevealHidden() => _windows.RevealHidden();

    private void OnFlash(WindowId window) => WindowAdapter.Apply(_core.Handle(new WindowFlashed(window)));

    public void ResetDecorations() => _windows.ResetDecorations();

    public void RestoreTaskbar()
    {
        if (!_ownsTaskbar)
            return;

        _ownsTaskbar = false;
        Taskbar.Restore();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            _commands.Dispose();
            _touchpad.Dispose();
            _mouse.Dispose();
            _keys.Dispose();
            _windows.Dispose();
        }
        finally
        {
            RestoreTaskbar();
        }
    }

    private SessionSignal WaitForShell()
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
        ShellBars.Rehide();
        var snapshot = Recall();
        if (snapshot is not null)
            SessionRecovery.Restore(_windows, snapshot);

        ApplyMonitors();
    }

    private void Remember(RecoverySnapshot snapshot)
    {
        var captured = SessionRecovery.Capture(snapshot);
        lock (_memory)
        {
            if (_clients is null || captured.Clients.Count >= _clients.Clients.Count)
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
        WindowAdapter.Apply(_core.Handle(new MonitorsChanged(MonitorAdapter.Read())));
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
