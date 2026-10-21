using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

public sealed partial class WindowAdapter
{
    private const int HitTestTimeoutMs = 100;

    // The system moves and sizes a window in a loop of its own, and reports only its start and its end.
    private WindowId? _moving;
    private WindowId? _sizing;

    // A drag whose window did not say whether it sizes or moves. The window's first change decides it.
    private UndecidedDrag? _undecided;

    private void BeginMove(HWND hwnd, WindowId window)
    {
        if (_moving is not null || _sizing is not null || !PInvoke.GetCursorPos(out var cursor))
            return;

        var pointer = new Point(cursor.X, cursor.Y);
        var kind = GrabbedKind(hwnd, cursor);
        if (kind != DragKind.Unclear)
        {
            _ = StartDrag(window, kind == DragKind.Size, pointer);
            return;
        }

        // A window that snaps its size to a grid, as a terminal does, draws its edge out from under the pointer
        // as the sizing begins, so the hit test misses the border it was grabbed by.
        if (TryOuter(hwnd, out var outer))
            _undecided = new UndecidedDrag(window, outer, pointer);
        else
            _ = StartDrag(window, sizing: false, pointer);
    }

    // The window changed while the system's loop ran. The first change that tells a size from a move settles the drag.
    private bool Decide(HWND hwnd, WindowId window)
    {
        if (_undecided is not { } drag || drag.Window != window)
            return false;

        if (!TryOuter(hwnd, out var now))
            return true;

        var kind = SizingGrab.Resolve(drag.Outer, now);
        if (kind == DragKind.Unclear)
            return true;

        _undecided = null;
        _ = StartDrag(window, kind == DragKind.Size, drag.Pointer);
        if (kind == DragKind.Size)
            FollowSizing(window);

        return true;
    }

    private readonly record struct UndecidedDrag(WindowId Window, Rectangle Outer, Point Pointer);

    /// <summary>Latches the one window being moved or sized. False while another drag holds the latch.</summary>
    internal bool StartDrag(WindowId window, bool sizing, Point pointer)
    {
        if (_moving is not null || _sizing is not null)
            return false;


        if (sizing)
        {
            _sizing = window;
            Apply(_core.Handle(new WindowSizeStarted(window, pointer)));
            return true;
        }

        _moving = window;
        Apply(_core.Handle(new WindowMoveStarted(window, pointer)));
        return true;
    }

    // The neighbours of a window the system is resizing follow its border while it is held.
    private void FollowSizing(WindowId window)
    {
        if (PInvoke.GetCursorPos(out var cursor))
        {
            var bounds = TryVisible(new HWND((nint)window.Value), out var visible) ? visible : (Rectangle?)null;
            Apply(_core.Handle(new WindowSizing(window, new Point(cursor.X, cursor.Y), bounds)));
        }
    }

    private void EndMove(WindowId window)
    {
        // A loop that ended with the window as it was: the border was clicked and let go.
        if (_undecided is { } drag && drag.Window == window)
            _undecided = null;

        var pointer = PInvoke.GetCursorPos(out var cursor) ? new Point(cursor.X, cursor.Y) : default;
        EndDrag(window, pointer, TryVisible(new HWND((nint)window.Value), out var dropped) ? dropped : null);
    }

    private void EndDrag(WindowId window, Point pointer, Rectangle? dropped)
    {
        if (_moving == window)
        {
            _moving = null;
            Apply(_core.Handle(new WindowMoveEnded(window, pointer, dropped)));
        }
        else if (_sizing == window)
        {
            _sizing = null;
            Apply(_core.Handle(new WindowSizeEnded(window, pointer, dropped)));
        }
    }

    // A window destroyed mid-drag never reports the end of its drag, so the latch is let go here.
    private void AbandonDrag(WindowId window)
    {
        if (_undecided is { } drag && drag.Window == window)
            _undecided = null;

        if (_moving != window && _sizing != window)
            return;

        var pointer = PInvoke.GetCursorPos(out var cursor) ? new Point(cursor.X, cursor.Y) : default;
        EndDrag(window, pointer, null);
    }

    // A drag that starts on a sizing border is a resize. Anything else, the title above all, is a move.
    private static unsafe DragKind GrabbedKind(HWND hwnd, System.Drawing.Point cursor)
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
        return SizingGrab.Classify(sent == 0 && Marshal.GetLastPInvokeError() != 0 ? null : (int)hit);
    }
}
