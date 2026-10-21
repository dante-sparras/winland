using System.Runtime.InteropServices;
using Winland.Core;
using Winland.Windows;

namespace Winland.Windows.Tests;

[Collection(DesktopCollection.Name)]
[Trait("Category", DesktopCollection.Name)]
public class OpacityProbeTests
{
    private const uint Layered = 0x00080000;
    private const uint ToolWindow = 0x00000080;
    private const uint NoActivate = 0x08000000;
    private const uint Popup = 0x80000000;
    private const int ExStyleIndex = -20;

    [Fact]
    public void ProbeWindowTakesTheLayeredAlphaAndGivesTheStyleBack()
    {
        using var probe = Probe.Open();
        var original = ExStyle(probe.Handle);
        Assert.Equal(0u, original & Layered);

        var adapter = new OpacityAdapter(new Win32WindowStyles());
        var window = new WindowId(probe.Handle);
        adapter.Apply(window, 128);

        Assert.Equal(Layered, ExStyle(probe.Handle) & Layered);
        Assert.Equal(128, LayeredAlpha(probe.Handle));
        Assert.True(adapter.Owns(window));

        adapter.Reset(window);

        var restored = ExStyle(probe.Handle);
        Assert.Equal(0u, restored & Layered);
        Assert.Equal(original, restored);
        Assert.False(adapter.Owns(window));
    }

    [Fact]
    public void ProbeWindowThatWasAlreadyLayeredIsLeftAlone()
    {
        using var probe = Probe.Open();
        var withLayer = ExStyle(probe.Handle) | Layered;
        _ = SetWindowLongPtr(probe.Handle, ExStyleIndex, (nint)withLayer);
        Assert.True(SetLayeredWindowAttributes(probe.Handle, 0, 200, 2));

        var adapter = new OpacityAdapter(new Win32WindowStyles());
        adapter.Apply(new WindowId(probe.Handle), 100);

        Assert.Equal(200, LayeredAlpha(probe.Handle));
        Assert.Contains(adapter.Reports, report => report.Contains("left alone", StringComparison.Ordinal));
    }

    private static uint ExStyle(nint window) => (uint)GetWindowLongPtr(window, ExStyleIndex);

    private static byte LayeredAlpha(nint window)
    {
        Assert.True(GetLayeredWindowAttributes(window, out _, out var alpha, out var flags));
        Assert.Equal(2u, flags & 2u);
        return alpha;
    }

    private sealed class Probe : IDisposable
    {
        private const string ClassName = "WinlandOpacityProbe";

        private static readonly Lock Gate = new();
        private static readonly WndProc Proc = WindowProc;
        private static bool _registered;

        private Probe(nint handle) => Handle = handle;

        public nint Handle { get; }

        public static Probe Open()
        {
            lock (Gate)
            {
                if (!_registered)
                {
                    var info = new WndClass
                    {
                        cbSize = Marshal.SizeOf<WndClass>(),
                        lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
                        hInstance = GetModuleHandle(null),
                        lpszClassName = ClassName,
                    };
                    if (RegisterClassEx(ref info) == 0 && Marshal.GetLastWin32Error() != 1410)
                        throw new InvalidOperationException($"The probe class was not registered ({Marshal.GetLastWin32Error()}).");

                    _registered = true;
                }
            }

            var handle = CreateWindowEx(
                ToolWindow | NoActivate,
                ClassName,
                "WinlandOpacityProbe",
                Popup,
                -2000,
                -2000,
                120,
                80,
                IntPtr.Zero,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"The probe window was not created ({Marshal.GetLastWin32Error()}).");

            return new Probe(handle);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
                _ = DestroyWindow(Handle);
        }

        private static IntPtr WindowProc(IntPtr hwnd, uint message, nint wParam, nint lParam) =>
            DefWindowProc(hwnd, message, wParam, lParam);
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClass lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLayeredWindowAttributes(nint hwnd, out uint colorKey, out byte alpha, out uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);
}
