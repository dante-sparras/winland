using System.Globalization;
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
        return ReadReply(pipe, 1024 * 1024);
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
            while (!cancel.IsCancellationRequested && TryReadLine(pipe, 1024 * 1024, out var line))
                onLine(line);
        }
        catch (Exception exception) when (cancel.IsCancellationRequested && exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private static string ReadReply(Stream stream, int maxBytes)
    {
        if (!TryReadLine(stream, maxBytes, out var first))
            return "";

        if (first.Length > 1
            && first[0] == '#'
            && int.TryParse(first.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var length)
            && length >= 0
            && length <= maxBytes)
        {
            var buffer = new byte[length];
            var read = 0;
            while (read < length)
            {
                int n;
                try
                {
                    n = stream.Read(buffer, read, length - read);
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    break;
                }

                if (n == 0)
                    break;

                read += n;
            }

            return Encoding.UTF8.GetString(buffer, 0, read);
        }

        return first;
    }

    private static bool TryReadLine(Stream stream, int maxBytes, out string line)
    {
        line = "";
        using var body = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            int read;
            try
            {
                read = stream.Read(one);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                return false;
            }

            if (read == 0)
            {
                if (body.Length == 0)
                    return false;

                break;
            }

            if (one[0] == (byte)'\n')
                break;

            if (one[0] == (byte)'\r')
                continue;

            if (body.Length >= maxBytes)
                throw new InvalidOperationException("The reply is too large.");

            body.Write(one);
        }

        line = Encoding.UTF8.GetString(body.GetBuffer().AsSpan(0, (int)body.Length));
        return true;
    }
}
