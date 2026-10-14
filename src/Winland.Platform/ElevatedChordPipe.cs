using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;

namespace Winland.Platform;

/// <summary>
/// Carries chords from the elevated helper to the desktop, in <see cref="HookWire"/> lines.
/// While an administrator window is in front the desktop hook is not called, so the helper's hook takes the chord and sends it here.
/// The helper also sends <see cref="HookWire.Super"/> up when the desktop's Super went up in that window.
/// </summary>
internal static class ElevatedChordPipe
{
    private static readonly string PipeName = SessionPipe.For("Winland.ElevatedChords");
    private static readonly BlockingCollection<string> Outbox = new(boundedCapacity: 256);
    private static int _serving;
    private static int _sending;

    /// <summary>Desktop side: reads lines from the helper and raises <paramref name="received"/> on the pipe thread. Starts once.</summary>
    /// <param name="received">Runs for each line that parses.</param>
    public static void Serve(Action<HookWire> received)
    {
        if (Interlocked.Exchange(ref _serving, 1) == 1)
        {
            return;
        }

        var thread = new Thread(() => Listen(received))
        {
            IsBackground = true,
            Name = "winoma-elevated-chords",
        };
        thread.Start();
    }

    /// <summary>Helper side: queues <paramref name="message"/> for the desktop. Runs inside the hook, so it never blocks and never throws.</summary>
    /// <param name="message">The message.</param>
    public static void Send(HookWire message)
    {
        if (Interlocked.Exchange(ref _sending, 1) == 0)
        {
            var thread = new Thread(Write)
            {
                IsBackground = true,
                Name = "winoma-elevated-chords",
            };
            thread.Start();
        }

        Outbox.TryAdd(message.Format());
    }

    private static void Listen(Action<HookWire> received)
    {
        // The name is one for the whole PC, so a desktop in another session can hold it. That is said once, and each try waits.
        var saidTaken = false;
        while (true)
        {
            // Nothing above this thread catches, so opening the pipe and the handler are inside this try as well.
            var connected = false;
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                saidTaken = false;
                pipe.WaitForConnection();
                connected = true;
                using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                while (reader.ReadLine() is string line)
                {
                    if (HookWire.TryParse(line, out var message))
                    {
                        received(message);
                    }
                }
            }
            catch (Exception ex)
            {
                if (connected || !saidTaken)
                {
                    Log.Warning("Chord", "elevated chord pipe failed", ex);
                }

                if (!connected)
                {
                    saidTaken = true;
                    Thread.Sleep(500);
                }
            }
        }
    }

    private static void Write()
    {
        while (true)
        {
            // The helper connects without CurrentUserOnly: an elevated token's owner is Administrators, not the user, so that check fails.
            // The desktop's pipe already admits only this user.
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            try
            {
                pipe.Connect(5_000);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
                foreach (var line in Outbox.GetConsumingEnumerable())
                {
                    writer.WriteLine(line);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException)
            {
                // The desktop is not listening, or it went away. A chord queued meanwhile is stale by the next connection.
                while (Outbox.TryTake(out _))
                {
                }

                Thread.Sleep(500);
            }
        }
    }
}
