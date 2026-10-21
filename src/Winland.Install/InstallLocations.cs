namespace Winland.Install;

/// <summary>The folders and registry names an install uses. The paths are built from the roots Windows gives the process.</summary>
public static class InstallLocations
{
    public const string FolderName = "Winland";

    /// <summary>The AppUserModelID carried by the Start menu shortcut.</summary>
    public const string AppUserModelId = "Winland.Winland";

    public const string ShortcutName = "Winland.lnk";

    public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Winland";

    public const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public const string RunValue = "Winland";

    public const string MachinePathKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    public const string UserPathKey = "Environment";

    public const string PathValue = "Path";

    /// <summary>The per-user folder under the profile that holds the config, the log, the state and the taskbar marker.</summary>
    public const string HomeFolder = ".winland";

    public static string Machine(string programFiles) => Path.Combine(programFiles, FolderName);

    public static string User(string localAppData) => Path.Combine(localAppData, "Programs", FolderName);

    /// <summary>The config directory <c>winland uninstall</c> keeps, and <c>--purge</c> removes. It is <c>%USERPROFILE%\.winland</c>.</summary>
    public static string Config(string userProfile) => Path.Combine(userProfile, HomeFolder);

    /// <summary>The state, log and taskbar marker directory. It is the same folder as <see cref="Config"/>.</summary>
    public static string State(string userProfile) => Config(userProfile);

    public static string Executable(string directory) => Path.Combine(directory, "winland.exe");

    public static string Shortcut(string programsDirectory) => Path.Combine(programsDirectory, ShortcutName);

    /// <summary>
    /// True when <paramref name="executable"/> is the per-user copy. A Winland started from there never
    /// relaunches itself through UAC, whatever argument started it.
    /// </summary>
    public static bool IsPerUserInstall(string? executable, string userDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDirectory);
        if (string.IsNullOrWhiteSpace(executable))
            return false;

        var folder = Path.GetDirectoryName(Path.GetFullPath(executable));
        var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(userDirectory));
        return folder is not null && folder.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
