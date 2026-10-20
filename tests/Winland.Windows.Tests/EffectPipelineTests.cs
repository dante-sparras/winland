using System.Diagnostics;
using System.Runtime.InteropServices;
using Winland;
using Winland.Windows;
using Winland.Windows.Tests;

namespace Winland.Tests;

/// <summary>
/// The wobble and motion blur pipeline against a real window: Windows.Graphics.Capture of a veiled window, the Direct3D passes, and
/// the pixels they read back. A machine with no hardware adapter or no capture support skips these by returning early.
/// </summary>
/// <summary>
/// A desktop test that needs the machine to capture a window and draw it. It skips, with the reason, where it cannot: a runner with no
/// desktop session, no hardware adapter, or no capture frames, as a headless CI runner has. The product falls back to thumbnails there.
/// </summary>
public sealed class CaptureFactAttribute : DesktopFactAttribute
{
    private static readonly Lazy<string?> Unavailable = new(EffectPipelineTests.WhyCaptureIsUnavailable);

    public CaptureFactAttribute()
    {
        if (Skip is null)
            Skip = Unavailable.Value;
    }
}

[Collection(DesktopCollection.Name)]
[Trait("Category", DesktopCollection.Name)]
public class EffectPipelineTests
{
    /// <summary>Why a window cannot be captured and drawn here, or null when it can. A real window is captured to find out.</summary>
    internal static string? WhyCaptureIsUnavailable()
    {
        try
        {
            if (!global::Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported())
                return "Windows.Graphics.Capture is not supported on this machine.";

            var gpu = Gpu.Acquire();
            if (gpu is null)
                return "There is no hardware graphics adapter to draw the effects with.";

            using var window = new PaintedWindow(Width, Height);
            WindowCapture? capture;
            lock (gpu.Gate)
                capture = WindowCapture.TryStart(gpu, window.Handle);

            if (capture is null)
                return "Windows would not start capturing a window.";

            using (capture)
            {
                var deadline = Stopwatch.GetTimestamp() + 3 * Stopwatch.Frequency;
                while (Stopwatch.GetTimestamp() < deadline)
                {
                    lock (gpu.Gate)
                    {
                        if (capture.Refresh() && capture.Picture is not null)
                            return null;
                    }

                    Thread.Sleep(10);
                }
            }

            return "Windows gave no captured frame of a window within three seconds, as on a headless runner.";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return $"Capture could not be tried: {exception.Message}";
        }
    }

    private const int Width = 300;
    private const int Height = 200;

    // BGRA of the four quarters of the window: red, green, blue and white, top left, top right, bottom left and bottom right.
    private static readonly byte[] Red = [0, 0, 255, 255];
    private static readonly byte[] Green = [0, 255, 0, 255];
    private static readonly byte[] Blue = [255, 0, 0, 255];
    private static readonly byte[] White = [255, 255, 255, 255];

    [CaptureFact]
    public void AVeiledWindowIsCapturedAndDrawnWithItsOwnPixels()
    {
        using var gpuScope = Scope();
        if (gpuScope.Gpu is not { } gpu)
            return;

        using var window = new PaintedWindow(Width, Height);
        window.Veil();
        window.Resize(Width + 40, Height + 20);

        using var capture = Captured(gpu, window.Handle, Width + 40, Height + 20);
        Assert.NotNull(capture);

        var frame = Render(gpu, capture!, new MotionEffects(null, 0), steps: [new Rectangle(500, 400, Width + 40, Height + 20)]);

        Assert.True(frame.Drawn, "The frame was not drawn.");
        Assert.Equal(new Rectangle(500, 400, Width + 40, Height + 20), frame.Canvas);
        Assert.Equal(Red, frame.Pixel((Width + 40) / 4, (Height + 20) / 4));
        Assert.Equal(Green, frame.Pixel((Width + 40) * 3 / 4, (Height + 20) / 4));
        Assert.Equal(Blue, frame.Pixel((Width + 40) / 4, (Height + 20) * 3 / 4));
        Assert.Equal(White, frame.Pixel((Width + 40) * 3 / 4, (Height + 20) * 3 / 4));
    }

    [CaptureFact]
    public void AWobblingWindowIsDrawnLargerThanItsRestAndKeepsItsPixels()
    {
        using var gpuScope = Scope();
        if (gpuScope.Gpu is not { } gpu)
            return;

        using var window = new PaintedWindow(Width, Height);
        using var capture = Captured(gpu, window.Handle, Width, Height);
        Assert.NotNull(capture);

        var effects = new MotionEffects(WobbleSettings.Documented, 0);
        var frame = Render(gpu, capture!, effects, steps: [new Rectangle(500, 400, Width, Height), new Rectangle(540, 400, Width, Height)]);

        Assert.True(frame.Drawn);
        Assert.True(frame.Canvas.X < 540);
        Assert.True(frame.Canvas.Width > Width);
        Assert.Equal(Red, frame.Pixel(540 - frame.Canvas.X + Width / 4, 400 - frame.Canvas.Y + Height / 4));
        Assert.Equal(White, frame.Pixel(540 - frame.Canvas.X + Width * 3 / 4, 400 - frame.Canvas.Y + Height * 3 / 4));
    }

    [CaptureFact]
    public void MotionBlurSmearsAMovingWindowAndLeavesTheRestOpaque()
    {
        using var gpuScope = Scope();
        if (gpuScope.Gpu is not { } gpu)
            return;

        using var window = new PaintedWindow(Width, Height);
        using var capture = Captured(gpu, window.Handle, Width, Height);
        Assert.NotNull(capture);

        var effects = new MotionEffects(null, 8);
        var frame = Render(gpu, capture!, effects, steps: [new Rectangle(500, 400, Width, Height), new Rectangle(580, 400, Width, Height)]);

        Assert.True(frame.Drawn);
        Assert.Equal(new Rectangle(500, 400, Width + 80, Height), frame.Canvas);

        // The trailing edge is a ramp of partly transparent pixels, and the part every sample covers is solid.
        var trailing = frame.Pixel(20, Height / 4);
        Assert.InRange(trailing[3], 1, 254);
        Assert.Equal(Red, frame.Pixel(100, Height / 4));
    }

    [CaptureFact]
    public void ALayerShowsAFrameOnTheScreen()
    {
        using var gpuScope = Scope();
        if (gpuScope.Gpu is null)
            return;

        using var window = new PaintedWindow(Width, Height);
        window.Veil();
        var stand = EffectStand.TryStart(window.Handle, new MotionEffects(WobbleSettings.Documented, 4));
        Assert.NotNull(stand);
        using (stand)
        {
            var bounds = new Rectangle(600, 300, Width, Height);
            var mapping = StandInMap.For(bounds, new Size(Width, Height), FrameInsets.None, stand!.Painted);
            Assert.True(stand.Show(bounds, mapping, 255, default));

            // The layer's own pixels, not the screen's: another window, such as the one a test before this one left in front, can cover
            // the layer, and the screen shows that instead.
            Assert.Equal(0x000000FFu, LayerPixel(stand.Layer, Width / 4, Height / 4));
            var landed = new Rectangle(620, 300, Width, Height);
            Assert.True(stand.Show(landed, mapping, 255, default));
            Assert.True(stand.Settle(landed, mapping, default));
            Assert.True(stand.Wobbling);

            var steps = 0;
            while (stand.Spring(landed, mapping, default) && steps++ < 2000)
                Thread.Sleep(2);

            Assert.False(stand.Wobbling);
        }
    }

    [CaptureFact]
    public void AMotionWithEffectsIsStoodInForAndTheWobbleLingersUntilItIsAtRest()
    {
        using var gpuScope = Scope();
        if (gpuScope.Gpu is null)
            return;

        using var window = new PaintedWindow(Width, Height);
        var id = new WindowId(window.Handle);
        MotionFx.Set(id, new MotionEffects(WobbleSettings.Documented, 5));
        try
        {
            var adapter = typeof(WindowAdapter);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var standFor = adapter.GetMethod("StandFor", flags)!;
            var settle = adapter.GetMethod("SettleStandIn", flags)!;
            var table = (System.Collections.IDictionary)adapter.GetField("StandIns", flags)!.GetValue(null)!;
            var rest = new Rectangle(300, 300, Width, Height);
            var shown = false;
            for (var step = 0; step <= 10; step++)
            {
                var bounds = rest with { X = 100 + step * 20 };
                shown = (bool)standFor.Invoke(null, [id, bounds, rest, null])!;
                Assert.True(shown);
                Thread.Sleep(16);
            }

            Assert.Single(table);
            settle.Invoke(null, [id, rest]);
            Assert.Single(table);

            var deadline = Stopwatch.GetTimestamp() + 10 * Stopwatch.Frequency;
            while (table.Count > 0 && Stopwatch.GetTimestamp() < deadline)
                Thread.Sleep(20);

            Assert.Empty(table);
            Assert.Equal(0u, (uint)GetWindowLongPtr(window.Handle, -20) & 0x00080000u);
        }
        finally
        {
            MotionFx.Forget(id);
            typeof(WindowAdapter).GetMethod("EndStandIns", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null);
        }
    }

    private static uint LayerPixel(nint layer, int x, int y)
    {
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, Width, Height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            // PW_RENDERFULLCONTENT.
            Assert.True(PrintWindow(layer, memory, 2));
            return GetPixel(memory, x, y);
        }
        finally
        {
            _ = SelectObject(memory, previous);
            _ = DeleteObject(bitmap);
            _ = DeleteDC(memory);
            _ = ReleaseDC(0, screen);
        }
    }

    private static GpuScope Scope() => new(Gpu.Acquire());

    private static WindowCapture? Captured(GpuDevice gpu, nint window, int width, int height)
    {
        WindowCapture? capture;
        lock (gpu.Gate)
            capture = WindowCapture.TryStart(gpu, window);

        if (capture is null)
            return null;

        var deadline = Stopwatch.GetTimestamp() + 3 * Stopwatch.Frequency;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            lock (gpu.Gate)
            {
                if (capture.Refresh() && capture.Size.Width >= width && capture.Size.Height >= height)
                    return capture;
            }

            Thread.Sleep(10);
        }

        capture.Dispose();
        return null;
    }

    private static unsafe Frame Render(GpuDevice gpu, WindowCapture capture, MotionEffects effects, Rectangle[] steps)
    {
        using var renderer = new EffectRenderer(gpu);
        var track = new EffectTrack(effects, 0);
        var now = 0.0;
        Frame frame = default;
        foreach (var step in steps)
        {
            now += 16;
            track.Step(EffectBox.Of(step), now);
            _ = track.Tick(now);
            var last = step;
            byte[]? pixels = null;
            Rectangle canvas = default;
            bool drawn;
            lock (gpu.Gate)
            {
                var picture = capture.Picture!;
                drawn = renderer.Render(
                    picture,
                    new Rectangle(0, 0, picture.Width, picture.Height),
                    track,
                    EffectBox.Of(last),
                    now,
                    (rows, pitch, area) =>
                    {
                        canvas = area;
                        pixels = new byte[area.Width * area.Height * 4];
                        for (var y = 0; y < area.Height; y++)
                            Marshal.Copy((nint)(rows + (long)y * pitch), pixels, y * area.Width * 4, area.Width * 4);
                        return true;
                    });
            }

            frame = new Frame(drawn, canvas, pixels);
        }

        return frame;
    }

    private readonly record struct Frame(bool Drawn, Rectangle Canvas, byte[]? Pixels)
    {
        public byte[] Pixel(int x, int y)
        {
            var at = (y * Canvas.Width + x) * 4;
            return Pixels![at..(at + 4)];
        }
    }

    private sealed class GpuScope(GpuDevice? gpu) : IDisposable
    {
        public GpuDevice? Gpu { get; } = gpu;

        public void Dispose()
        {
        }
    }

    private sealed class PaintedWindow : IDisposable
    {
        private const string ClassName = "WinlandEffectProbe";
        private const uint Popup = 0x80000000;
        private const uint ToolWindow = 0x00000080;
        private const uint NoActivate = 0x08000000;
        private const uint Layered = 0x00080000;
        private const int ExStyleIndex = -20;
        private static readonly Lock Gate = new();
        private static readonly WndProc Proc = WindowProc;
        private static bool _registered;

        private readonly Thread _thread;
        private uint _threadId;

        public PaintedWindow(int width, int height)
        {
            using var ready = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                Register();
                Handle = CreateWindowEx(ToolWindow | NoActivate, ClassName, "", Popup, 200, 200, width, height, 0, 0, GetModuleHandle(null), 0);
                _threadId = GetCurrentThreadId();
                _ = ShowWindow(Handle, 4);
                ready.Set();
                while (GetMessage(out var message, 0, 0, 0) > 0)
                {
                    _ = TranslateMessage(ref message);
                    _ = DispatchMessage(ref message);
                }
            })
            {
                IsBackground = true,
            };
            _thread.Start();
            Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotEqual(0, Handle);
            Thread.Sleep(100);
        }

        public nint Handle { get; private set; }

        public void Veil()
        {
            _ = SetWindowLongPtr(Handle, ExStyleIndex, GetWindowLongPtr(Handle, ExStyleIndex) | (nint)Layered);
            Assert.True(SetLayeredWindowAttributes(Handle, 0, 0, 2));
        }

        public void Resize(int width, int height)
        {
            _ = SetWindowPos(Handle, 0, 200, 200, width, height, 0x0004 | 0x0010);
            Thread.Sleep(150);
        }

        public void Dispose()
        {
            if (_threadId != 0)
                _ = PostThreadMessage(_threadId, 0x0012, 0, 0);

            _ = _thread.Join(TimeSpan.FromSeconds(2));
            if (Handle != 0)
                _ = DestroyWindow(Handle);
        }

        private static void Register()
        {
            lock (Gate)
            {
                if (_registered)
                    return;

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

        private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
        {
            const uint Paint = 0x000F;
            const uint EraseBackground = 0x0014;
            if (message == EraseBackground)
                return 1;

            if (message != Paint)
                return DefWindowProc(hwnd, message, wParam, lParam);

            var info = new byte[72];
            var dc = BeginPaint(hwnd, info);
            _ = GetClientRect(hwnd, out var client);
            var halfWidth = client.Right / 2;
            var halfHeight = client.Bottom / 2;
            Fill(dc, new Rect { Left = 0, Top = 0, Right = halfWidth, Bottom = halfHeight }, 0x000000FF);
            Fill(dc, new Rect { Left = halfWidth, Top = 0, Right = client.Right, Bottom = halfHeight }, 0x0000FF00);
            Fill(dc, new Rect { Left = 0, Top = halfHeight, Right = halfWidth, Bottom = client.Bottom }, 0x00FF0000);
            Fill(dc, new Rect { Left = halfWidth, Top = halfHeight, Right = client.Right, Bottom = client.Bottom }, 0x00FFFFFF);
            _ = EndPaint(hwnd, info);
            return 0;
        }

        private static void Fill(nint dc, Rect area, uint color)
        {
            var brush = CreateSolidBrush(color);
            _ = FillRect(dc, ref area, brush);
            _ = DeleteObject(brush);
        }
    }

    private delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public int cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClass lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg message, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint thread, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint hwnd, byte[] paint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(nint hwnd, byte[] paint);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint dc, ref Rect area, nint brush);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hwnd, out Rect area);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(nint dc, int x, int y);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint handle);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint dc);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint hwnd, nint dc, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);
}
