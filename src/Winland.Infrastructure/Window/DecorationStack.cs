using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

/// <summary>
/// Where Winland's own decorations of a window go in the z-order: just above the window, the border pieces and the
/// tab bar together. Each host leaves the other's windows where they are, so the two never take turns restacking.
/// </summary>
internal static class DecorationStack
{
    internal const string BorderClass = "WinlandBorder";
    internal const string GroupBarClass = "WinlandGroupBar";

    /// <summary>
    /// The decorations stacked just above <paramref name="target"/>, and the first other window above them,
    /// which is null when they are at the top.
    /// </summary>
    internal static HWND Above(HWND target, List<HWND> chain)
    {
        var above = PInvoke.GetWindow(target, GET_WINDOW_CMD.GW_HWNDPREV);
        while (!above.IsNull && IsDecoration(above))
        {
            chain.Add(above);
            above = PInvoke.GetWindow(above, GET_WINDOW_CMD.GW_HWNDPREV);
        }

        return above;
    }

    private static bool IsDecoration(HWND hwnd) => WindowAdapter.ClassName(hwnd) is BorderClass or GroupBarClass;
}
