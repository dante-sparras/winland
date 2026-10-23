namespace Winland.Infrastructure;

/// <summary>
/// The Start menu, Search and the other flyouts the shell opens from the taskbar, and the popups Explorer shows
/// over it, such as a taskbar button's preview and a notification icon's tip. They are windows of the shell, not
/// programs, so tiling them would split the screen every time Start opens or the pointer rests on the taskbar.
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

    internal static bool Is(string exe, string className)
    {
        if (className == "Xaml_WindowedPopupClass" && exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var host in Hosts)
        {
            if (host.Equals(exe, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
