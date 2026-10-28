using System.Text;

namespace Winland.Daemon;

/// <summary>
/// Writes to another writer. <c>debug:disable_logs</c> false adds a copy of the log in a file.
/// </summary>
sealed class RollingLogWriter(TextWriter inner) : TextWriter
{
    // Keep one backup past this many characters, so a session of days does not fill the disk.
    private const long Limit = 10 * 1024 * 1024;

    private readonly Lock _gate = new();
    private TextWriter? _file;
    private long _written;
    private bool _lineStart = true;

    public override Encoding Encoding => inner.Encoding;

    internal static StreamWriter OpenFile(string path)
    {
        if (File.Exists(path))
            File.Move(path, path + ".1", overwrite: true);

        return new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
    }

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
                CloseFile();
                return;
            }
            if (_file is not null)
                return;
        }

        // Opening can report through Console.Error, whose lock must not be taken under _gate.
        var opened = openFile();
        lock (_gate)
        {
            if (_file is null)
            {
                _file = opened;
                _written = 0;
                _lineStart = true;
            }
            else
                opened?.Dispose();
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

    public override void Write(char[] buffer, int index, int count)
    {
        inner.Write(buffer, index, count);
        Copies(new string(buffer, index, count));
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
        {
            try
            {
                _file?.Flush();
            }
            catch (IOException)
            {
                CloseFile();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            lock (_gate)
                CloseFile();
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

            try
            {
                _written += text.Length;
                if (_written > Limit && _file is StreamWriter { BaseStream: FileStream stream })
                {
                    var path = stream.Name;
                    CloseFile();
                    _file = OpenFile(path);
                    _written = text.Length;
                    _lineStart = true;
                }

                var remaining = text.AsSpan();
                while (!remaining.IsEmpty)
                {
                    if (_lineStart)
                        _file.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ", System.Globalization.CultureInfo.InvariantCulture));

                    var newline = remaining.IndexOf('\n');
                    var length = newline < 0 ? remaining.Length : newline + 1;
                    _file.Write(remaining[..length]);
                    _lineStart = newline >= 0;
                    remaining = remaining[length..];
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                CloseFile();
            }
        }
    }

    private void CloseFile()
    {
        var file = _file;
        _file = null;
        try
        {
            file?.Dispose();
        }
        catch (IOException)
        {
            // A full disk can fail again while closing. Console.Error must still be usable.
        }
    }
}
