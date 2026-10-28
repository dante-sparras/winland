using System.Runtime.InteropServices;

namespace Winland.Infrastructure.Tests;

internal static class ProbeNative
{
    internal const int ExtendedFrame = 9;

    internal static readonly IntPtr Module = GetModuleHandle(null);

    private static readonly Dictionary<string, WndProc> Procs = [];

    internal delegate IntPtr WndProc(IntPtr hwnd, uint message, nint wParam, nint lParam);

    internal static void Register(string className, WndProc proc)
    {
        Procs[className] = proc;
        var info = new WndClass
        {
            cbSize = Marshal.SizeOf<WndClass>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            hInstance = Module,
            hbrBackground = new IntPtr(6),
            lpszClassName = className,
        };
        if (RegisterClassEx(ref info) != 0)
            return;

        if (Marshal.GetLastWin32Error() != 1410)
            throw new InvalidOperationException($"The probe window class was not registered ({Marshal.GetLastWin32Error()}).");
    }

    internal static FrameInsets Insets(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var outer))
            throw new InvalidOperationException("The outer frame was not read.");

        var visible = Visible(hwnd);
        return FrameInsets.Between(ToRect(outer), visible);
    }

    internal static Rectangle Visible(IntPtr hwnd)
    {
        var rect = default(Rect);
        var result = DwmGetWindowAttribute(hwnd, ExtendedFrame, ref rect, Marshal.SizeOf<Rect>());
        if (result != 0)
            throw new InvalidOperationException($"The visible frame was not read ({result}).");

        return ToRect(rect);
    }

    internal static unsafe void WriteTrack(nint lParam, int minW, int minH, int maxW, int maxH)
    {
        var info = (MinMaxInfo*)lParam;
        info->MinTrackX = minW;
        info->MinTrackY = minH;
        info->MaxTrackX = maxW;
        info->MaxTrackY = maxH;
    }

    internal static bool TaskbarVisible()
    {
        var tray = FindWindow("Shell_TrayWnd", null);
        return tray != IntPtr.Zero && IsWindowVisible(tray);
    }

    private static Rectangle ToRect(Rect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    [DllImport("user32.dll")]
    internal static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WndClass lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool PeekMessage(out Message message, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    internal static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int code);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? module);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, ref Rect rect, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? title);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WndClass
    {
        public int cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        public IntPtr hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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
