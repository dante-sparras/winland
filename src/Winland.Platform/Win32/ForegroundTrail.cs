namespace Winland.Platform.Win32;

/// <summary>
/// The Host's one <see cref="KeyboardTrail"/>, which decides whether the keyboard fell to a window or was taken by it.
/// The window watcher notes each move of the keyboard as Windows reports it, and the gateway reads for the shell.
/// This holds only what Windows is asked: whether a window is on the glass.
/// </summary>
internal static class ForegroundTrail
{
    private static readonly KeyboardTrail Trail = new(OnGlass);

    /// <summary>The keyboard moved to <paramref name="hwnd"/>.</summary>
    /// <param name="hwnd">The window Windows reports in front.</param>
    internal static void Note(nint hwnd) => Trail.Note(hwnd);

    /// <summary>The window the keyboard left for <paramref name="current"/>, when that window was already off the glass as the keyboard moved.</summary>
    /// <param name="current">The window that has the keyboard now.</param>
    /// <returns>Null when the window before was still on the glass, when nothing had the keyboard before, and when the move was not seen.</returns>
    internal static long? Left(nint current) => Trail.Left(current);

    private static bool OnGlass(long id)
    {
        var hwnd = (nint)id;
        return Native.IsWindow(hwnd) && Native.IsWindowVisible(hwnd) && !Native.IsIconic(hwnd) && !Native.ShellCloaked(hwnd);
    }
}
