namespace Winland.Core;

/// <summary>
/// The desktop-wide folder both processes share: <c>%USERPROFILE%\.config\winoma</c>.
/// <c>two-monitors</c> is the development switch, read once at startup.
/// Each product writes its own <c>lifetime</c>, in its own config folder, so stopping the shell does not read as Winland stopping.
/// Winoma's own files live here too, until the rename (ADR 0176 step 4).
/// </summary>
public static class DesktopFolder
{
    /// <summary>The folder. Resolved once. Asking Windows for the profile folder is a shell call.</summary>
    public static string Path { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "winoma");
}
