using System.Runtime.InteropServices;

namespace Winland.Windows;

/// <summary>Which session-window messages mean the Windows accent may have changed.</summary>
internal static class AccentNotices
{
    internal const uint SettingChange = 0x001A;
    internal const uint ColorizationChanged = 0x0320;

    private const string ImmersiveColorSet = "ImmersiveColorSet";

    /// <summary>
    /// True for <c>WM_DWMCOLORIZATIONCOLORCHANGED</c>, and for <c>WM_SETTINGCHANGE</c> whose string is
    /// <c>ImmersiveColorSet</c>. The color is read again; the message's own color is not used.
    /// </summary>
    internal static bool Changed(uint message, nint lParam)
    {
        if (message == ColorizationChanged)
            return true;

        if (message != SettingChange || lParam == 0)
            return false;

        return string.Equals(Marshal.PtrToStringUni(lParam), ImmersiveColorSet, StringComparison.Ordinal);
    }
}
