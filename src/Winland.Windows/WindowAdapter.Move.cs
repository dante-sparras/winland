using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

public sealed partial class WindowAdapter
{
    private const int HitTestTimeoutMs = 100;
    private const int HitSizeBox = 4;
    private const int HitLeft = 10;
    private const int HitBottomRight = 17;

    // The system moves and sizes a window in a loop of its own, and reports only its start and its end.
    private WindowId? _moving;

    private void BeginMove(HWND hwnd, WindowId window)
    {
        if (_moving is not null || !PInvoke.GetCursorPos(out var cursor) || !GrabsTheTitle(hwnd, cursor))
            return;

        _moving = window;
        Apply(_core.Handle(new WindowMoveStarted(window, new Point(cursor.X, cursor.Y))));
    }

    private void EndMove(WindowId window)
    {
        if (_moving != window)
            return;

        _moving = null;
        var pointer = PInvoke.GetCursorPos(out var cursor) ? new Point(cursor.X, cursor.Y) : default;
        Apply(_core.Handle(new WindowMoveEnded(window, pointer)));
    }

    // A drag that starts on a sizing border is a resize, which the core does not lift a window for.
    private static unsafe bool GrabsTheTitle(HWND hwnd, System.Drawing.Point cursor)
    {
        nuint hit = 0;
        var packed = (nint)(((cursor.Y & 0xFFFF) << 16) | (cursor.X & 0xFFFF));
        Marshal.SetLastPInvokeError(0);
        var sent = PInvoke.SendMessageTimeout(
            hwnd,
            PInvoke.WM_NCHITTEST,
            default,
            new LPARAM(packed),
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG | SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_BLOCK | SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ERRORONEXIT,
            HitTestTimeoutMs,
            &hit);
        if (sent == 0 && Marshal.GetLastPInvokeError() != 0)
            return true;

        var code = (int)hit;
        return code != HitSizeBox && !(code is >= HitLeft and <= HitBottomRight);
    }
}
