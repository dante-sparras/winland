using System.Text;

namespace Winland;

/// <summary>
/// The last <see cref="Size"/> characters Winland wrote to its log, which <c>rollinglog</c> answers with, as the pinned
/// release keeps its rolling log.
/// </summary>
public static class RollingLog
{
    public const int Size = 4096;

    private static readonly Lock Gate = new();
    private static readonly StringBuilder Text = new();

    public static void Append(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        lock (Gate)
        {
            Text.Append(text);
            if (Text.Length > Size)
                Text.Remove(0, Text.Length - Size);
        }
    }

    public static string Read()
    {
        lock (Gate)
            return Text.ToString();
    }

    internal static void Clear()
    {
        lock (Gate)
            Text.Clear();
    }
}

/// <summary>Writes to another writer and keeps what it wrote in the <see cref="RollingLog"/>.</summary>
public sealed class RollingLogWriter(TextWriter inner) : TextWriter
{
    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value)
    {
        inner.Write(value);
        RollingLog.Append(value.ToString());
    }

    public override void Write(string? value)
    {
        inner.Write(value);
        RollingLog.Append(value);
    }

    public override void WriteLine(string? value)
    {
        inner.WriteLine(value);
        RollingLog.Append(value + inner.NewLine);
    }

    public override void Flush() => inner.Flush();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();

        base.Dispose(disposing);
    }
}
