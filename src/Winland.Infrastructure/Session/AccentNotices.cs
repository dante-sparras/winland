namespace Winland.Infrastructure;

/// <summary>Which session-window messages mean the Windows accent, High Contrast or text size may have changed.</summary>
internal static class AccentNotices
{
    internal const uint SettingChange = 0x001A;
    internal const uint SystemColorChange = 0x0015;
    internal const uint ColorizationChanged = 0x0320;

    /// <summary>
    /// True for <c>WM_DWMCOLORIZATIONCOLORCHANGED</c>, <c>WM_SYSCOLORCHANGE</c> and every <c>WM_SETTINGCHANGE</c>, whose
    /// string names the accent and High Contrast but not always the text size. Everything is read again, and a read
    /// that matches the last one changes nothing.
    /// </summary>
    internal static bool Changed(uint message) =>
        message is ColorizationChanged or SystemColorChange or SettingChange;
}
