namespace Winland.Install;

/// <summary>
/// Stable treats the running version and the published <c>version.json</c> version as the same build
/// when they are the same text. Any other published version is the one to install, including an older stable
/// version than the dev build that is running. Dev compares the commits instead.
/// </summary>
public static class ReleaseVersion
{
    public static bool IsCurrent(string running, string published)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(running);
        ArgumentException.ThrowIfNullOrWhiteSpace(published);
        return running.Equals(published, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when both commits are present and equal, ignoring case. An empty commit is never current,
    /// so a build that recorded none still updates.
    /// </summary>
    public static bool IsSameCommit(string running, string published)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(published);
        if (string.IsNullOrWhiteSpace(running) || string.IsNullOrWhiteSpace(published))
            return false;

        return running.Equals(published, StringComparison.OrdinalIgnoreCase);
    }
}
