using System.Runtime.InteropServices;

namespace Winland.Platform;

internal static partial class Native
{
    internal const uint CfUnicodeText = 13;
    internal const uint GmemMoveable = 0x0002;
    internal const uint CfDib = 8;
    internal const uint CfHdrop = 15;
    internal const uint CfDibV5 = 17;
    internal const uint WmClipboardUpdate = 0x031D;

    [DllImport("user32.dll")]
    internal static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    internal static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll")]
    internal static extern nint GetClipboardData(uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterClipboardFormat(string name);

    [DllImport("kernel32.dll")]
    internal static extern nuint GlobalSize(nint hMem);

    [DllImport("kernel32.dll")]
    internal static extern nint GlobalFree(nint hMem);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool RemoveClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    internal static extern nint GetClipboardOwner();
}
