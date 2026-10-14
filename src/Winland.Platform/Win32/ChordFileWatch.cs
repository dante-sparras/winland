
namespace Winland.Platform.Win32;

/// <summary>
/// Watches <c>binds.json</c> and hands over the chord book each time a save leaves a file that parses (ADR 0067).
/// The desktop, the keyboard hook process, and the elevated helper each run one, so a saved key map needs no restart.
/// A save lands in bursts, so the file is read 300 ms after its last event, on a pool thread.
/// </summary>
public sealed class ChordFileWatch : IDisposable
{
    private const int SettleMilliseconds = 300;

    private readonly string _path;
    private readonly Func<IReadOnlyList<DeclaredPlugin>?> _plugins;
    private readonly Action<ChordBook> _changed;
    private readonly FileSystemWatcher _watch;
    private readonly System.Threading.Timer _settle;
    private readonly object _applyGate = new();
    private string _text;

    /// <summary>Starts watching.</summary>
    /// <param name="path">The chord file.</param>
    /// <param name="plugins">The running plugins, read at each reload. Null reads the file without them.</param>
    /// <param name="changed">Takes the new book. Runs on a pool thread.</param>
    public ChordFileWatch(string path, Func<IReadOnlyList<DeclaredPlugin>?> plugins, Action<ChordBook> changed)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(changed);
        _path = path;
        _plugins = plugins;
        _changed = changed;
        _text = ReadMerged() ?? "";
        _settle = new System.Threading.Timer(_ => Apply(), null, Timeout.Infinite, Timeout.Infinite);
        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        _watch = new FileSystemWatcher(folder, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        _watch.Changed += OnFile;
        _watch.Created += OnFile;
        _watch.Renamed += OnFile;
        _watch.EnableRaisingEvents = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watch.Dispose();
        _settle.Dispose();
    }

    private void OnFile(object sender, FileSystemEventArgs args)
    {
        try
        {
            _settle.Change(SettleMilliseconds, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private string? ReadText()
    {
        try
        {
            return File.Exists(_path) ? File.ReadAllText(_path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The merged document. A root file that is not JSON comes back raw, so the caller keeps the chords it has.</summary>
    private string? ReadMerged()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return ConfigJson.Read(_path).Json;
        }
        catch (System.Text.Json.JsonException)
        {
            return ReadText();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Apply()
    {
        // One reload at a time. A save that lands while a slow reload runs gets its own turn after it,
        // so an older book is never handed over after a newer one.
        lock (_applyGate)
        {
            var applied = _text;
            try
            {
                // Still being written, or deleted. The next event reads it again.
                ConfigJson.ReadResult? merged = null;
                string? text;
                try
                {
                    if (!File.Exists(_path))
                    {
                        return;
                    }

                    merged = ConfigJson.Read(_path);
                    text = merged.Value.Json;
                }
                catch (System.Text.Json.JsonException)
                {
                    text = ReadText();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return;
                }

                if (text is null || text == _text)
                {
                    return;
                }

                _text = text;
                if (merged is { } read)
                {
                    foreach (var note in read.Notes)
                    {
                        Log.Warning("Chord", note);
                    }
                }

                if (!ChordSwitch.Parses(text))
                {
                    Log.Warning("Chord", "binds.json has no chords list that parses; the chords in use stay");
                    return;
                }

                // The text that was just checked, not a second read of the file. A save that has emptied the file by then
                // would read as the built-in map and replace the person's chords.
                var book = ChordBook.Parse(text, _plugins());
                foreach (var warning in book.Warnings)
                {
                    Log.Warning("Chord", warning);
                }

                _changed(book);
                Log.Information("Chord", "chords reloaded");
            }
            catch (Exception ex)
            {
                // The same text is read again on the file's next event, since it was never applied.
                _text = applied;
                Log.Error("Chord", "could not reload binds.json", ex);
            }
        }
    }
}
