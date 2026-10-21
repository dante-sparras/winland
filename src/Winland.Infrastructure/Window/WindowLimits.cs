using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

/// <summary>
/// Reads a window's minimum and maximum track sizes.
/// A timeout or a failed send is no limit. The sizes are the visible frame, after the invisible border.
/// </summary>
internal static class WindowLimits
{
    private const uint TrackTimeoutMs = 200;

    internal static bool TryRead(nint hwnd, FrameInsets insets, out Size? min, out Size? max) =>
        TryRead(new HWND(hwnd), insets, out min, out max);

    internal static bool TryRead(HWND hwnd, FrameInsets insets, out Size? min, out Size? max)
    {
        min = null;
        max = null;
        if (!TryOuter(hwnd, out var outerMin, out var outerMax))
            return false;

        (min, max) = TrackLimits.Visible(outerMin, outerMax, insets);
        return true;
    }

    private static unsafe bool TryOuter(HWND hwnd, out Size? min, out Size? max)
    {
        min = null;
        max = null;
        var info = new MinMaxInfo();
        nuint result = 0;
        // The window procedure returns 0 for this message, which is also the failure
        // return. A cleared last-error distinguishes a timeout from that 0.
        Marshal.SetLastPInvokeError(0);
        var sent = PInvoke.SendMessageTimeout(
            hwnd,
            PInvoke.WM_GETMINMAXINFO,
            default,
            new LPARAM((nint)(&info)),
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG | SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_BLOCK | SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ERRORONEXIT,
            TrackTimeoutMs,
            &result);
        if (sent == 0 && Marshal.GetLastPInvokeError() != 0)
            return false;

        min = new Size(info.MinTrackX, info.MinTrackY);
        max = new Size(info.MaxTrackX, info.MaxTrackY);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public int ReservedX;
        public int ReservedY;
        public int MaxSizeX;
        public int MaxSizeY;
        public int MaxPositionX;
        public int MaxPositionY;
        public int MinTrackX;
        public int MinTrackY;
        public int MaxTrackX;
        public int MaxTrackY;
    }
}
