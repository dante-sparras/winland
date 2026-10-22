namespace Winland.Infrastructure;

/// <summary>
/// One Daemon per session and pipe. The named mutex lives in the session's Local namespace and goes away
/// with the process that holds it, so a crashed Daemon does not block the next start.
/// </summary>
public sealed class InstanceLock : IDisposable
{
    private readonly Mutex _mutex;

    private InstanceLock(Mutex mutex) => _mutex = mutex;

    public static string NameFor(string pipe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipe);
        return @"Local\Winland-" + pipe.Replace('\\', '_');
    }

    /// <summary>Null when another process in this session already holds <paramref name="name"/>.</summary>
    public static InstanceLock? TryTake(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Mutex mutex;
        bool created;
        try
        {
            mutex = new Mutex(false, name, out created);
        }
        catch (UnauthorizedAccessException)
        {
            // An instance at another integrity level holds it.
            return null;
        }

        if (created)
            return new InstanceLock(mutex);

        mutex.Dispose();
        return null;
    }

    public void Dispose() => _mutex.Dispose();
}
