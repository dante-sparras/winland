namespace Winland.Install;

/// <summary>Where the running <c>winland</c> and its <c>winlandctl</c> are copied from.</summary>
public static class InstallPayload
{
    public const string DaemonFile = "winland.exe";

    public const string ControlFile = "winlandctl.exe";

    public const string ControlDirectoryName = "winlandctl";

    /// <summary>
    /// The daemon directory, and the <c>winlandctl</c> directory when it is a sibling rather than the same folder.
    /// A release zip has <c>winland\</c> and <c>winlandctl\</c> beside each other. A folder that already holds both
    /// executables is copied as that one folder.
    /// </summary>
    public static (string DaemonDirectory, string? ControlDirectory) Locate(string executable, Func<string, bool> exists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(exists);

        var daemonDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))
            ?? throw new InvalidOperationException("The executable folder is not available.");

        if (exists(Path.Combine(daemonDirectory, ControlFile)))
            return (daemonDirectory, null);

        var parent = Path.GetDirectoryName(daemonDirectory);
        if (parent is not null)
        {
            var sibling = Path.Combine(parent, ControlDirectoryName);
            if (exists(Path.Combine(sibling, ControlFile)))
                return (daemonDirectory, sibling);
        }

        throw new InvalidOperationException(
            "winlandctl was not found next to winland, or in a winlandctl folder beside it.");
    }
}
