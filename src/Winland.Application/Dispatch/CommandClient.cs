using System.IO.Pipes;
using System.Text;

namespace Winland.Application;

public static class CommandClient
{
    public static string Transact(string command, string? pipeName = null, int timeoutMs = 2000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        using var pipe = new NamedPipeClientStream(
            ".",
            pipeName ?? CommandProtocol.PipeName,
            PipeDirection.InOut);

        try
        {
            pipe.Connect(timeoutMs);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("Winland is not running.");
        }

        var outbound = Encoding.UTF8.GetBytes(command + "\n");
        pipe.Write(outbound);
        pipe.Flush();
        return ReadReply(pipe);
    }

    public static void Follow(string command, Action<string> onLine, string? pipeName = null, int timeoutMs = 2000, CancellationToken cancel = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(onLine);
        using var pipe = new NamedPipeClientStream(
            ".",
            pipeName ?? CommandProtocol.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            pipe.Connect(timeoutMs);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("Winland is not running.");
        }

        using var registration = cancel.Register(static state =>
        {
            var stream = (NamedPipeClientStream)state!;
            try
            {
                stream.Dispose();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
            }
        }, pipe);

        var outbound = Encoding.UTF8.GetBytes(command + "\n");
        pipe.Write(outbound);
        pipe.Flush();
        try
        {
            while (!cancel.IsCancellationRequested && TryReadLine(pipe, out var line))
                onLine(line);
        }
        catch (Exception exception) when (cancel.IsCancellationRequested && exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private static string ReadReply(Stream stream)
    {
        try
        {
            return PipeFrame.ReadReply(stream);
        }
        catch (InvalidDataException)
        {
            throw new InvalidOperationException("The reply is too large.");
        }
    }

    private static bool TryReadLine(Stream stream, out string line)
    {
        line = "";
        try
        {
            if (PipeFrame.ReadLineAsync(stream, PipeFrame.ReplyLimit).GetAwaiter().GetResult() is not string read)
                return false;

            line = read;
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            throw new InvalidOperationException("The reply is too large.");
        }
    }
}
