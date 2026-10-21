namespace Winland.Install;

/// <summary>
/// The running version and the published <c>version.json</c> version match when they are the same text.
/// Any other published version is the one to install.
/// </summary>
public static class ReleaseVersion
{
    public static bool IsCurrent(string running, string published)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(running);
        ArgumentException.ThrowIfNullOrWhiteSpace(published);
        return running.Equals(published, StringComparison.Ordinal);
    }
}
