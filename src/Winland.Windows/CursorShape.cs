using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

/// <summary>
/// Shows the pointer shape the core names while a drag holds a window or the pointer is on a border that resizes,
/// as the pinned release overrides its cursor. Windows lets a program set the pointer only over its own windows, so
/// every system cursor is replaced by that shape for the time, and the user's own scheme is loaded again after it,
/// at exit, and on the next start after a crash.
/// </summary>
internal static unsafe class CursorShape
{
    // The system cursors a program can show, every one of which takes the shape, so a window under the pointer that sets
    // its own, such as a text field's I-beam, shows it too.
    private static readonly SYSTEM_CURSOR_ID[] Replaced =
    [
        SYSTEM_CURSOR_ID.OCR_NORMAL,
        SYSTEM_CURSOR_ID.OCR_IBEAM,
        SYSTEM_CURSOR_ID.OCR_WAIT,
        SYSTEM_CURSOR_ID.OCR_CROSS,
        SYSTEM_CURSOR_ID.OCR_UP,
        SYSTEM_CURSOR_ID.OCR_SIZENWSE,
        SYSTEM_CURSOR_ID.OCR_SIZENESW,
        SYSTEM_CURSOR_ID.OCR_SIZEWE,
        SYSTEM_CURSOR_ID.OCR_SIZENS,
        SYSTEM_CURSOR_ID.OCR_SIZEALL,
        SYSTEM_CURSOR_ID.OCR_NO,
        SYSTEM_CURSOR_ID.OCR_HAND,
        SYSTEM_CURSOR_ID.OCR_APPSTARTING,
        (SYSTEM_CURSOR_ID)32651,
    ];

    private static readonly Lock Gate = new();
    private static string? _shown;

    /// <summary>The standard Windows cursor for a cursor name the core reports, or null for one it has none of.</summary>
    internal static SYSTEM_CURSOR_ID? For(string? name) => name switch
    {
        "grabbing" => SYSTEM_CURSOR_ID.OCR_SIZEALL,
        "top_side" or "bottom_side" or "n-resize" or "s-resize" => SYSTEM_CURSOR_ID.OCR_SIZENS,
        "left_side" or "right_side" or "w-resize" or "e-resize" => SYSTEM_CURSOR_ID.OCR_SIZEWE,
        "top_left_corner" or "bottom_right_corner" or "nw-resize" or "se-resize" => SYSTEM_CURSOR_ID.OCR_SIZENWSE,
        "top_right_corner" or "bottom_left_corner" or "ne-resize" or "sw-resize" => SYSTEM_CURSOR_ID.OCR_SIZENESW,
        _ => null,
    };

    /// <summary>Shows <paramref name="name"/>'s shape, or the user's own cursors again for null.</summary>
    // Stryker disable once all : replaces the system cursors.
    internal static void Show(string? name)
    {
        var shape = For(name);
        lock (Gate)
        {
            var wanted = shape is null ? null : name;
            if (wanted == _shown)
                return;

            if (shape is not SYSTEM_CURSOR_ID id)
            {
                Restore();
                _shown = null;
                return;
            }

            var cursor = PInvoke.LoadCursor(default, new PCWSTR((char*)(nint)(int)id));
            foreach (var replaced in Replaced)
            {
                // SetSystemCursor takes the handle it is given and destroys it, so each cursor gets a copy.
                var copy = PInvoke.CopyIcon(new HICON(cursor.Value));
                if (!copy.IsNull)
                    _ = PInvoke.SetSystemCursor(new HCURSOR(copy.Value), replaced);
            }

            _shown = name;
        }
    }

    /// <summary>Loads the user's own cursors again, when a shape is shown, at exit.</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            if (_shown is null)
                return;

            Restore();
            _shown = null;
        }
    }

    /// <summary>Loads the user's own cursors again unconditionally, after a run that may have left a shape behind.</summary>
    // Stryker disable once all : reloads the system cursors.
    internal static void Restore() =>
        _ = PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETCURSORS, 0, null, 0);
}
