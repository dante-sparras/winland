using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace Winland.Platform;

/// <summary>
/// The keyboard hook process. Winoma starts it with <see cref="Switch"/> and the desktop's process id.
/// It runs <see cref="KeyboardHook"/> and nothing else, so a garbage collection, a hang, or a crash in the desktop
/// never holds up a key. It talks to the desktop over one pipe and exits when the desktop does, releasing every key it holds.
/// The hook callback never writes the pipe. It queues lines, and a writer thread sends them.
/// </summary>
public static class KeyboardHookHost
{
    /// <summary>The argument that starts Winoma as the keyboard hook process.</summary>
    public const string Switch = "--keys";

    /// <summary>The pipe's buffer each way. A line is 20 bytes at most, so many fit.</summary>
    private const int PipeBufferBytes = 4096;

    /// <summary>The pipe between one desktop and its hook process. Only the same user can open it.</summary>
    /// <param name="desktopPid">The desktop's process id.</param>
    /// <returns>The pipe name.</returns>
    public static string PipeName(int desktopPid) => "Winoma.Keys." + desktopPid;

    /// <summary>Runs until the desktop says quit, closes the pipe, or exits.</summary>
    /// <param name="desktopPid">The desktop's process id.</param>
    /// <param name="binds">The shell's binds. Called once the pipe is up, and disposed when this process stops.</param>
    /// <returns>The process exit code.</returns>
    public static int Run(int desktopPid, Func<HookProcessBinds> binds)
    {
        ArgumentNullException.ThrowIfNull(binds);
        Process desktop;
        try
        {
            desktop = Process.GetProcessById(desktopPid);
        }
        catch (ArgumentException)
        {
            Log.Warning("Chord", "keyboard hook process found no desktop " + desktopPid);
            return 1;
        }

        try
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
        }
        catch (Exception ex)
        {
            Log.Warning("Chord", "keyboard hook process kept normal priority", ex);
        }

        using var stop = new CancellationTokenSource();

        // With no buffer a write waits until the other side reads it. With one, a writer waits only on a reader that has fallen behind.
        using var pipe = new NamedPipeServerStream(
            PipeName(desktopPid),
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            PipeBufferBytes,
            PipeBufferBytes);
        desktop.EnableRaisingEvents = true;
        desktop.Exited += (_, _) =>
        {
            try
            {
                stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The pipe broke first and this process is already on its way out.
            }
        };
        if (desktop.HasExited)
        {
            return 0;
        }

        try
        {
            pipe.WaitForConnectionAsync(stop.Token).WaitAsync(TimeSpan.FromSeconds(10), stop.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException)
        {
            Log.Warning("Chord", "keyboard hook process saw no desktop connect");
            return 1;
        }

        // Closing the pipe ends the read below when the desktop exits without saying quit.
        stop.Token.Register(pipe.Dispose);
        var outbox = new BlockingCollection<string>();
        var writer = new Thread(() => Write(pipe, outbox))
        {
            IsBackground = true,
            Name = "winoma-keys-write",
        };
        writer.Start();

        var superSent = 0;
        using var hook = new KeyboardHook(superDown: () =>
        {
            // Inside the hook. Only queue, and only on the change.
            if (Interlocked.Exchange(ref superSent, 1) == 0)
            {
                Post(outbox, new HookWire.Super(true));
            }
        });
        hook.ChordQueued += () =>
        {
            while (hook.TryTake(out var chord, out var down, out _))
            {
                Post(outbox, new HookWire.ChordKeyed(chord, down));
            }
        };
        hook.SuperReleased += () =>
        {
            Interlocked.Exchange(ref superSent, 0);
            Post(outbox, new HookWire.Super(false));
        };
        hook.EscapeTaken += () => Post(outbox, new HookWire.EscapeTaken());
        hook.PickerKeyTaken += key => Post(outbox, new HookWire.PickerKeyTaken(key));

        // The shell names the plugins and the chord file. This process only honors what it hands over.
        using var catalog = binds();
        var book = catalog.Book;
        hook.UseChords(catalog.Ledger);
        hook.UseBinds(book);
        hook.Install();
        using var stall = new System.Threading.Timer(_ => hook.WatchStall(), null, 1000, 1000);
        Log.Information("Chord", "keyboard hook process serving desktop " + desktopPid);
        try
        {
            Serve(pipe, hook, outbox, book);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The desktop went away.
        }
        finally
        {
            outbox.CompleteAdding();
        }

        Log.Information("Chord", "keyboard hook process stopping");
        return 0;
    }

    private static void Serve(NamedPipeServerStream pipe, KeyboardHook hook, BlockingCollection<string> outbox, BindBook book)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        while (reader.ReadLine() is string line)
        {
            if (!HookWire.TryParse(line, out var message))
            {
                continue;
            }

            switch (message)
            {
                case HookWire.EscapeMode mode:
                    book.Ask(BindConditions.Escape, mode.Cancels);
                    break;
                case HookWire.PickerMode mode:
                    hook.PickerTakesKeys = mode.Open;
                    break;
                case HookWire.Deliver:
                    hook.KeyboardMoved();
                    break;
                case HookWire.Pointer:
                    hook.SamplePointer();
                    if (hook.TakeHover())
                    {
                        Post(outbox, new HookWire.Hovered());
                    }

                    break;
                case HookWire.Release release:
                    hook.ReleaseForLock();
                    Post(outbox, new HookWire.Reply(release.Id, true));
                    break;
                case HookWire.Forget:
                    hook.ForgetSuper();
                    break;
                case HookWire.Quit:
                    return;
            }
        }
    }

    /// <summary>Queues a line. Runs inside the hook too, so it never blocks and never throws.</summary>
    private static void Post(BlockingCollection<string> outbox, HookWire message)
    {
        try
        {
            outbox.TryAdd(message.Format());
        }
        catch (InvalidOperationException)
        {
            // Stopping. Nothing reads the line now.
        }
    }

    private static void Write(Stream pipe, BlockingCollection<string> outbox)
    {
        try
        {
            using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true)
            {
                AutoFlush = true,
            };
            foreach (var line in outbox.GetConsumingEnumerable())
            {
                writer.WriteLine(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The desktop went away. The read loop ends too.
        }
    }
}
