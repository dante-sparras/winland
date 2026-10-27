using System.Text;

namespace Winland.Daemon;

/// <summary>
/// Writes to another writer. <c>debug:disable_logs</c> false adds a copy of the log in a file.
/// </summary>
sealed class RollingLogWriter(TextWriter inner) : TextWriter
{
    // The file starts over past this many characters, so a session of days does not fill the disk.
    private const long Limit = 10 * 1024 * 1024;

    private readonly Lock _gate = new();
    private TextWriter? _file;
    private long _written;

    public override Encoding Encoding => inner.Encoding;

    /// <summary>
    /// Sets the copy <paramref name="options"/> asks for. <paramref name="openFile"/> opens the log file the first time
    /// it is wanted. The file is closed when it is no longer wanted.
    /// </summary>
    public void Copy(DebugOptions options, Func<TextWriter?> openFile)
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
            else if (_file is null)
            {
                _file = openFile();
                _written = 0;
            }
        }
    }

    public override void Write(char value)
    {
        inner.Write(value);
        Copies(value.ToString());
    }

    public override void Write(string? value)
    {
        inner.Write(value);
        Copies(value);
    }

    public override void WriteLine(string? value)
    {
        inner.WriteLine(value);
        Copies(value + inner.NewLine);
    }

    public override void Flush()
    {
        inner.Flush();
        lock (_gate)
            _file?.Flush();
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

    private void Copies(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        lock (_gate)
        {
            if (_file is null)
                return;

            _written += text.Length;
            if (_written > Limit && _file is StreamWriter { BaseStream: { CanSeek: true } stream })
            {
                _file.Flush();
                stream.SetLength(0);
                _written = text.Length;
            }

            _file.Write(text);
        }
    }
}
