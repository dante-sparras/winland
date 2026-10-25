using System.Text;

namespace Winland.Daemon;

/// <summary>
/// Writes to another writer. <c>debug:disable_logs</c> false adds a copy of the log in a file.
/// </summary>
sealed class RollingLogWriter(TextWriter inner) : TextWriter
{
    private readonly Lock _gate = new();
    private TextWriter? _file;

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
            else
            {
                _file ??= openFile();
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
            _file?.Write(text);
            _file?.Flush();
        }
    }
}
