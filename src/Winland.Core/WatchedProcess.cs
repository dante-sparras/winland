namespace Winland.Core;

/// <summary>
/// Whether the process a watcher was asked to wait for is still that process.
/// <c>Process.WaitForExit</c> is not what the watcher uses: the watch thread is STA, and a WinExe that never
/// reached managed code can sit forever on a hidden runtime dialog. The watcher asks again instead.
/// </summary>
public static class WatchedProcess
{
    /// <summary>What a process id names at one moment.</summary>
    /// <param name="Started">When that process started.</param>
    /// <param name="HasExited">True when it has exited and the id has not been given out again.</param>
    public readonly record struct Sight(DateTimeOffset Started, bool HasExited);

    /// <summary>
    /// True when the id no longer names the process that started at <paramref name="started"/>.
    /// A missing id, an exit, and an id handed to a later process are all gone. A live process whose start could not be read is not.
    /// </summary>
    /// <param name="started">When the watched process started. Null when that could not be read.</param>
    /// <param name="now">The process at that id now. Null when the id is not a process.</param>
    /// <returns>True when the watcher should stop waiting.</returns>
    public static bool Gone(DateTimeOffset? started, Sight? now)
    {
        if (now is null || now.Value.HasExited)
        {
            return true;
        }

        // The id still names a live process, and reuse cannot be told apart without a start time.
        if (started is null)
        {
            return false;
        }

        return now.Value.Started.UtcDateTime != started.Value.UtcDateTime;
    }
}
