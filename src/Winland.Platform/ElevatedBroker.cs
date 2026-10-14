using System.Diagnostics;
using System.IO.Pipes;

namespace Winland.Platform;

/// <summary>
/// Moves, closes, and marks windows this process is not allowed to touch. An administrator window rejects
/// <c>SetWindowPos</c>, <c>WM_CLOSE</c>, and <c>SetProp</c> from a normal process, so a helper running elevated applies them.
/// Hiding one needs no helper: Explorer cloaks an administrator window for any process (ADR 0059).
/// </summary>
public static class ElevatedBroker
{
    private static readonly string PipeName = SessionPipe.For("Winland.Place");
    private const int HelperCheckMs = 1000;
    private const int HelperNetMs = 30_000;
    private static readonly object Gate = new();
    private static readonly ElevatedBook Book = new();
    private static int Serving;
    private static int Starting;
    private static readonly AutoResetEvent Pending = new(false);
    private static bool Denied;
    private static volatile bool _allowed;

    /// <summary>
    /// Whether the Host lets Winoma start the helper, which asks Windows for administrator rights (ADR 0069).
    /// While this is false no prompt is shown, administrator windows are not placed, and chords do not reach Winoma over them.
    /// </summary>
    public static bool Allowed
    {
        get => _allowed;
        set => _allowed = value;
    }

    private static bool Refused => Denied || !_allowed || LeftUnconnected();
    private static Process? Helper;

    /// <summary>True once the pipe has connected since the helper was last asked for.</summary>
    private static volatile bool Connected;

    /// <summary>
    /// True when the helper this desktop started exited and its pipe never connected. That is kept as a refusal (ADR 0065):
    /// asking again would show the consent prompt at every administrator window.
    /// </summary>
    private static bool LeftUnconnected()
    {
        // Read in this order. Launch lets the old helper go before it clears the flag, so a cleared flag is never read with the helper before it.
        var connected = Connected;
        if (!WindowExecutor.LeftUnconnected(Helper is { HasExited: true }, connected))
        {
            return false;
        }

        Denied = true;
        Log.Warning("Tiling", "the elevated helper left without connecting; it is not asked for again until the desktop restarts");
        return true;
    }

    /// <summary>
    /// A chord, or the key-up of the desktop's Super, that the helper's hook took while an administrator window was in front.
    /// Raised on a pipe thread; post the work elsewhere.
    /// </summary>
    public static event Action<HookWire>? Keyed;

    /// <summary>Reads act lines until the desktop closes the pipe, and applies each one.</summary>
    public static void Run()
    {
        ElevatedChordHook.Start();
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.In);
        try
        {
            pipe.Connect(60_000);
        }
        catch (Exception ex)
        {
            Log.Warning("Tiling", "elevated placer could not connect", ex);
            return;
        }

        Log.Information("Tiling", "elevated placer connected");
        using var reader = new StreamReader(pipe);
        // This process only ever places windows the desktop asked it to, so it keeps one placer of its own.
        var placer = new WindowPlacer();
        while (reader.ReadLine() is string line)
        {
            if (!ElevatedBook.TryRead(line, out var act))
            {
                continue;
            }

            switch (act.Kind)
            {
                case ElevatedKind.Place:
                    placer.Force((nint)act.Id, act.X, act.Y, act.Width, act.Height);
                    break;
                case ElevatedKind.Close:
                    var hwnd = (nint)act.Id;
                    if (!Native.PostMessage(hwnd, Native.WmClose, 0, 0))
                    {
                        Log.Warning("Tiling", "could not close elevated window " + hwnd);
                    }

                    break;
                case ElevatedKind.Raise:
                    Native.SetWindowPos((nint)act.Id, 0, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate);
                    break;
                case ElevatedKind.Focus:
                    ShellBridge.Windows.FocusFromHelper((nint)act.Id);
                    break;
                case ElevatedKind.Mark:
                    if (!WorkspaceProperty.Apply((nint)act.Id, act.Value, out var error))
                    {
                        Log.Warning("Workspace", "the helper could not mark window " + act.Id + " with workspace " + act.Value + ": error " + error);
                    }

                    break;
            }
        }
    }

    /// <summary>Queues a move for the elevated helper, starting the helper the first time.</summary>
    public static void Submit(nint hwnd, int x, int y, int width, int height)
    {
        lock (Gate)
        {
            if (!Book.Place(hwnd, x, y, width, height))
            {
                return;
            }
        }

        Pending.Set();
        StartServing();
        StartHelper();
    }

    /// <summary>True when the elevated helper is already running. A focus request must not show a new consent prompt.</summary>
    public static bool Running => Helper is { HasExited: false };

    /// <summary>Asks the running helper to focus an administrator window.</summary>
    public static void Focus(nint hwnd)
    {
        if (hwnd == 0 || !Running)
        {
            return;
        }

        lock (Gate)
        {
            Book.Focus(hwnd);
        }

        Pending.Set();
        StartServing();
    }

    /// <summary>
    /// Asks the helper to close an administrator window.
    /// The first request starts the helper, which is the same consent the placer already asks for.
    /// A later request does not ask again.
    /// </summary>
    public static void Close(nint hwnd)
    {
        if (hwnd == 0 || Refused)
        {
            return;
        }

        lock (Gate)
        {
            Book.Close(hwnd);
        }

        Pending.Set();
        StartServing();
        StartHelper();
    }

    /// <summary>
    /// Asks the helper to write the Workspace a window is on onto that window, or to take the mark off (ADR 0078).
    /// The desktop was refused, as it is on an administrator window. The first request starts the helper,
    /// which is the same consent the placer already asks for.
    /// </summary>
    /// <param name="hwnd">The window.</param>
    /// <param name="mark">A Workspace number, the Scratchpad's mark, or none to take the mark off.</param>
    /// <returns>False when the Host does not allow the helper or it was refused: the window then carries no mark.</returns>
    public static bool Mark(nint hwnd, int mark)
    {
        if (hwnd == 0 || Refused)
        {
            return false;
        }

        lock (Gate)
        {
            if (!Book.Mark(hwnd, mark))
            {
                return false;
            }
        }

        Pending.Set();
        StartServing();
        StartHelper();
        return true;
    }

    /// <summary>Starts the helper when an administrator window is in front and it is not running yet.</summary>
    public static void Ensure()
    {
        if (Refused || Running)
        {
            return;
        }

        StartServing();
        StartHelper();
    }

    /// <summary>
    /// A window took the foreground. An administrator window starts the helper, so chords work over it
    /// without waiting for Winoma to move, close, or focus it (ADR 0065).
    /// Runs on the window watch's thread for every foreground change, and returns at once.
    /// </summary>
    /// <param name="hwnd">The window now in front.</param>
    public static void InFront(nint hwnd)
    {
        if (hwnd == 0 || !WindowExecutor.Front(ShellBridge.Windows.IsElevated(hwnd), Running, Refused))
        {
            return;
        }

        Log.Information("Chord", "administrator window " + hwnd + " is in front; starting the elevated helper");
        Ensure();
    }

    /// <summary>Queues a raise, so a free window stays above the tiles. An administrator window needs the helper.</summary>
    public static void Raise(nint hwnd)
    {
        // A Host that does not tile administrator windows still has them Free, and a Free window is lifted at every arrange.
        // With no helper to ask, nothing is kept for one and no pipe is opened.
        if (Refused)
        {
            return;
        }

        lock (Gate)
        {
            if (!Book.Raise(hwnd))
            {
                return;
            }
        }

        Pending.Set();
        StartServing();
        StartHelper();
    }

    private static void StartServing()
    {
        if (Interlocked.Exchange(ref Serving, 1) == 1)
        {
            return;
        }

        ElevatedChordPipe.Serve(message => Keyed?.Invoke(message));

        var thread = new Thread(Serve)
        {
            IsBackground = true,
            Name = "winoma-place",
        };
        thread.Start();
    }

    private static void Serve()
    {
        // The name is one for the whole PC, so a desktop in another session can hold it. That is said once, and each try waits.
        var saidTaken = false;
        while (true)
        {
            // The writer flushes when it is disposed, and that throws on a pipe the helper left.
            // Nothing above this thread catches, so every way out of a connection ends inside this try.
            var connected = false;
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                saidTaken = false;
                pipe.WaitForConnection();
                connected = true;
                Connected = true;
                using var writer = new StreamWriter(pipe) { AutoFlush = true };
                WritePending(writer, pipe);
            }
            catch (IOException) when (connected)
            {
                Log.Warning("Tiling", "elevated placer disconnected");
            }
            catch (Exception ex)
            {
                // A pipe that did not open fails the same way on the next try, with no helper to wait for in between.
                if (connected || !saidTaken)
                {
                    Log.Warning("Tiling", "elevated placer pipe failed", ex);
                }

                saidTaken = !connected;
                Thread.Sleep(500);
            }
        }
    }

    private static void WritePending(StreamWriter writer, NamedPipeServerStream pipe)
    {
        WaitHandle? left = null;
        WaitHandle[]? either = null;
        try
        {
            while (pipe.IsConnected)
            {
                IReadOnlyList<ElevatedAct> acts;
                lock (Gate)
                {
                    acts = Book.Drain();
                }

                foreach (var act in acts)
                {
                    writer.WriteLine(act.Line);
                }

                // Woken by the next act, and by the helper this desktop started leaving.
                // A helper it did not start has no handle here, so the second is there for that one to be noticed.
                left ??= HelperLeft();
                if (left is null)
                {
                    Pending.WaitOne(HelperCheckMs);
                    continue;
                }

                either ??= [Pending, left];
                if (WaitHandle.WaitAny(either, HelperNetMs) == 1)
                {
                    // Nobody reads this pipe now. The caller opens a new one for the next helper.
                    return;
                }
            }
        }
        finally
        {
            left?.Dispose();
        }
    }

    /// <summary>Signals when the helper this desktop started exits. Null when it started none, or the handle cannot be waited on.</summary>
    private static WaitHandle? HelperLeft()
    {
        try
        {
            if (Helper is not { HasExited: false } helper)
            {
                return null;
            }

            // The process object keeps the handle. This only waits on it.
            return new ManualResetEvent(false)
            {
                SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(helper.Handle, ownsHandle: false),
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    private static void StartHelper()
    {
        if (Refused)
        {
            return;
        }

        if (Helper is { HasExited: false })
        {
            return;
        }

        if (Interlocked.Exchange(ref Starting, 1) == 1)
        {
            return;
        }

        var thread = new Thread(Launch)
        {
            IsBackground = true,
            Name = "winoma-place-launch",
        };
        thread.Start();
    }

    private static void Launch()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                Denied = true;
                Log.Warning("Tiling", "could not place elevated windows");
                return;
            }

            Log.Information("Tiling", "asking to place elevated windows");

            // A helper that connected and left is let go before the flag is cleared for the next one.
            // Its exit would otherwise be read with the cleared flag while the consent prompt is up, and count as a refusal.
            Helper = null;
            Connected = false;
            Helper = Process.Start(new ProcessStartInfo(exe, "--place")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Exception ex)
        {
            Denied = true;
            Log.Warning("Tiling", "could not place elevated windows", ex);
        }
        finally
        {
            Interlocked.Exchange(ref Starting, 0);
        }
    }
}
