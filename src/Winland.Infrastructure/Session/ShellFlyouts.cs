
namespace Winland.Infrastructure;

/// <summary>
/// The Start menu, Search and the other flyouts the shell opens from the taskbar. They are windows of the
/// shell, not programs, so tiling them would split the screen every time Start opens.
/// </summary>
internal static class ShellFlyouts
{
    private static readonly string[] Hosts =
    [
        "StartMenuExperienceHost.exe",
        "SearchHost.exe",
        "SearchApp.exe",
        "ShellExperienceHost.exe",
        "ShellHost.exe",
    ];

    internal static bool Is(string exe)
    {
        foreach (var host in Hosts)
        {
            if (host.Equals(exe, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
