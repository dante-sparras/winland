using System.Text;

namespace Winland.Ipc;

/// <summary>One JSON object per line, UTF-8, ended by a newline. The line itself holds no newline.</summary>
public static class LineCodec
{
    /// <summary>The longest line that is read. A query of every window fits; a line past this is refused.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>Writes <paramref name="line"/> and a newline, and flushes.</summary>
    /// <param name="stream">The pipe.</param>
    /// <param name="line">One JSON object, with no newline in it.</param>
    public static void Write(Stream stream, string line)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(line);
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    /// <summary>Reads one line. Blocks until a newline, the stream ends, or <paramref name="cancel"/> is signalled.</summary>
    /// <param name="stream">The pipe.</param>
    /// <param name="cancel">Stops the read. A cancelled read answers false.</param>
    /// <param name="line">The line, without the newline. Empty when the answer is false.</param>
    /// <returns>False when the stream ended, the line was too long, or the read was cancelled.</returns>
    public static bool TryRead(Stream stream, CancellationToken cancel, out string line)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var buffer = new List<byte>(128);
        var one = new byte[1];
        while (true)
        {
            if (cancel.IsCancellationRequested)
            {
                line = "";
                return false;
            }

            int read;
            try
            {
                read = stream.Read(one, 0, 1);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                line = "";
                return false;
            }

            if (read == 0)
            {
                line = "";
                return false;
            }

            if (one[0] == (byte)'\n')
            {
                break;
            }

            if (one[0] != (byte)'\r')
            {
                buffer.Add(one[0]);
            }

            if (buffer.Count > MaxBytes)
            {
                line = "";
                return false;
            }
        }

        line = Encoding.UTF8.GetString(CollectionsMarshalBytes(buffer));
        return true;
    }

    private static byte[] CollectionsMarshalBytes(List<byte> buffer)
    {
        var bytes = new byte[buffer.Count];
        for (var i = 0; i < buffer.Count; i++)
        {
            bytes[i] = buffer[i];
        }

        return bytes;
    }
}
