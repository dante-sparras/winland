using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace Winland.Platform;

/// <summary>
/// The keyboard hook, run by <see cref="KeyboardHookHost"/> in a process of its own.
/// This side starts that process, reads chords and Super's state from its pipe, and starts it again if it ends.
/// While it is down no hook is installed, so keys reach Windows as they would without Winoma.
/// No caller writes the pipe. A line is queued in a <see cref="HookOutbox"/> and a writer thread sends it,
/// so a hook process that has stopped reading holds that thread and never the bar.
/// </summary>
public sealed class RemoteKeyboardHook : IKeyboardHook
{
    private const int ReleaseWaitMs = 500;
    private const long SteadyMs = 60_000;
    private const int MaxDelayMs = 30_000;

    private readonly Action? _superDown;
    private readonly ConcurrentQueue<(Chord Chord, bool Down, long QueuedAt)> _pending = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _asks = new();
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly object _gate = new();
    private readonly Action _releaseForLock;
    private NamedPipeClientStream? _pipe;
    private HookOutbox? _outbox;
    private Process? _host;
    private Thread? _link;
    private int _nextId;
    private int _disposed;
    private int _escapeCancels;
    private int _pickerTakesKeys;
    private int _mayHold;
    private int _hovered;

    /// <summary>Creates the link. Nothing starts until <see cref="Install"/>.</summary>
    /// <param name="superDown">Runs on the link thread when Super goes down. It must return at once.</param>
    public RemoteKeyboardHook(Action? superDown = null)
    {
        _superDown = superDown;
        _releaseForLock = ReleaseForLock;
        ElevatedBroker.Keyed += NoteElevatedSuper;
    }

    /// <summary>The elevated helper saw the desktop's Super go up. The hook process notes it for the next key.</summary>
    private void NoteElevatedSuper(HookWire message)
    {
        if (message is HookWire.Super { Held: false })
        {
            ForgetSuper();
        }
    }

    /// <inheritdoc />
    public event Action? ChordQueued;

    /// <inheritdoc />
    public event Action? SuperReleased;

    /// <inheritdoc />
    public event Action? EscapeTaken;

    /// <inheritdoc />
    public event Action? HoverReady;

    /// <inheritdoc />
    public bool EscapeCancels
    {
        get => Volatile.Read(ref _escapeCancels) != 0;
        set
        {
            Volatile.Write(ref _escapeCancels, value ? 1 : 0);
            Send(new HookWire.EscapeMode(value));
        }
    }

    /// <inheritdoc />
    public event Action<int>? PickerKeyTaken;

    /// <inheritdoc />
    public bool PickerTakesKeys
    {
        get => Volatile.Read(ref _pickerTakesKeys) != 0;
        set
        {
            Volatile.Write(ref _pickerTakesKeys, value ? 1 : 0);
            Send(new HookWire.PickerMode(value));
        }
    }

    /// <summary>Not asked. The hook process cannot wait on the desktop inside the hook, so F11 reaches the app as it is.</summary>
    public Func<bool>? FullscreenKey { get; set; }

    /// <summary>The hook process reads the chord file itself, so this side keeps nothing.</summary>
    /// <param name="chords">Unused.</param>
    public void UseChords(IBindTable chords)
    {
    }

    /// <summary>The hook process has its own bind book. This side only forwards <see cref="EscapeCancels"/>.</summary>
    /// <param name="binds">Unused.</param>
    public void UseBinds(IBindTable binds)
    {
    }

    /// <summary>Escape comes back as <see cref="EscapeTaken"/> across the pipe. This side has no sink.</summary>
    /// <param name="sink">Unused.</param>
    public void UseSink(IGlobalBindSink sink)
    {
    }

    /// <inheritdoc />
    public void Install()
    {
        KeyboardHook.ReleaseStuckModifiers();
        ShellBridge.Session.Arm(_releaseForLock);
        _link = new Thread(Supervise)
        {
            IsBackground = true,
            Name = "winoma-keys-link",
        };
        _link.Start();
    }

    /// <inheritdoc />
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
    public void ForgetSuper() => Send(new HookWire.Forget());

    /// <inheritdoc />
    public void KeyboardMoved()
    {
        Volatile.Write(ref _mayHold, 1);
        Send(new HookWire.Deliver());
    }

    /// <summary>The hook process watches its own hook thread and writes to the same log.</summary>
    public void WatchStall()
    {
    }

    /// <summary>
    /// Wakes the hook process so keys kept off a hidden window are typed once the keyboard is on one the person can see.
    /// The bar asks on every turn. The line is written only on the one turn after <see cref="KeyboardMoved"/>.
    /// Keys that wait for any other reason are typed on the hook thread's own turn, which is a quarter second while any wait.
    /// </summary>
    public void DeliverHeld()
    {
        if (Interlocked.Exchange(ref _mayHold, 0) == 0)
        {
            return;
        }

        Send(new HookWire.Deliver());
    }

    /// <summary>Tells the hook process, which knows which keys are held, to read the pointer. It answers with <see cref="HoverReady"/>.</summary>
    public void SamplePointer() => Send(new HookWire.Pointer());

    /// <summary>True once after the hook process said hover may focus. Never waits on it.</summary>
    /// <returns>True when hover may focus the window under the pointer.</returns>
    public bool TakeHover() => Interlocked.Exchange(ref _hovered, 0) != 0;

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ElevatedBroker.Keyed -= NoteElevatedSuper;

        _stop.Set();
        Send(new HookWire.Quit());
        ShellBridge.Session.Disarm(_releaseForLock);

        var host = _host;
        try
        {
            if (host is not null && !host.WaitForExit(500))
            {
                host.Kill();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }

        Close();
        KeyboardHook.PublishRemoteSuper(false);
        KeyboardHook.ReleaseStuckModifiers();
    }

    private void ReleaseForLock() => Ask(id => new HookWire.Release(id), ReleaseWaitMs);

    /// <summary>Keeps one hook process running. One that lived a minute starts again at once; one that keeps ending backs off.</summary>
    private void Supervise()
    {
        var delay = 1000;
        while (Volatile.Read(ref _disposed) == 0)
        {
            var started = Environment.TickCount64;
            try
            {
                Connect();
                Read();
            }
            catch (Exception ex)
            {
                // Dispose closes the pipe under the read. That is the end, not a failure.
                if (Volatile.Read(ref _disposed) == 0)
                {
                    Log.Warning("Chord", "keyboard hook process link failed", ex);
                }
            }
            finally
            {
                Lost();
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            delay = Environment.TickCount64 - started >= SteadyMs ? 1000 : Math.Min(delay * 2, MaxDelayMs);
            Log.Warning("Chord", "keyboard hook process ended; starting it again in " + delay + " ms");
            if (_stop.Wait(delay))
            {
                return;
            }
        }
    }

    private void Connect()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Winoma has no executable path.");
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(KeyboardHookHost.Switch);
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var host = Process.Start(start) ?? throw new InvalidOperationException("The keyboard hook process did not start.");
        _host = host;
        var pipe = new NamedPipeClientStream(".", KeyboardHookHost.PipeName(Environment.ProcessId), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            pipe.Connect(10_000);
        }
        catch
        {
            pipe.Dispose();
            throw;
        }

        // Each process gets an outbox of its own, so a line queued for the last one is never sent to this one.
        var outbox = new HookOutbox();
        lock (_gate)
        {
            _pipe = pipe;
            _outbox = outbox;
        }

        var writer = new Thread(() => Write(pipe, outbox))
        {
            IsBackground = true,
            Name = "winoma-keys-write",
        };
        writer.Start();

        // A new process knows nothing yet. Tell it what the old one was told.
        if (EscapeCancels)
        {
            Send(new HookWire.EscapeMode(true));
        }

        if (PickerTakesKeys)
        {
            Send(new HookWire.PickerMode(true));
        }

        Log.Information("Chord", "keyboard hook process " + host.Id + " connected");
    }

    private void Read()
    {
        NamedPipeClientStream? pipe;
        lock (_gate)
        {
            pipe = _pipe;
        }

        if (pipe is null)
        {
            return;
        }

        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        while (reader.ReadLine() is string line)
        {
            if (!HookWire.TryParse(line, out var message))
            {
                continue;
            }

            switch (message)
            {
                case HookWire.ChordKeyed keyed:
                    _pending.Enqueue((keyed.Chord, keyed.Down, System.Diagnostics.Stopwatch.GetTimestamp()));
                    ChordQueued?.Invoke();
                    break;
                case HookWire.Super { Held: true }:
                    KeyboardHook.PublishRemoteSuper(true);
                    _superDown?.Invoke();
                    break;
                case HookWire.Super:
                    KeyboardHook.PublishRemoteSuper(false);
                    SuperReleased?.Invoke();
                    break;
                case HookWire.EscapeTaken:
                    EscapeTaken?.Invoke();
                    break;
                case HookWire.PickerKeyTaken picked:
                    PickerKeyTaken?.Invoke(picked.Key);
                    break;
                case HookWire.Hovered:
                    Volatile.Write(ref _hovered, 1);
                    HoverReady?.Invoke();
                    break;
                case HookWire.Reply reply when _asks.TryRemove(reply.Id, out var ask):
                    ask.TrySetResult(reply.Value);
                    break;
            }
        }
    }

    /// <summary>The process or its pipe is gone. Nothing is held, and every question waiting gets a no.</summary>
    private void Lost()
    {
        Close();
        Interlocked.Exchange(ref _hovered, 0);
        if (KeyboardHook.SuperIsHeld)
        {
            KeyboardHook.PublishRemoteSuper(false);
            SuperReleased?.Invoke();
        }

        foreach (var ask in _asks)
        {
            if (_asks.TryRemove(ask.Key, out var waiting))
            {
                waiting.TrySetResult(false);
            }
        }

        // Two hooks must never run at once. A process whose pipe broke is ended before another starts.
        var host = Interlocked.Exchange(ref _host, null);
        try
        {
            if (host is not null && !host.HasExited)
            {
                host.Kill();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
        finally
        {
            host?.Dispose();
        }

        // A process that crashed may have left a modifier down in Windows.
        KeyboardHook.ReleaseStuckModifiers();

        // And its Super stamped. The elevated helper reads that stamp while no hook process is up to clear it.
        HookStamp.DesktopSuper(null);
    }

    private void Close()
    {
        lock (_gate)
        {
            // The lines that wait were for this process, so they are dropped, and the writer thread ends with the outbox.
            // A writer still inside a write ends when the pipe does.
            _outbox?.Close();
            _pipe?.Dispose();
            _outbox = null;
            _pipe = null;
        }
    }

    /// <summary>
    /// Queues one line for the writer thread and returns at once. Lines go out in the order they were sent.
    /// False when no hook process is connected or the pipe broke.
    /// </summary>
    private bool Send(HookWire message)
    {
        HookOutbox? outbox;
        lock (_gate)
        {
            outbox = _outbox;
        }

        return outbox is not null && outbox.Add(message);
    }

    /// <summary>Writes the outbox to the pipe until either closes. Only this thread waits on a hook process that has stopped reading.</summary>
    private static void Write(Stream pipe, HookOutbox outbox)
    {
        try
        {
            using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true)
            {
                AutoFlush = true,
            };
            while (outbox.Take(out var message))
            {
                writer.WriteLine(message.Format());
            }
        }
        catch (Exception ex)
        {
            // Nothing above this thread catches. A broken or closed pipe is the usual end: the link thread sees it too and starts the process again.
            if (ex is not (IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException))
            {
                Log.Warning("Chord", "keyboard hook process writer failed", ex);
            }
        }
        finally
        {
            // Send answers false from here on, so no line queues for a pipe nobody writes and no question waits out its timeout.
            outbox.Close();
        }
    }

    private bool Ask(Func<int, HookWire> question, int waitMs)
    {
        var id = Interlocked.Increment(ref _nextId);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _asks[id] = answer;
        if (!Send(question(id)))
        {
            // No process to ask. The UI thread does not wait out the timeout for nobody.
            _asks.TryRemove(id, out _);
            return false;
        }

        var answered = answer.Task.Wait(waitMs);
        _asks.TryRemove(id, out _);
        return answered && answer.Task.Result;
    }
}
