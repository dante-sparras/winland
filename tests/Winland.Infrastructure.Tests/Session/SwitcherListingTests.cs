using System.Runtime.InteropServices;
using static Winland.Infrastructure.Tests.ProbeNative;

namespace Winland.Infrastructure.Tests;

[Collection(DesktopCollection.Name)]
[Trait("Category", DesktopCollection.Name)]
public class SwitcherListingTests
{
    [DesktopFact]
    public void HideRemovesAWindowFromTheSwitcher()
    {
        using var window = ProbeWindow.Open();

        Assert.True(Listed(window.Handle));

        window.Hide();
        Assert.False(window.Visible);
        Assert.False(Listed(window.Handle));

        window.Show();
        Assert.True(Listed(window.Handle));

        window.Minimize();
        Assert.True(window.Iconic);
        Assert.True(Listed(window.Handle));

        window.Hide();
        Assert.True(window.Iconic);
        Assert.False(Listed(window.Handle));

        window.ShowMinimized();
        Assert.True(window.Iconic);
        Assert.True(window.Visible);
        Assert.True(Listed(window.Handle));

        window.Restore();
        window.MoveOffScreen();
        Assert.True(window.Visible);
        Assert.True(Listed(window.Handle));

        window.Hide();
        Assert.False(Listed(window.Handle));
    }

    private static bool Listed(IntPtr window)
    {
        var found = false;
        EnumWindows((candidate, _) =>
        {
            if (candidate == window && InSwitcher(candidate))
                found = true;
            return true;
        }, 0);
        return found;
    }

    private static bool InSwitcher(IntPtr window)
    {
        if (!IsWindowVisible(window) || GetAncestor(window, 2) != window)
            return false;

        var extended = GetWindowLongPtr(window, -20);
        if ((extended & 0x80) != 0 || GetWindow(window, 4) != 0)
            return false;

        var cloaked = 0;
        _ = DwmGetWindowAttribute(window, 14, ref cloaked, sizeof(int));
        return cloaked == 0;
    }

    private sealed class ProbeWindow : IDisposable
    {
        private readonly Thread _thread;
        private bool _closed;

        private ProbeWindow(Thread thread, IntPtr handle)
        {
            _thread = thread;
            Handle = handle;
        }

        public IntPtr Handle { get; }

        public bool Visible => IsWindowVisible(Handle);

        public bool Iconic => IsIconic(Handle);

        public static ProbeWindow Open()
        {
            using var ready = new ManualResetEventSlim(false);
            IntPtr handle = 0;
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    handle = Create();
                    ready.Set();
                    while (GetMessage(out var message, 0, 0, 0) > 0)
                    {
                        TranslateMessage(ref message);
                        DispatchMessage(ref message);
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
                Name = "SwitcherProbe",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(5)) || handle == 0)
                throw error ?? new InvalidOperationException("The probe window did not open.");

            return new ProbeWindow(thread, handle);
        }

        public void Hide() => ShowWindow(Handle, 0);

        public void Show() => ShowWindow(Handle, 4);

        public void Minimize() => ShowWindow(Handle, 6);

        public void ShowMinimized() => ShowWindow(Handle, 7);

        public void Restore() => ShowWindow(Handle, 9);

        public void MoveOffScreen() =>
            SetWindowPos(Handle, 0, -32000, -32000, 200, 120, 0x0014);

        public void Dispose()
        {
            if (_closed)
                return;

            _closed = true;
            ShowWindow(Handle, 9);
            PostMessage(Handle, 0x0010, 0, 0);
            _thread.Join(TimeSpan.FromSeconds(3));
        }

        private static IntPtr Create()
        {
            var module = ProbeNative.Module;
            ProbeNative.Register("WinlandSwitcherProbe", ProbeProc.Proc);

            var handle = CreateWindowEx(
                0x40000,
                "WinlandSwitcherProbe",
                "WinlandSwitcherProbe",
                0x00CF0000,
                40,
                40,
                200,
                120,
                0,
                0,
                module,
                0);
            if (handle == 0)
                throw new InvalidOperationException("The probe window was not created.");

            ShowWindow(handle, 5);
            return handle;
        }
    }

    private static class ProbeProc
    {
        internal static readonly ProbeNative.WndProc Proc = WindowProc;

        private static IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == 0x0002)
            {
                PostQuitMessage(0);
                return 0;
            }

            return DefWindowProc(window, message, wParam, lParam);
        }
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
