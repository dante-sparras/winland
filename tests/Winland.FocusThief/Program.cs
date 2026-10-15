using System.Runtime.InteropServices;

// A window that takes the foreground itself, so a parent can see that change refused.

var ready = new ManualResetEventSlim(false);
uint threadId = 0;
Exception? error = null;
var thread = new Thread(() =>
{
    try
    {
        threadId = Native.GetCurrentThreadId();
        Native.Register();
        Native.Hwnd = Native.Create();
        ready.Set();
        while (Native.GetMessage(out var message, 0, 0, 0) > 0)
        {
            if (message.message == Native.StealMessage)
            {
                _ = Native.ShowWindow(Native.Hwnd, 5);
                _ = Native.SetForegroundWindow(Native.Hwnd);
                continue;
            }

            if (message.message == Native.CloseMessage)
            {
                _ = Native.DestroyWindow(Native.Hwnd);
                Native.PostQuitMessage(0);
                continue;
            }

            Native.TranslateMessage(ref message);
            Native.DispatchMessage(ref message);
        }
    }
    catch (Exception exception)
    {
        error = exception;
        ready.Set();
    }
})
{
    IsBackground = true,
    Name = "WinlandFocusThief",
};
thread.SetApartmentState(ApartmentState.STA);
thread.Start();

if (!ready.Wait(TimeSpan.FromSeconds(5)) || error is not null || Native.Hwnd == 0)
{
    Console.Error.WriteLine(error?.Message ?? "The window was not created.");
    return 1;
}

Console.WriteLine(Native.Hwnd.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));
Console.Out.Flush();
Console.ReadLine();
_ = Native.PostThreadMessage(threadId, Native.StealMessage, 0, 0);
if (!thread.Join(TimeSpan.FromSeconds(8)))
    _ = Native.PostThreadMessage(threadId, Native.CloseMessage, 0, 0);

thread.Join(TimeSpan.FromSeconds(2));
return 0;

internal static class Native
{
    internal const uint StealMessage = 0x8001;
    internal const uint CloseMessage = 0x8000;
    private const int ClassAlreadyExists = 1410;
    private const uint OverlappedWindow = 0x00CF0000;

    private static readonly WndProc Proc = WindowProc;

    internal static IntPtr Hwnd;

    internal static void Register()
    {
        var info = new WndClass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
            hInstance = GetModuleHandle(null),
            hbrBackground = 6,
            lpszClassName = "WinlandFocusThief",
        };
        if (RegisterClassW(ref info) != 0)
            return;

        if (Marshal.GetLastWin32Error() != ClassAlreadyExists)
            throw new InvalidOperationException($"The window class was not registered ({Marshal.GetLastWin32Error()}).");
    }

    internal static IntPtr Create()
    {
        var hwnd = CreateWindowExW(0, "WinlandFocusThief", "WinlandFocusThief", OverlappedWindow, 420, 80, 320, 180, 0, 0, GetModuleHandle(null), 0);
        if (hwnd == 0)
            throw new InvalidOperationException($"The window was not created ({Marshal.GetLastWin32Error()}).");

        return hwnd;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, nuint wParam, nint lParam) =>
        DefWindowProcW(hwnd, message, wParam, lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int x;
        public int y;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProc(IntPtr hwnd, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WndClass wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out Msg message, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessage(ref Msg message);

    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int code);

    [DllImport("user32.dll")]
    internal static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();
}
