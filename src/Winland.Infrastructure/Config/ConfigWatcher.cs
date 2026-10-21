
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
        if (Interested(args.FullPath))
            Schedule();
    }

    private void OnRenamed(object? sender, RenamedEventArgs args)
    {
        if (Interested(args.FullPath) || Interested(args.OldFullPath))
            Schedule();
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
        lock (_gate)
        {
            if (_disposed)
                return;

            reload = _reload;
        }

        try
        {
            reload();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _ = exception;
        }
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
