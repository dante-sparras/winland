using System.Runtime.InteropServices;

namespace Winland.Infrastructure.Tests;

/// <summary>
/// A window that takes a long time over each move, as a large application does when it lays itself out.
/// It stays hidden, so a running Winland does not adopt it.
/// </summary>
public sealed class SlowWindowTests : IDisposable
{
    private const int Delay = 60;

    private readonly SlowWindow _window = new();
    private readonly WindowAdapter _adapter = new(new WindowManager());

    public SlowWindowTests() => _window.Open();

    // An in-between frame of a motion must not hold up the frame of every other window.
    [Fact]
    public async Task AFrameDoesNotWaitForTheWindowAndTheWindowStillGetsThere()
    {
        var window = new WindowId(_window.Handle.ToInt64());

        // The first call compiles the path, which is not what this measures.
        var warm = new Rectangle(240, 220, 500, 400);
        _adapter.QuickPlace(window, warm, borderless: true);
        Assert.True(SpinWait.SpinUntil(() => _window.Outer == warm, TimeSpan.FromSeconds(3)), $"the window is at {_window.Outer}");

        // The window cannot take the move until the gate opens, so a placement that waited for it would not return.
        var target = new Rectangle(300, 260, 520, 410);
        SlowWindow.Gate.Reset();
        try
        {
            var placing = Task.Run(() => _adapter.QuickPlace(window, target, borderless: true));
            Assert.Same(placing, await Task.WhenAny(placing, Task.Delay(TimeSpan.FromSeconds(5))));
            Assert.NotEqual(target, _window.Outer);
        }
        finally
        {
            SlowWindow.Gate.Set();
        }

        Assert.True(SpinWait.SpinUntil(() => _window.Outer == target, TimeSpan.FromSeconds(3)), $"the window is at {_window.Outer}");
    }

    public void Dispose()
    {
        _adapter.Dispose();
        _window.Dispose();
    }

    private sealed class SlowWindow : IDisposable
    {
        private const uint CloseMessage = 0x8000;
        private const uint PositionChanging = 0x0046;
        private const string ClassName = "WinlandSlowWindow";

        private static readonly ProbeNative.WndProc Proc = WindowProc;

        /// <summary>Closed, the window holds each move until it opens.</summary>
        public static readonly ManualResetEventSlim Gate = new(true);

        private readonly ManualResetEventSlim _ready = new(false);
        private readonly Thread _thread;
        private uint _threadId;

        public SlowWindow() => _thread = new Thread(Run) { IsBackground = true, Name = ClassName };

        public IntPtr Handle { get; private set; }

        public Rectangle Outer => GetWindowRect(Handle, out var rect)
            ? new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : default;

        public void Open()
        {
            _thread.Start();
            if (!_ready.Wait(TimeSpan.FromSeconds(5)) || Handle == IntPtr.Zero)
                throw new TimeoutException("The slow window did not open.");
        }

        public void Dispose()
        {
            if (_threadId != 0)
                ProbeNative.PostThreadMessage(_threadId, CloseMessage, 0, 0);
            _thread.Join(TimeSpan.FromSeconds(5));
            _ready.Dispose();
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out Bounds rect);

        private static IntPtr WindowProc(IntPtr hwnd, uint message, nint wParam, nint lParam)
        {
            if (message == PositionChanging)
            {
                Gate.Wait(TimeSpan.FromSeconds(10));
                Thread.Sleep(Delay);
            }

            return ProbeNative.DefWindowProc(hwnd, message, wParam, lParam);
        }

        private void Run()
        {
            _threadId = ProbeNative.GetCurrentThreadId();
            ProbeNative.Register(ClassName, Proc);

            // WS_POPUP, never shown: no frame, and nothing on screen for Winland to take.
            Handle = ProbeNative.CreateWindowEx(0, ClassName, ClassName, 0x80000000, 100, 100, 400, 300, IntPtr.Zero, IntPtr.Zero, ProbeNative.Module, IntPtr.Zero);
            _ready.Set();
            while (ProbeNative.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.message == CloseMessage)
                {
                    ProbeNative.DestroyWindow(Handle);
                    ProbeNative.PostQuitMessage(0);
                    continue;
                }

                ProbeNative.TranslateMessage(ref message);
                ProbeNative.DispatchMessage(ref message);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Bounds
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
