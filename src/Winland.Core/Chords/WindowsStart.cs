namespace Winland.Core.Chords;

/// <summary>
/// The Windows Start menu. Winoma replaces it, so it is closed whenever it takes the foreground.
/// </summary>
public static class WindowsStart
{
    /// <summary>The process that hosts the Start menu on Windows 11.</summary>
    public const string HostProcess = "StartMenuExperienceHost";

    /// <summary>True when a window of this process in the foreground is the Start menu.</summary>
    /// <param name="processName">Process name without the extension.</param>
    /// <returns>True for the Start menu host.</returns>
    public static bool IsStart(string? processName) =>
        string.Equals(processName, HostProcess, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a window of this process in the foreground is the Start menu. Reads the name where it lies, without making a string.</summary>
    /// <param name="processName">Process name without the extension.</param>
    /// <returns>True for the Start menu host.</returns>
    public static bool IsStart(ReadOnlySpan<char> processName) =>
        processName.Equals(HostProcess, StringComparison.OrdinalIgnoreCase);
}
