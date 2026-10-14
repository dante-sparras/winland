namespace Winland.Platform;

/// <summary>
/// The action that drops a held Super before Windows locks.
/// The keyboard hook arms it. The shell runs it on the thread that asks for the lock.
/// </summary>
public static class LockRelease
{
    /// <summary>Drops the fingers the hook still holds. Null until the hook arms it.</summary>
    public static Action? BeforeLock { get; set; }
}
