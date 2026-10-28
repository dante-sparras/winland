using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Winland.Daemon;

/// <summary>The commands that talk to the running Winland over its pipe: <c>winland dispatch</c> and the rest.</summary>
static class ControlCommand
{
    public static int Run(string line, bool events, string? pipeName)
    {
        WriteUtf8WhenRedirected();
        if (events)
            return FollowEvents(pipeName);

        try
        {
            var reply = CommandClient.Transact(line, pipeName, connected: LendForeground);
            return WriteReply(line, reply);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static int WriteReply(string line, string reply)
    {
        if (CommandProtocol.Parse(line) is CommandParsed { Request: BatchRequest })
        {
            Console.WriteLine(reply);
            return reply.Split("\n\n\n", StringSplitOptions.TrimEntries).Any(part => part.StartsWith("error:", StringComparison.Ordinal)) ? 1 : 0;
        }

        // A reply with nothing in it means the server failed before it could answer.
        if (reply.Length == 0 || reply.StartsWith("error:", StringComparison.Ordinal))
        {
            Console.Error.WriteLine(reply.Length == 0 ? "error: no reply" : reply);
            return 1;
        }

        Console.WriteLine(reply);

        return 0;
    }

    private static int FollowEvents(string? pipeName)
    {
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, args) =>
        {
            args.Cancel = true;
            cancel.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            CommandClient.Follow("events", static line =>
            {
                Console.WriteLine(line);
                Console.Out.Flush();
            }, pipeName, cancel: cancel.Token);
            if (cancel.IsCancellationRequested)
                return 0;

            // Winland exited, or dropped this reader for falling too far behind, so a supervisor knows to reconnect.
            Console.Error.WriteLine("error: events stream ended");
            return 1;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    // A bar or a script reads what this prints as UTF-8, whatever code page the console has. A console a person reads
    // keeps its own writer, which shows every character.
    private static void WriteUtf8WhenRedirected()
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        if (Console.IsOutputRedirected)
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
        if (Console.IsErrorRedirected)
            Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
    }

    // This process just had the user's key or click, so it may hand the foreground on. Winland, which answers on a pipe
    // thread, can then focus the window a dispatcher asks for without its fallbacks.
    private static void LendForeground(NamedPipeClientStream pipe)
    {
        if (GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var server) && server != 0)
            _ = AllowSetForegroundWindow(server);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(nint pipe, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
