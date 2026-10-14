using System.Runtime.InteropServices;

namespace Winland;

/// <summary>The window manager's thread queue. Winland.Platform's copies are internal to that assembly.</summary>
internal static class WmPump
{
    internal const uint Work = 0x8001;
    internal const uint Timer = 0x0113;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    internal static extern bool PeekMessage(out Msg message, nint hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    internal static extern bool GetMessage(out Msg message, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessage(ref Msg message);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostThreadMessage(uint id, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetTimer(nint hwnd, nint id, uint milliseconds, nint procedure);
}
