namespace Winland.Install;

public enum InstallKind
{
    None,
    Machine,
    User,
}

/// <summary>Which install folder the running executable is in, if either.</summary>
public static class InstalledCopy
{
    public static InstallKind Of(string executable, string machineDirectory, string userDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDirectory);

        var folder = Path.GetDirectoryName(Path.GetFullPath(executable));
        if (folder is null)
            return InstallKind.None;

        if (Same(folder, machineDirectory))
            return InstallKind.Machine;

        if (Same(folder, userDirectory))
            return InstallKind.User;

        return InstallKind.None;
    }

    private static bool Same(string left, string right) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left))
            .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
}
