using System.Diagnostics;
using System.Runtime.InteropServices;
using Winland;
using Winland.Windows;

namespace Winland.Windows.Tests;

public sealed class FocusFactAttribute : FactAttribute
{
    public FocusFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("WINLAND_FOCUS"), "1", StringComparison.Ordinal))
            Skip = "Set WINLAND_FOCUS=1 to check foreground restore on the desktop.";
    }
}

public class FocusRestoreTests
{
    [FocusFact]
    public void AnApplicationThatTakesTheForegroundIsSentBack()
    {
        var previous = Desktop.Foreground();
        if (previous == 0)
            throw new InvalidOperationException("No foreground window is available.");

        string? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                failure = Run();
            }
            catch (Exception exception)
            {
                failure = exception.ToString();
            }
        })
        {
            IsBackground = true,
            Name = "WinlandFocusCheck",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(20)))
            failure = "The focus check did not finish.";

        try
        {
            _ = Desktop.Restore(previous);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure = failure is null ? exception.Message : failure + Environment.NewLine + exception.Message;
        }

        Assert.True(failure is null, failure);
    }

    private static string? Run()
    {
        var exe = ThiefPath();
        if (!File.Exists(exe))
            return "The focus thief was not built at " + exe;

        var core = new Core();
        using var keys = new KeyboardHook(core);
        using var mouse = new MouseHook(core, keys);
        keys.Start();
        mouse.Start();

        var keep = Desktop.Create("WinlandFocusKeep", "WinlandFocusKeep", 80, 80);
        var hook = Desktop.Hook(out var callback);
        Process? thief = null;
        try
        {
            core.Handle(new WindowAppeared(new WindowId(keep), new Rectangle(0, 0, 800, 600)));
            if (!Foreground.MoveTo(new WindowId(keep)))
                return "Could not focus the window under test.";

            Pump(TimeSpan.FromMilliseconds(100));
            thief = Process.Start(new ProcessStartInfo(exe)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (thief is null)
                return "The focus thief did not start.";

            var line = thief.StandardOutput.ReadLine();
            if (!long.TryParse(line, out var thiefValue) || thiefValue == 0)
                return "The focus thief did not report a window. " + thief.StandardError.ReadToEnd();

            Desktop.Handle = (hwnd, time) =>
            {
                if (hwnd != keep && hwnd != thiefValue)
                    return;

                var window = new WindowId(hwnd);
                var outcome = core.Handle(new WindowFocused(window, Foreground.Classify(window, time)));
                if (outcome.Focus is WindowId focus)
                    _ = Foreground.MoveTo(focus);
            };

            // A click during setup must age out before the other process takes the foreground.
            Pump(TimeSpan.FromMilliseconds(InputClock.RecentMilliseconds + 200));
            if (!Foreground.MoveTo(new WindowId(keep)))
                return "Could not focus the window under test before the steal.";

            Pump(TimeSpan.FromMilliseconds(100));
            thief.StandardInput.WriteLine("go");
            thief.StandardInput.Flush();

            var until = Environment.TickCount64 + 3_000;
            while (Environment.TickCount64 < until)
            {
                Pump(TimeSpan.FromMilliseconds(50));
                if (Desktop.Foreground() == keep)
                    return null;
            }

            return "Foreground stayed on 0x" + Desktop.Foreground().ToString("x") + " (" + Desktop.Title(Desktop.Foreground()) + ") after the other process called SetForegroundWindow.";
        }
        finally
        {
            Desktop.Handle = null;
            if (hook != 0)
                _ = Desktop.UnhookWinEvent(hook);
            if (!keep.Equals(0))
                _ = Desktop.Destroy(keep);
            if (thief is { HasExited: false })
            {
                try
                {
                    thief.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
            }

            thief?.Dispose();
            _ = callback;
        }
    }

    private static void Pump(TimeSpan duration)
    {
        var until = Environment.TickCount64 + (long)duration.TotalMilliseconds;
        while (Environment.TickCount64 < until)
        {
            while (Desktop.Peek(out var message))
            {
                Desktop.TranslateMessage(ref message);
                Desktop.DispatchMessage(ref message);
            }

            Thread.Sleep(10);
        }
    }

    private static string ThiefPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "focus-thief.path");
        if (!File.Exists(path))
            throw new FileNotFoundException("focus-thief.path", path);

        return File.ReadAllText(path).Trim();
    }
}

internal static class Desktop
{
    private const uint ForegroundEvent = 0x0003;
    private const uint OutOfContext = 0x0000;
    private const int ClassAlreadyExists = 1410;
    private const uint OverlappedWindow = 0x00CF0000;
    private const uint Visible = 0x10000000;

    private static readonly WndProc Proc = WindowProc;
    private static readonly EventProc Event = OnEvent;

    internal static Action<long, uint>? Handle;

    internal static long Create(string className, string title, int x, int y)
    {
        var info = new WndClass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
            hInstance = Module(null),
            hbrBackground = 6,
            lpszClassName = className,
        };
        if (RegisterClassW(ref info) == 0 && Marshal.GetLastWin32Error() != ClassAlreadyExists)
            throw new InvalidOperationException($"The window class was not registered ({Marshal.GetLastWin32Error()}).");

        var hwnd = CreateWindowExW(0, className, title, OverlappedWindow | Visible, x, y, 320, 180, 0, 0, Module(null), 0);
        if (hwnd == 0)
            throw new InvalidOperationException($"The window was not created ({Marshal.GetLastWin32Error()}).");

        return hwnd.ToInt64();
    }

    internal static nint Hook(out EventProc callback)
    {
        callback = Event;
        return SetWinEventHook(ForegroundEvent, ForegroundEvent, 0, callback, 0, 0, OutOfContext);
    }

    private static void OnEvent(nint hook, uint @event, nint hwnd, int idObject, int idChild, uint idEventThread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == 0)
            return;

        try
        {
            Handle?.Invoke(hwnd.ToInt64(), time);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
    }

    internal static bool Restore(long hwnd)
    {
        var window = new IntPtr(hwnd);
        if (SetForegroundWindow(window))
            return Foreground() == hwnd;

        var foreground = new IntPtr(Foreground());
        var foregroundThread = ThreadOf(foreground);
        var current = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != current && AttachThreadInput(foregroundThread, current, true);
        try
        {
            _ = SetForegroundWindow(window);
            return Foreground() == hwnd;
        }
        finally
        {
            if (attached)
                _ = AttachThreadInput(foregroundThread, current, false);
        }
    }

    internal static string Title(long hwnd)
    {
        var buffer = new char[256];
        var length = GetWindowText(new IntPtr(hwnd), buffer, buffer.Length);
        return length <= 0 ? "" : new string(buffer, 0, length);
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void EventProc(nint hook, uint @event, nint hwnd, int idObject, int idChild, uint idEventThread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WndClass wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    internal static bool Destroy(long hwnd) => DestroyWindow(new IntPtr(hwnd));

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint min, uint max, nint module, EventProc proc, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessage(out Msg message, IntPtr hwnd, uint min, uint max, uint remove);

    internal static bool Peek(out Msg message) => PeekMessage(out message, 0, 0, 0, 1);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessage(ref Msg message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, char[] text, int length);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool join);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr Module(string? name);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private static uint ThreadOf(IntPtr hwnd) => GetWindowThreadProcessId(hwnd, out _);

    internal static long Foreground() => GetForegroundWindow().ToInt64();
}
