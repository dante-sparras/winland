using System.Globalization;

namespace Winland.Core.Diagnostics;

/// <summary>
/// Appends records to a file and keeps the previous chunk when the file grows past a limit.
/// The same record inside <see cref="RepeatWindow"/> is counted instead of written again.
/// A record that comes with its own time is written and counted by that time, and any other by the clock.
/// </summary>
public sealed class FileLog : ITimedLog
{
    /// <summary>Default cap for the current file, in bytes. The previous chunk is kept beside it.</summary>
    public const long DefaultMaxBytes = 1_048_576;

    /// <summary>How long an identical record is counted instead of written again.</summary>
    public static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(5);

    private static readonly Mutex FileGate = new(false, @"Local\Winland.FileLog");

    private readonly string _path;
    private readonly long _maxBytes;
    private readonly Func<DateTimeOffset> _clock;

    private readonly object _gate = new();
    private readonly Dictionary<string, Recent> _recent = [];
    private readonly List<string> _expired = [];
    private bool _directoryReady;

    /// <summary>Creates a log at <paramref name="path"/>.</summary>
    /// <param name="path">File that receives records. Parent directories are created.</param>
    /// <param name="maxBytes">Size that triggers a rotation. The previous file is <c>path.1</c>.</param>
    /// <param name="clock">Clock used for timestamps and repeat suppression. Null uses the current time.</param>
    public FileLog(string path, long maxBytes = DefaultMaxBytes, Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);

        _path = path;
        _maxBytes = maxBytes;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary><c>%USERPROFILE%\.config\winland\winland.log</c>.</summary>
    public static string HostPath =>
        Path.Combine(ConfigFolder.Path, "winland.log");

    /// <inheritdoc />
    public void Write(LogSeverity severity, string area, string message, Exception? error = null) =>
        Record(null, severity, area, message, error);

    /// <inheritdoc />
    public void Write(DateTimeOffset at, LogSeverity severity, string area, string message, Exception? error = null) =>
        Record(at, severity, area, message, error);

    private void Record(DateTimeOffset? at, LogSeverity severity, string area, string message, Exception? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var flat = OneLine(message);
        var signature = severity + "\n" + area + "\n" + flat + "\n" + (error?.GetType().FullName ?? "") + "\n" + (error?.Message ?? "");
        lock (_gate)
        {
            try
            {
                var now = at ?? _clock();
                if (_recent.TryGetValue(signature, out var recent) && Fresh(now, recent.At))
                {
                    recent.Repeats++;
                    return;
                }

                _expired.Clear();
                foreach (var pair in _recent)
                {
                    if (Fresh(now, pair.Value.At))
                    {
                        continue;
                    }

                    if (pair.Value.Repeats > 0)
                    {
                        Append(now, LogSeverity.Information, "Log", RepeatText(pair.Value), null);
                    }

                    _expired.Add(pair.Key);
                }

                foreach (var key in _expired)
                {
                    _recent.Remove(key);
                }

                Append(now, severity, area, flat, error);
                _recent[signature] = new Recent(now, severity, area, flat);
            }
            catch (Exception)
            {
                // A full disk must not take down the desktop.
            }
        }
    }

    private void Append(DateTimeOffset now, LogSeverity severity, string area, string message, Exception? error)
    {
        var held = false;
        try
        {
            try
            {
                held = FileGate.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                held = true;
            }

            AppendFile(now, severity, area, message, error);
        }
        finally
        {
            if (held)
            {
                FileGate.ReleaseMutex();
            }
        }
    }

    private void AppendFile(DateTimeOffset now, LogSeverity severity, string area, string message, Exception? error)
    {
        EnsureDirectory();
        RotateIfNeeded();
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{now:o} {severity} {area} pid={Environment.ProcessId} {message}{Environment.NewLine}");
        if (error is not null)
        {
            line += error + Environment.NewLine;
        }

        try
        {
            File.AppendAllText(_path, line);
        }
        catch (DirectoryNotFoundException)
        {
            // The folder was removed while Winoma ran. Make it again once instead of on every record.
            _directoryReady = false;
            EnsureDirectory();
            File.AppendAllText(_path, line);
        }
    }

    private void EnsureDirectory()
    {
        if (_directoryReady)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _directoryReady = true;
    }

    private void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length < _maxBytes)
            {
                return;
            }

            File.Move(_path, _path + ".1", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another program holds the file. The record still goes into the oversized file, and the next write tries again.
        }
    }

    /// <summary>
    /// True while a record written at <paramref name="at"/> still counts its repeats. One dated after <paramref name="now"/> was written
    /// before the clock was set back, and is old: its repeats would otherwise be held back until the clock passed it again.
    /// </summary>
    private static bool Fresh(DateTimeOffset now, DateTimeOffset at)
    {
        var age = now - at;
        return age >= TimeSpan.Zero && age < RepeatWindow;
    }

    private static string RepeatText(Recent recent) =>
        "suppressed " + recent.Repeats + (recent.Repeats == 1 ? " repeat of " : " repeats of ") + recent.Severity + " " + recent.Area + ": " + recent.Message;

    private static string OneLine(string message) =>
        message.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private sealed class Recent(DateTimeOffset at, LogSeverity severity, string area, string message)
    {
        public DateTimeOffset At { get; } = at;

        public int Repeats { get; set; }

        public LogSeverity Severity { get; } = severity;

        public string Area { get; } = area;

        public string Message { get; } = message;
    }
}
