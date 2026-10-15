using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

internal static class Taskbar
{
    private const uint QueryStateMessage = 0x00000004;
    private const uint ChangeStateMessage = 0x0000000A;
    private const int AutoHide = 0x1;
    private const int AlwaysOnTop = 0x2;

    private static readonly Lock Gate = new();
    private static int? _savedState;

    internal static bool SessionHidden { get; private set; }

    public static void Hide()
    {
        lock (Gate)
        {
            // A previous process may have exited without restoring the bars. Put them
            // back before this run hides them, so a crash cannot leave them gone.
            if (MarkerPresent())
                Show(TryReadMarker(out var crashed) ? crashed : AlwaysOnTop);

            var state = ReadState();
            _savedState = state;
            WriteMarker(state);

            // SW_HIDE takes the taskbar windows off the screen, but the shell still
            // reserves their strip in the work area. ABM_SETSTATE with ABS_AUTOHIDE
            // is the shell's own switch for that reservation: the bar gives the strip
            // back and would normally slide out when the pointer hits the edge, which
            // SW_HIDE prevents. The previous state is kept in the marker file so a
            // later start can put it back if this process does not.
            SetState(AutoHide);
            SessionHidden = true;
            ConcealCore();
        }
    }

    // The shell can mark the bars visible again while it applies the autohide
    // state. Hide them whenever that happens until the session ends.
    internal static void Conceal()
    {
        lock (Gate)
            ConcealCore();
    }

    public static void Restore()
    {
        lock (Gate)
        {
            SessionHidden = false;
            var state = _savedState ?? (TryReadMarker(out var saved) ? saved : AlwaysOnTop);
            Show(state);
            _savedState = null;
            TryDeleteMarker();
        }
    }

    private static void ConcealCore()
    {
        if (!SessionHidden)
            return;

        foreach (var window in Windows())
            _ = PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_HIDE);
    }

    private static void Show(int state)
    {
        foreach (var window in Windows())
            _ = PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_SHOW);
        SetState(state);
    }

    private static int ReadState()
    {
        var data = NewData(Primary());
        var state = Message(QueryStateMessage, ref data);
        return state < 0 ? AlwaysOnTop : (int)state;
    }

    private static void SetState(int state)
    {
        var data = NewData(Primary());
        data.lParam = new LPARAM(state);
        _ = Message(ChangeStateMessage, ref data);
    }

    private static unsafe nuint Message(uint message, ref APPBARDATA data)
    {
        fixed (APPBARDATA* pointer = &data)
            return PInvoke.SHAppBarMessage(message, pointer);
    }

    private static APPBARDATA NewData(HWND window) => new()
    {
        cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
        hWnd = window,
    };

    private static List<HWND> Windows()
    {
        var windows = new List<HWND>();
        var primary = Primary();
        if (!primary.IsNull)
            windows.Add(primary);

        var after = HWND.Null;
        while (true)
        {
            var next = Find("Shell_SecondaryTrayWnd", after, child: true);
            if (next.IsNull)
                break;

            windows.Add(next);
            after = next;
        }

        return windows;
    }

    private static HWND Primary() => Find("Shell_TrayWnd", HWND.Null, child: false);

    private static unsafe HWND Find(string className, HWND after, bool child)
    {
        fixed (char* name = className)
        {
            return child
                ? PInvoke.FindWindowEx(HWND.Null, after, name, null)
                : PInvoke.FindWindow(name, null);
        }
    }

    private static bool MarkerPresent()
    {
        try
        {
            return File.Exists(MarkerPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string MarkerPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winland", "taskbar.marker");

    private static void WriteMarker(int state)
    {
        var path = MarkerPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, state.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool TryReadMarker(out int state)
    {
        state = AlwaysOnTop;
        try
        {
            var text = File.ReadAllText(MarkerPath).Trim();
            return int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out state);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static void TryDeleteMarker()
    {
        try
        {
            File.Delete(MarkerPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
