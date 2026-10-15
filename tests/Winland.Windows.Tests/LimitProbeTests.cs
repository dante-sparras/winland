using System.Runtime.InteropServices;
using Winland.Windows;

namespace Winland.Tests;

public class LimitProbeTests
{
    [Fact]
    public void RealWindowsFloatTheSmallMaximumAndKeepTheLargeMinimum()
    {
        _ = ProbeNative.SetProcessDpiAwarenessContext(new IntPtr(-4));
        var taskbar = ProbeNative.TaskbarVisible();
        using var probe = new LimitProbe();
        probe.Open();
        try
        {
            var area = new Rectangle(0, 0, 1920, 1080);
            var anchorLimits = Read(probe.Anchor);
            var wideLimits = Read(probe.Wide);
            var smallLimits = Read(probe.Small);
            var wideInsets = ProbeNative.Insets(probe.Wide);
            Assert.True(wideInsets.Left + wideInsets.Right > 0, "The probe window had no invisible frame.");
            Assert.Equal(1700 - wideInsets.Left - wideInsets.Right, wideLimits.Min!.Value.Width);
            Assert.Equal(200 - wideInsets.Top - wideInsets.Bottom, wideLimits.Min.Value.Height);
            Assert.True(smallLimits.Max!.Value.Width < area.Width);
            Assert.True(smallLimits.Max.Value.Height < area.Height);

            var anchorId = probe.Anchor.ToInt64();
            var wideId = probe.Wide.ToInt64();
            var smallId = probe.Small.ToInt64();
            // The probe reads where each placement lands, so nothing animates on the way.
            var core = new Core { ReducedMotion = () => true };
            core.Handle(new WindowAppeared(new WindowId(anchorId), area)
            {
                Traits = WindowTraits.Normal with { MinSize = anchorLimits.Min, MaxSize = anchorLimits.Max },
            });
            var tiled = core.Handle(new WindowAppeared(new WindowId(wideId), area)
            {
                Traits = WindowTraits.Normal with { MinSize = wideLimits.Min, MaxSize = wideLimits.Max },
            });
            var wideBox = BoundsOf(tiled, wideId);
            var anchorBox = BoundsOf(tiled, anchorId);
            Assert.True(wideBox.Width >= wideLimits.Min.Value.Width);
            Assert.Equal(area.Width - wideBox.Width, anchorBox.Width);
            Assert.True(anchorBox.Width < wideBox.Width);
            WindowAdapter.Apply(tiled);
            AssertClose(ProbeNative.Visible(probe.Wide), wideBox, "wide");
            AssertClose(ProbeNative.Visible(probe.Anchor), anchorBox, "anchor");

            var bounds = new Rectangle(80, 90, 200, 150);
            var centered = new Rectangle(860, 465, 200, 150);
            var floated = core.Handle(new WindowAppeared(new WindowId(smallId), area)
            {
                Traits = WindowTraits.Normal with { MinSize = smallLimits.Min, MaxSize = smallLimits.Max },
                Bounds = bounds,
            });
            Assert.True(Assert.Single(floated.State.Clients, client => client.Window == new WindowId(smallId)).Floating);
            Assert.Equal(centered, BoundsOf(floated, smallId));
            WindowAdapter.Apply(floated);
            var smallVisible = ProbeNative.Visible(probe.Small);
            AssertClose(smallVisible, centered, "small");
            Assert.True(smallVisible.Width < area.Width / 2, $"The small window filled a tile: {smallVisible.Width}");

            probe.SetMinimum(probe.Wide, 1850, 200);
            var grown = Read(probe.Wide);
            Assert.True(grown.Min!.Value.Width > wideLimits.Min.Value.Width);
            var updated = core.Handle(new WindowLimitsChanged(new WindowId(wideId), grown.Min, grown.Max));
            var grownBox = BoundsOf(updated, wideId);
            Assert.True(grownBox.Width >= grown.Min.Value.Width);
            Assert.True(grownBox.Width > wideBox.Width);
            WindowAdapter.Apply(updated);
            AssertClose(ProbeNative.Visible(probe.Wide), grownBox, "grown");
            Assert.True(ProbeNative.Visible(probe.Anchor).Width < anchorBox.Width);
        }
        finally
        {
            probe.Close();
            Assert.Equal(taskbar, ProbeNative.TaskbarVisible());
        }
    }

    private static (Size? Min, Size? Max) Read(IntPtr hwnd)
    {
        Assert.True(WindowLimits.TryRead(hwnd, ProbeNative.Insets(hwnd), out var min, out var max), "The track size was not read.");
        return (min, max);
    }

    private static void AssertClose(Rectangle actual, Rectangle expected, string name)
    {
        Assert.True(Math.Abs(actual.X - expected.X) <= 12, $"{name} x {actual.X} expected {expected.X}");
        Assert.True(Math.Abs(actual.Y - expected.Y) <= 12, $"{name} y {actual.Y} expected {expected.Y}");
        Assert.True(Math.Abs(actual.Width - expected.Width) <= 12, $"{name} width {actual.Width} expected {expected.Width}");
        Assert.True(Math.Abs(actual.Height - expected.Height) <= 12, $"{name} height {actual.Height} expected {expected.Height}");
    }

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }

    private sealed class LimitProbe : IDisposable
    {
        private const uint CloseMessage = 0x8000;
        private const string ClassName = "WinlandLimitProbe";

        private static readonly ProbeNative.WndProc Proc = WindowProc;
        private static readonly Lock Gate = new();
        private static readonly Dictionary<nint, Track> Limits = [];
        private static readonly Queue<Track> Pending = new();

        private readonly ManualResetEventSlim _ready = new(false);
        private readonly Thread _thread;
        private uint _threadId;
        private Exception? _error;

        public LimitProbe()
        {
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "WinlandLimitProbe",
            };
            _thread.SetApartmentState(ApartmentState.STA);
        }

        public IntPtr Anchor { get; private set; }

        public IntPtr Wide { get; private set; }

        public IntPtr Small { get; private set; }

        public void Open()
        {
            _thread.Start();
            if (!_ready.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The probe windows did not open.");

            if (_error is not null)
                throw new InvalidOperationException("The probe windows did not open.", _error);
        }

        public void SetMinimum(IntPtr hwnd, int width, int height)
        {
            lock (Gate)
            {
                var track = Limits[hwnd];
                Limits[hwnd] = track with { MinW = width, MinH = height };
            }
        }

        public void Close()
        {
            if (_threadId != 0)
                ProbeNative.PostThreadMessage(_threadId, CloseMessage, 0, 0);

            if (_thread.IsAlive)
                _thread.Join(TimeSpan.FromSeconds(5));

            _ready.Dispose();
        }

        public void Dispose() => Close();

        private void Run()
        {
            try
            {
                _threadId = ProbeNative.GetCurrentThreadId();
                ProbeNative.Register(ClassName, Proc);
                Anchor = Create(new Track(100, 100, 8000, 8000));
                Wide = Create(new Track(1700, 200, 8000, 8000));
                Small = Create(new Track(100, 100, 400, 300));
                _ready.Set();
                Pump();
            }
            catch (Exception exception)
            {
                _error = exception;
                _ready.Set();
            }
        }

        private void Pump()
        {
            while (ProbeNative.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.message == CloseMessage)
                {
                    ProbeNative.DestroyWindow(Anchor);
                    ProbeNative.DestroyWindow(Wide);
                    ProbeNative.DestroyWindow(Small);
                    ProbeNative.PostQuitMessage(0);
                    continue;
                }

                ProbeNative.TranslateMessage(ref message);
                ProbeNative.DispatchMessage(ref message);
            }
        }

        private static IntPtr Create(Track track)
        {
            lock (Gate)
                Pending.Enqueue(track);

            var hwnd = ProbeNative.CreateWindowEx(
                0x00040000,
                ClassName,
                "WinlandLimitProbe",
                0x00CF0000,
                120,
                120,
                320,
                240,
                IntPtr.Zero,
                IntPtr.Zero,
                ProbeNative.Module,
                IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException($"The probe window was not created ({Marshal.GetLastWin32Error()}).");

            _ = ProbeNative.ShowWindow(hwnd, 5);
            return hwnd;
        }

        private static IntPtr WindowProc(IntPtr hwnd, uint message, nint wParam, nint lParam)
        {
            if (message == 0x0024)
            {
                lock (Gate)
                {
                    if (!Limits.TryGetValue(hwnd, out var track) && Pending.Count > 0)
                    {
                        track = Pending.Dequeue();
                        Limits[hwnd] = track;
                    }

                    if (Limits.TryGetValue(hwnd, out track))
                        ProbeNative.WriteTrack(lParam, track.MinW, track.MinH, track.MaxW, track.MaxH);
                }

                return IntPtr.Zero;
            }

            return ProbeNative.DefWindowProc(hwnd, message, wParam, lParam);
        }

        private readonly record struct Track(int MinW, int MinH, int MaxW, int MaxH);
    }
}

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
    internal static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);

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
    private static extern bool IsWindowVisible(IntPtr hwnd);

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
