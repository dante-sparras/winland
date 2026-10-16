using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Winland.Smoke.Tests;

public sealed class SmokeFactAttribute : FactAttribute
{
    public SmokeFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("WINLAND_SMOKE"), "1", StringComparison.Ordinal))
        {
            Skip = "Set WINLAND_SMOKE=1 to run the end-to-end smoke test in a desktop session.";
            return;
        }

        if (DesktopSession.SkipReason() is string reason)
            Skip = reason;
    }
}

public class EndToEndSmokeTests
{
    private readonly ITestOutputHelper _output;

    public EndToEndSmokeTests(ITestOutputHelper output) => _output = output;

    [SmokeFact]
    public void RealDaemonTilesTwoWindowsThroughWinlandctl()
    {
        // -4 is per-monitor v2, matching the daemon, so window rects share one coordinate space.
        _ = PInvoke.SetProcessDpiAwarenessContext((DPI_AWARENESS_CONTEXT)(nint)(-4));
        AssertNoForeignDaemon();
        var userState = UserState.Stamp();
        var paths = SmokePaths.Read();
        var dotnetRoot = SmokeDaemon.ResolveDotnetRoot();
        _output.WriteLine("DOTNET_ROOT=" + dotnetRoot);
        _output.WriteLine("daemon=" + paths.Daemon);

        SmokeWindows? windows = null;
        SmokeDaemon? daemon = null;
        EventLog? events = null;
        string? temp = null;
        Exception? testError = null;
        try
        {
            daemon = SmokeDaemon.Start(paths, dotnetRoot);
            temp = Path.GetDirectoryName(daemon.State);
            daemon.WaitUntilReady();
            RemainUnmanaged(daemon);
            events = EventLog.Follow(paths, daemon.Pipe, dotnetRoot);
            // Workspace 1 has to be the focused monitor's workspace before the test
            // windows open, so a later switch to workspace 2 hides them.
            Assert.Equal("ok", daemon.Dispatch("workspace 1"));
            windows = new SmokeWindows();
            windows.Open();
            WaitUntilTiled(windows, "after open");
            windows.FocusFirst();
            Thread.Sleep(300);
            Assert.Equal("ok", daemon.Dispatch("workspace 2"));
            WaitUntilHidden(windows, () => daemon.Clients() + " monitors " + daemon.Monitors() + " " + daemon.Logs());
            AssertClients(daemon.Clients(), windows, hidden: true);
            Assert.Equal("ok", daemon.Dispatch("workspace 1"));
            WaitUntilTiled(windows, "after workspace 1");
            var clients = daemon.Clients();
            var workspaces = daemon.Workspaces();
            _output.WriteLine(clients);
            _output.WriteLine(workspaces);
            AssertClients(clients, windows, hidden: false);
            AssertWorkspaces(workspaces);
            var lines = events.Lines;
            _output.WriteLine(string.Join(Environment.NewLine, lines));
            AssertEvents(lines);
            events.Dispose();
            events = null;
            Assert.True(daemon.TryStopClean(), "winland did not stop cleanly. " + daemon.Logs());
            AssertVisible(windows);
            Assert.True(Taskbars.AllVisible(out var taskbars), taskbars);
            AssertNoneRunning();
            Assert.Equal(userState, UserState.Stamp());
        }
        catch (Exception exception)
        {
            testError = exception;
        }

        Exception? cleanupError = null;
        try
        {
            events?.Dispose();
            if (daemon is { Exited: false })
            {
                if (!daemon.TryStopClean())
                {
                    daemon.Kill();
                    daemon.Recover();
                }
            }

            AssertVisibleIfStillOpen(windows);
            windows?.Close();
            if (daemon is not null && !Taskbars.AllVisible(out _))
            {
                if (!daemon.Exited)
                    daemon.Kill();
                daemon.Recover();
            }

            if (!Taskbars.AllVisible(out var taskbars))
                throw new InvalidOperationException(taskbars);

            if (temp is not null)
                SmokeDaemon.DeleteTree(temp);

            if (temp is not null && Directory.Exists(temp))
                throw new InvalidOperationException($"Temporary files remain at {temp}.");

            AssertNoneRunning();
        }
        catch (Exception exception)
        {
            cleanupError = exception;
        }
        finally
        {
            daemon?.Dispose();
        }

        if (testError is not null && cleanupError is not null)
            throw new AggregateException(testError, cleanupError);

        if (testError is not null)
            ExceptionDispatchInfo.Capture(testError).Throw();

        if (cleanupError is not null)
            throw cleanupError;
    }

    private static void RemainUnmanaged(SmokeDaemon daemon)
    {
        var deadline = Environment.TickCount64 + 1_000;
        while (Environment.TickCount64 < deadline)
        {
            var clients = daemon.Clients();
            Assert.True(clients == "[]", "winland adopted a window it should have ignored: " + clients + " " + daemon.Logs());
            Thread.Sleep(100);
        }
    }

    private static void WaitUntilTiled(SmokeWindows windows, string when)
    {
        var deadline = Environment.TickCount64 + 20_000;
        string last = "";
        while (Environment.TickCount64 < deadline)
        {
            if (TryBoxes(windows, out var first, out var second, out var work) && SideBySide(first, second, work))
                return;

            last = Describe(windows);
            Thread.Sleep(50);
        }

        Assert.Fail($"The test windows were not tiled side by side {when}. {last}");
    }

    private static void WaitUntilHidden(SmokeWindows windows, Func<string> detail)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (Environment.TickCount64 < deadline)
        {
            if (!PInvoke.IsWindowVisible(windows.First) && !PInvoke.IsWindowVisible(windows.Second))
                return;

            Thread.Sleep(50);
        }

        Assert.Fail($"workspace 2 left a test window visible. {Describe(windows)} {detail()}");
    }

    private static void AssertVisible(SmokeWindows windows)
    {
        Assert.True(PInvoke.IsWindow(windows.First) && PInvoke.IsWindowVisible(windows.First), "The first test window stayed hidden.");
        Assert.True(PInvoke.IsWindow(windows.Second) && PInvoke.IsWindowVisible(windows.Second), "The second test window stayed hidden.");
    }

    private static void AssertVisibleIfStillOpen(SmokeWindows? windows)
    {
        if (windows is null || windows.First.IsNull)
            return;

        if (!PInvoke.IsWindow(windows.First))
            return;

        AssertVisible(windows);
    }

    private static void AssertClients(string json, SmokeWindows windows, bool hidden)
    {
        using var document = JsonDocument.Parse(json);
        var clients = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, clients.Length);
        var addresses = clients.Select(client => client.GetProperty("address").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Addresses.Of(windows.First), addresses);
        Assert.Contains(Addresses.Of(windows.Second), addresses);
        foreach (var client in clients)
        {
            Assert.Equal(!hidden, client.GetProperty("mapped").GetBoolean());
            Assert.Equal(hidden, client.GetProperty("hidden").GetBoolean());
            Assert.False(client.GetProperty("floating").GetBoolean());
            Assert.Equal(1, client.GetProperty("workspace").GetProperty("id").GetInt32());
        }
    }

    private static void AssertWorkspaces(string json)
    {
        using var document = JsonDocument.Parse(json);
        var workspace = document.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetInt32() == 1);
        Assert.Equal("1", workspace.GetProperty("name").GetString());
        Assert.Equal(2, workspace.GetProperty("windows").GetInt32());
    }

    private static void AssertEvents(IReadOnlyList<string> lines)
    {
        var rendered = string.Join(" | ", lines);
        Assert.True(
            lines.Any(line => line.Contains("WinlandSmokeWindow,WinlandSmoke 1", StringComparison.Ordinal) && line.StartsWith("openwindow>>", StringComparison.Ordinal)),
            "The event stream missed openwindow for the first window. " + rendered);
        Assert.True(
            lines.Any(line => line.Contains("WinlandSmokeWindow,WinlandSmoke 2", StringComparison.Ordinal) && line.StartsWith("openwindow>>", StringComparison.Ordinal)),
            "The event stream missed openwindow for the second window. " + rendered);
        Assert.True(
            lines.Any(line => line.StartsWith("workspacev2>>", StringComparison.Ordinal)),
            "The event stream missed workspacev2. " + rendered);
    }

    private static unsafe bool TryBoxes(SmokeWindows windows, out Box first, out Box second, out Box work)
    {
        first = default;
        second = default;
        work = default;
        if (!TryFrame(windows.First, out var firstRect) || !TryFrame(windows.Second, out var secondRect))
            return false;

        var monitor = PInvoke.MonitorFromWindow(windows.First, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (monitor.IsNull || !PInvoke.GetMonitorInfo(monitor, ref info))
            return false;

        first = Box.From(firstRect);
        second = Box.From(secondRect);
        work = Box.From(info.rcWork);
        return true;
    }

    // The visible frame, which Winland fits to the Layout rectangle. The outer rectangle of a window
    // with a sizing border also takes in the invisible border around it.
    private static unsafe bool TryFrame(HWND hwnd, out RECT rect)
    {
        rect = default;
        Span<byte> value = stackalloc byte[sizeof(RECT)];
        if (PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, value).Failed)
            return false;

        rect = MemoryMarshal.Read<RECT>(value);
        return true;
    }

    private static bool SideBySide(Box first, Box second, Box work)
    {
        if (!work.Contains(first) || !work.Contains(second))
            return false;

        if (first.Width < 80 || second.Width < 80 || first.Height < 80 || second.Height < 80)
            return false;

        var left = first.Left <= second.Left ? first : second;
        var right = first.Left <= second.Left ? second : first;
        var overlapY = Math.Min(left.Bottom, right.Bottom) - Math.Max(left.Top, right.Top);
        if (overlapY < Math.Min(left.Height, right.Height) * 8 / 10)
            return false;

        var gap = right.Left - left.Right;
        if (gap is < -8 or > 24)
            return false;

        if (right.Right - left.Left < work.Width * 7 / 10)
            return false;

        if (Math.Max(left.Height, right.Height) < work.Height * 7 / 10)
            return false;

        return true;
    }

    private static string Describe(SmokeWindows windows)
    {
        var visible = $"visible={PInvoke.IsWindowVisible(windows.First)},{PInvoke.IsWindowVisible(windows.Second)}";
        if (!TryBoxes(windows, out var first, out var second, out var work))
            return visible;

        return $"{visible} first={first} second={second} work={work}";
    }

    private static void AssertNoForeignDaemon()
    {
        var running = Process.GetProcessesByName("winland");
        try
        {
            if (running.Length > 0)
                Assert.Fail($"A winland process is already running (pid {running[0].Id}). The smoke test will not start a second one.");
        }
        finally
        {
            foreach (var process in running)
                process.Dispose();
        }
    }

    private static void AssertNoneRunning()
    {
        var deadline = Environment.TickCount64 + 5_000;
        Process[] running = [];
        while (Environment.TickCount64 < deadline)
        {
            foreach (var process in running)
                process.Dispose();

            running = Process.GetProcessesByName("winland");
            if (running.Length == 0)
                return;

            Thread.Sleep(100);
        }

        try
        {
            Assert.Fail("A winland process is still running: " + string.Join(", ", running.Select(process => process.Id.ToString())));
        }
        finally
        {
            foreach (var process in running)
                process.Dispose();
        }
    }

    private readonly record struct Box(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;

        public int Height => Bottom - Top;

        public bool Contains(Box window) =>
            window.Left >= Left - 4
            && window.Top >= Top - 4
            && window.Right <= Right + 4
            && window.Bottom <= Bottom + 4;

        public static Box From(RECT rect) => new(rect.left, rect.top, rect.right, rect.bottom);

        public override string ToString() => $"{Left},{Top} {Width}x{Height}";
    }
}
