namespace Winland.Core;

/// <summary>
/// Where the window manager reads the person's files: <c>%USERPROFILE%\.config\winland</c>.
/// <c>binds.json</c>, the tiling settings, <c>hidden</c>, <c>winland-restarts</c>, <c>winland.log</c>, and <c>lifetime</c> live here.
/// <c>two-monitors</c> stays in <see cref="DesktopFolder"/>. The shell keeps its own lifetime word and does not write this one, except to say the whole desktop is stopping or being replaced.
/// </summary>
public static class ConfigFolder
{
    /// <summary>The folder. Resolved once. Asking Windows for the profile folder is a shell call.</summary>
    public static string Path { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "winland");
}
