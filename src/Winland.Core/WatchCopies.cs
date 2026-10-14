namespace Winland.Core;

/// <summary>
/// The copies under <c>%LOCALAPPDATA%\Winoma\watch</c>. Winland's folder name starts with <c>w-</c>. The shell's does not.
/// Each product removes only its own old copies. Removing the other's deletes every file that watcher has not locked yet,
/// and a WinExe left without <c>hostfxr.dll</c> then waits on a hidden "install .NET" dialog instead of noticing its process has gone (ADR 0059).
/// </summary>
public static class WatchCopies
{
    /// <summary>True when <paramref name="folder"/> is one of Winland's copies.</summary>
    /// <param name="folder">A directory under the watch folder.</param>
    /// <returns>True when the last part of the path starts with <c>w-</c>.</returns>
    public static bool IsWinland(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return Path.GetFileName(folder).StartsWith("w-", StringComparison.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="folder"/> is an old copy <paramref name="winland"/> may remove.
    /// The copy just made is kept, and so is the other product's.
    /// </summary>
    /// <param name="folder">A directory under the watch folder.</param>
    /// <param name="kept">The copy just made. It is never removable.</param>
    /// <param name="winland">True when the caller is Winland. False when it is the shell.</param>
    /// <returns>True when this product may delete <paramref name="folder"/>.</returns>
    public static bool Removable(string folder, string kept, bool winland)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(kept);
        if (string.Equals(folder, kept, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsWinland(folder) == winland;
    }
}
