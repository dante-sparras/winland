using System.IO.Pipes;
using System.Text;

namespace Winland.Infrastructure;

public static class CommandClient
{
    /// <param name="connected">Runs once the pipe is connected, before the command is sent.</param>
    public static string Transact(string command, string? pipeName = null, int timeoutMs = 2000, Action<NamedPipeClientStream>? connected = null, int replyTimeoutMs = 10_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        using var pipe = new NamedPipeClientStream(
            ".",
            pipeName ?? PipeServer.DefaultName,
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

        PipeServer.VerifyServer(pipe);
        connected?.Invoke(pipe);
        using var timeout = new CancellationTokenSource(replyTimeoutMs);
        try
        {
            var outbound = Encoding.UTF8.GetBytes(command + "\n");
            pipe.WriteAsync(outbound, timeout.Token).AsTask().GetAwaiter().GetResult();
            pipe.FlushAsync(timeout.Token).GetAwaiter().GetResult();

            // Read in blocks, not a call per byte. Nothing else reads this pipe.
            using var buffered = new BufferedStream(pipe);
            return ReadReply(buffered, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new InvalidOperationException("Winland is not answering.");
        }
    }

    public static void Follow(string command, Action<string> onLine, string? pipeName = null, int timeoutMs = 2000, CancellationToken cancel = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(onLine);
        using var pipe = new NamedPipeClientStream(
            ".",
            pipeName ?? PipeServer.DefaultName,
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

        PipeServer.VerifyServer(pipe);
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
        using var buffered = new BufferedStream(pipe);
        try
        {
            while (!cancel.IsCancellationRequested && TryReadLine(buffered, out var line))
                onLine(line);
        }
        catch (Exception exception) when (cancel.IsCancellationRequested && exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private static string ReadReply(Stream stream, CancellationToken cancel)
    {
        try
        {
            return PipeFrame.ReadReply(stream, cancel);
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
