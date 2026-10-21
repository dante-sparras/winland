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

/// <summary>
/// Writes to another writer and keeps what it wrote in the <see cref="RollingLog"/>. The <c>debug</c> options can add
/// copies of the log, to a file and to standard output, whose lines may start with the time, as Winland's
/// logger writes its file and standard output.
/// </summary>
public sealed class RollingLogWriter(TextWriter inner, Func<DateTime>? clock = null) : TextWriter
{
    private const string Dim = "\u001b[90m";
    private const string Plain = "\u001b[0m";

    private readonly Lock _gate = new();
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.Now);
    private TextWriter? _file;
    private TextWriter? _standardOutput;
    private bool _time;
    private bool _color;
    private bool _lineStart = true;

    public override Encoding Encoding => inner.Encoding;

    /// <summary>
    /// Sets the copies <paramref name="options"/> ask for. <paramref name="openFile"/> opens the log file the first time
    /// one is wanted. The file is closed when it is no longer wanted.
    /// </summary>
    public void Copy(DebugOptions options, Func<TextWriter?> openFile, TextWriter standardOutput)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(openFile);
        lock (_gate)
        {
            if (options.DisableLogs)
            {
                _file?.Dispose();
                _file = null;
            }
            else
            {
                _file ??= openFile();
            }

            _standardOutput = !options.DisableLogs && options.EnableStdoutLogs ? standardOutput : null;
            _time = !options.DisableTime;
            _color = options.ColoredStdoutLogs;
        }
    }

    public override void Write(char value)
    {
        inner.Write(value);
        RollingLog.Append(value.ToString());
        Copies(value.ToString());
    }

    public override void Write(string? value)
    {
        inner.Write(value);
        RollingLog.Append(value);
        Copies(value);
    }

    public override void WriteLine(string? value)
    {
        inner.WriteLine(value);
        RollingLog.Append(value + inner.NewLine);
        Copies(value + inner.NewLine);
    }

    public override void Flush()
    {
        inner.Flush();
        lock (_gate)
        {
            _file?.Flush();
            _standardOutput?.Flush();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            lock (_gate)
            {
                _file?.Dispose();
                _file = null;
            }
        }

        base.Dispose(disposing);
    }

    // Each line of the copies starts with the time when the options ask for it, dimmed on a colored standard output.
    private void Copies(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        lock (_gate)
        {
            if (_file is null && _standardOutput is null)
                return;

            var start = 0;
            while (start < text.Length)
            {
                var end = text.IndexOf('\n', start);
                var piece = end < 0 ? text[start..] : text[start..(end + 1)];
                if (_lineStart && _time)
                {
                    var stamp = "[" + _clock().ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) + "] ";
                    _file?.Write(stamp);
                    _standardOutput?.Write(_color ? Dim + stamp + Plain : stamp);
                }

                _file?.Write(piece);
                _standardOutput?.Write(piece);
                _lineStart = piece.EndsWith('\n');
                start += piece.Length;
            }

            _file?.Flush();
        }
    }
}
