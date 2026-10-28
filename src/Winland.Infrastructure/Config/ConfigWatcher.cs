namespace Winland.Infrastructure;

public sealed class ConfigWatcher : IDisposable
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(150);

    private readonly Action _reload;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _timer;
    private bool _disposed;

    // What the watched files held when the last save reloaded them, so a save that changed nothing reloads nothing.
    private string? _lastRead;

    // A file an editor is still writing can refuse to be read for a moment.
    private static readonly TimeSpan ReadRetry = TimeSpan.FromMilliseconds(100);

    public ConfigWatcher(Action reload, TimeSpan? debounce = null)
    {
        ArgumentNullException.ThrowIfNull(reload);
        if (debounce is { } delay && delay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(debounce));

        _reload = reload;
        _debounce = debounce ?? DefaultDebounce;
    }

    public void Watch(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        List<FileSystemWatcher> previous;
        lock (_gate)
        {
            if (_disposed)
                return;

            var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                next.Add(Path.GetFullPath(path));
            }

            _paths = next;
            _lastRead = Read([.. next]);
            previous = _watchers.ToList();
            _watchers.Clear();
            foreach (var directory in Directories(next))
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    Filter = "*.*",
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    InternalBufferSize = 64 * 1024,
                };
                watcher.Changed += OnChanged;
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Renamed += OnRenamed;
                watcher.Error += OnError;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
        }

        Release(previous);
    }

    public void Dispose()
    {
        Timer? timer;
        List<FileSystemWatcher> watchers;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            timer = _timer;
            _timer = null;
            watchers = _watchers.ToList();
            _watchers.Clear();
            _paths.Clear();
        }

        timer?.Dispose();
        Release(watchers);
    }

    private static List<string> Directories(HashSet<string> paths)
    {
        var directories = new List<string>();
        foreach (var path in paths)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                continue;

            var seen = false;
            foreach (var existing in directories)
            {
                if (string.Equals(existing, directory, StringComparison.OrdinalIgnoreCase))
                {
                    seen = true;
                    break;
                }
            }

            if (!seen)
                directories.Add(directory);
        }

        return directories;
    }

    private void OnChanged(object? sender, FileSystemEventArgs args)
    {
        if (Interested(args.FullPath) || (args.ChangeType != WatcherChangeTypes.Changed && Sibling(args.FullPath)))
            Schedule();
    }

    private void OnRenamed(object? sender, RenamedEventArgs args)
    {
        if (Interested(args.FullPath) || Interested(args.OldFullPath) || Sibling(args.FullPath) || Sibling(args.OldFullPath))
            Schedule();
    }

    // A file of the same kind as a watched one, appearing or going in a watched folder, may be one a glob source
    // such as conf.d/*.conf takes in or loses.
    private bool Sibling(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extension = Path.GetExtension(path);
        if (extension.Length == 0)
            return false;

        lock (_gate)
            return _paths.Any(watched => string.Equals(Path.GetExtension(watched), extension, StringComparison.OrdinalIgnoreCase));
    }

    private void OnError(object? sender, ErrorEventArgs args) => Schedule();

    private bool Interested(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return false;
        }

        lock (_gate)
            return _paths.Contains(full);
    }

    private void Schedule()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _timer ??= new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        Action reload;
        string[] paths;
        lock (_gate)
        {
            if (_disposed)
                return;

            reload = _reload;
            paths = [.. _paths];
        }

        // A file still being written is waited for once. A save that changed nothing, as Ctrl+S on an unchanged file
        // or an editor that writes twice, does not run the config's exec lines again.
        var read = Read(paths);
        if (read is null)
        {
            Thread.Sleep(ReadRetry);
            read = Read(paths);
        }

        lock (_gate)
        {
            // Disposed while the files were read: the exit has begun.
            if (_disposed || (read is not null && read == _lastRead))
                return;

            _lastRead = read;
        }

        try
        {
            reload();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception);
        }
    }

    // The text of every watched file, in a fixed order, or null when one could not be read. A file that is gone reads
    // as gone.
    private static string? Read(string[] paths)
    {
        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
        var all = new System.Text.StringBuilder();
        foreach (var path in paths)
        {
            all.Append(path).Append('\0');
            try
            {
                all.Append(File.Exists(path) ? File.ReadAllText(path) : "\u0001gone").Append('\0');
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        // The files a glob source could take in, so one that appears or goes is a change.
        foreach (var directory in Directories(new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase)))
        {
            var extensions = paths.Select(path => Path.GetExtension(path) ?? "").Where(extension => extension.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var extension in extensions)
            {
                string[] names;
                try
                {
                    names = Directory.GetFiles(directory, "*" + extension);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return null;
                }

                Array.Sort(names, StringComparer.OrdinalIgnoreCase);
                all.AppendJoin('|', names).Append('\0');
            }
        }

        return all.ToString();
    }

    private static void Release(List<FileSystemWatcher> watchers)
    {
        foreach (var watcher in watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }
}
