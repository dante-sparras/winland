using System.Globalization;
using System.Text;

namespace Winland.Application;

/// <summary>
/// How text crosses the pipe. A request is one line. A reply is one line, or a <c>#&lt;length&gt;</c> line and then that
/// many bytes when the reply has a newline in it.
/// </summary>
public static class PipeFrame
{
    public const int RequestLimit = 64 * 1024;

    public const int ReplyLimit = 1024 * 1024;

    public static byte[] Frame(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // A line that starts with # would read as a frame's header, so it is framed too.
        if (!text.Contains('\n') && !text.StartsWith('#'))
            return Encoding.UTF8.GetBytes(text + "\n");

        var body = Encoding.UTF8.GetBytes(text);
        var header = Encoding.UTF8.GetBytes("#" + body.Length.ToString(CultureInfo.InvariantCulture) + "\n");
        var bytes = new byte[header.Length + body.Length];
        header.CopyTo(bytes, 0);
        body.CopyTo(bytes, header.Length);
        return bytes;
    }

    /// <summary>
    /// The text up to the next newline, without a carriage return. Null when the stream ends before any byte. A line
    /// longer than <paramref name="limit"/> throws <see cref="InvalidDataException"/>.
    /// </summary>
    public static async Task<string?> ReadLineAsync(Stream stream, int limit, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var body = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one, cancel).ConfigureAwait(false);
            if (read == 0)
            {
                if (body.Length == 0)
                    return null;

                break;
            }

            if (one[0] == (byte)'\n')
                break;

            if (one[0] == (byte)'\r')
                continue;

            if (body.Length >= limit)
                throw new InvalidDataException("The line is too large.");

            body.Write(one);
        }

        return Encoding.UTF8.GetString(body.GetBuffer().AsSpan(0, (int)body.Length));
    }

    /// <summary>
    /// One framed reply. Empty when the stream ends first. A frame longer than <see cref="ReplyLimit"/> throws
    /// <see cref="InvalidDataException"/>, and one the stream ends inside throws <see cref="EndOfStreamException"/>.
    /// </summary>
    public static string ReadReply(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (ReadLineAsync(stream, ReplyLimit).GetAwaiter().GetResult() is not string first)
            return "";

        if (first.Length <= 1
            || first[0] != '#'
            || !int.TryParse(first.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var length))
        {
            return first;
        }

        if (length > ReplyLimit)
            throw new InvalidDataException("The reply is too large.");

        var buffer = new byte[length];
        var got = 0;
        while (got < length)
        {
            var read = stream.Read(buffer, got, length - got);
            if (read == 0)
                throw new EndOfStreamException("The reply was cut short.");

            got += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, got);
    }
}
