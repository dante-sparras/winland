using System.Diagnostics;
using System.Runtime.InteropServices;

using Winland.Core.Tiling;

namespace Winland.Platform.Win32;

/// <summary>
/// Moves the Windows notification center and toasts to a Bar's outer corner: the Bar on the monitor Windows put the surface on (ADR 0080).
/// Windows sizes that surface to the whole monitor, so a top bar would otherwise sit underneath it.
/// While Winoma draws its own toasts, Windows' toast banners are parked off screen instead, so a notification is not shown twice.
/// </summary>
internal sealed class NotificationAnchor
{
    private const uint EventShow = 0x8002;
    private const uint EventHide = 0x8003;
    private const uint EventMove = 0x800B;
    private const uint SkipOwnProcess = 0x0002;

    private readonly Native.WinEventProc _callback;
    private readonly object _gate = new();
    private readonly HashSet<nint> _tracked = [];
    private readonly HashSet<nint> _ignored = [];
    private readonly Dictionary<nint, (long Start, int Count)> _burst = [];
    private readonly HashSet<uint> _otherProcesses = [];
    private nint _showHook;
    private nint _moveHook;
    private nint[] _bars = [];
    private uint _shellPid;
    private uint _hookedPid;
    private long _checkedAt;
    private volatile bool _conceal;

    // Tells when the hooked shell host ends, so nothing has to ask. Null while nothing is hooked, and when the process
    // could not be opened to wait on. Its image is then asked for every five seconds.
    private ShellExit? _exit;

    public NotificationAnchor() => _callback = OnEvent;

    /// <summary>Windows showed a notification window: a banner or the notification center. Raised on the thread that made the hooks.</summary>
    public Action? Shown { get; set; }

    /// <summary>Starts watching shell notification windows.</summary>
    public void Start()
    {
        if (_showHook != 0 || _moveHook != 0)
        {
            Recheck();
            return;
        }

        // Hooked to the shell host alone. A hook on every process would call back for each pointer and caret move on the bar's thread.
        _shellPid = FindShell();
        _checkedAt = Environment.TickCount64;
        Hook(_shellPid);
        Log.Information("Bar", "notifications follow the bar");
        Sweep();
    }

    // The hooks name one process. When the shell host restarts they point at nothing, so they are made again.
    private void Recheck()
    {
        var now = Environment.TickCount64;
        if (_hookedPid != 0 && _exit is { } exit)
        {
            // The process is held open, so its id cannot pass to another program, and its end is told.
            if (!exit.Gone)
            {
                return;
            }
        }
        else
        {
            if (now - _checkedAt < (_hookedPid == 0 ? 30_000 : 5_000))
            {
                return;
            }

            _checkedAt = now;
            if (_hookedPid == 0)
            {
                if (FindShell() is var found && found != 0)
                {
                    _shellPid = found;
                    Retarget(found);
                }

                return;
            }

            if (ImageIsShell(_hookedPid))
            {
                return;
            }
        }

        _checkedAt = now;
        Log.Information("Bar", "the shell host restarted; following its notifications again");
        Unhook();
        lock (_gate)
        {
            _tracked.Clear();
            _ignored.Clear();
            _burst.Clear();
            _otherProcesses.Clear();
        }

        _shellPid = FindShell();
        Hook(_shellPid);
        Sweep();
    }

    private static uint FindShell()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            var session = self.SessionId;
            uint found = 0;
            foreach (var process in Process.GetProcessesByName("ShellExperienceHost"))
            {
                using (process)
                {
                    if (found == 0 && process.SessionId == session)
                    {
                        found = (uint)process.Id;
                    }
                }
            }

            return found;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return 0;
        }
    }

    /// <summary>Remembers the Bars and pulls any open notification against the one on its monitor.</summary>
    /// <param name="bars">Every monitor's Bar, the first one first. The array is kept, so it is one that is replaced and never written.</param>
    public void Follow(nint[] bars)
    {
        _bars = bars;
        Start();
        Reflow();
    }

    /// <summary>Parks Windows' toast banners off screen, or lets them sit at the bar's corner again.</summary>
    /// <param name="conceal">True while Winoma draws its own toasts.</param>
    public void Conceal(bool conceal)
    {
        if (_conceal == conceal)
        {
            return;
        }

        _conceal = conceal;
        Log.Information("Bar", conceal ? "windows toasts are off screen" : "windows toasts follow the bar");
        Reflow();
    }

    /// <summary>Stops moving notification windows.</summary>
    public void Stop()
    {
        Unhook();
        lock (_gate)
        {
            _tracked.Clear();
            _ignored.Clear();
            _burst.Clear();
        }
    }

    private void Hook(uint pid)
    {
        _showHook = Native.SetWinEventHook(EventShow, EventHide, 0, _callback, pid, 0, SkipOwnProcess);
        _moveHook = Native.SetWinEventHook(EventMove, EventMove, 0, _callback, pid, 0, SkipOwnProcess);
        _hookedPid = _showHook != 0 || _moveHook != 0 ? pid : 0;
        _exit = _hookedPid == 0 ? null : ShellExit.Watch(_hookedPid);
    }

    private void Unhook()
    {
        if (_showHook != 0)
        {
            Native.UnhookWinEvent(_showHook);
            _showHook = 0;
        }

        if (_moveHook != 0)
        {
            Native.UnhookWinEvent(_moveHook);
            _moveHook = 0;
        }

        _hookedPid = 0;
        _exit?.Dispose();
        _exit = null;
    }

    private void Retarget(uint pid)
    {
        if (pid == 0 || pid == _hookedPid || (_showHook == 0 && _moveHook == 0))
        {
            return;
        }

        Unhook();
        Hook(pid);
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (hwnd == 0 || idObject != 0 || idChild != 0)
        {
            return;
        }

        if (eventType == EventHide)
        {
            lock (_gate)
            {
                _tracked.Remove(hwnd);
                _burst.Remove(hwnd);
            }

            return;
        }

        if (eventType is not (EventShow or EventMove))
        {
            return;
        }

        bool known;
        bool skipped;
        lock (_gate)
        {
            known = _tracked.Contains(hwnd);
            skipped = !known && _ignored.Contains(hwnd);
        }

        if (skipped)
        {
            return;
        }

        if (!known && !Adopt(hwnd))
        {
            return;
        }

        if (eventType == EventShow)
        {
            Shown?.Invoke();
        }

        Reflow();
    }

    private void Ignore(nint hwnd)
    {
        lock (_gate)
        {
            if (_ignored.Count > 512)
            {
                _ignored.Clear();
            }

            _ignored.Add(hwnd);
        }
    }

    private bool Adopt(nint hwnd)
    {
        var name = ClassOf(hwnd);
        if (!string.Equals(name, "Windows.UI.Core.CoreWindow", StringComparison.Ordinal))
        {
            Ignore(hwnd);
            return false;
        }

        if (!Native.IsWindowVisible(hwnd) || !Native.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        // Windows sizes this surface to the screen, so it is measured against the screen, also while one screen is two monitors.
        var screen = ScreenMetrics.Whole(hwnd);
        if (!NotificationSeat.IsNotification(name, width, height, screen.Width))
        {
            return false;
        }

        if (!IsShell(hwnd))
        {
            Ignore(hwnd);
            return false;
        }

        lock (_gate)
        {
            _ignored.Remove(hwnd);
            return _tracked.Add(hwnd);
        }
    }

    private void Sweep()
    {
        uint count = 0;
        _ = Native.NtUserBuildHwndList(0, 0, false, false, 0, 0, 0, ref count);
        if (count == 0 || count > 8192)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal((int)count * IntPtr.Size);
        try
        {
            if (Native.NtUserBuildHwndList(0, 0, false, false, 0, count, buffer, ref count) != 0)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                var hwnd = Marshal.ReadIntPtr(buffer, i * IntPtr.Size);
                if (hwnd != 0 && hwnd != 1)
                {
                    Adopt(hwnd);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        Reflow();
    }

    private void Reflow()
    {
        nint[] windows;
        lock (_gate)
        {
            // The bar asks on every turn, and nearly every time nothing is tracked: only a banner on screen or the open
            // notification center is. Then there is nothing to place and the bar need not be measured.
            if (_tracked.Count == 0)
            {
                return;
            }

            _tracked.RemoveWhere(hwnd => !Native.IsWindow(hwnd));
            windows = _tracked.Where(Native.IsWindowVisible).ToArray();
        }

        if (windows.Length == 0 || !Frames(out var screens, out var bars))
        {
            return;
        }

        var ranked = new List<(nint Hwnd, int Left, int Top, int Width, int Height)>(windows.Length);
        foreach (var hwnd in windows)
        {
            if (!Native.GetWindowRect(hwnd, out var rect))
            {
                continue;
            }

            ranked.Add((hwnd, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
        }

        ranked.Sort((left, right) => left.Top.CompareTo(right.Top));

        // Each monitor's notifications stack against its own Bar.
        var occupied = new int[screens.Length];
        var conceal = _conceal;
        foreach (var window in ranked)
        {
            // The monitor Windows has the window on names the Bar. A banner parked off screen is on none, and is parked again wherever it is.
            var on = ScreenMetrics.Of(window.Hwnd);
            var at = BarSet.On(screens, new TileRect(on.X, on.Y, on.Width, on.Height));
            var screen = new TileRect(screens[at].X, screens[at].Y, screens[at].Width, screens[at].Height);
            var away = conceal && NotificationSeat.IsBanner(window.Height, screen.Height);
            var slot = away
                ? NotificationSeat.Away(window.Width, window.Height)
                : NotificationSeat.Fit(screen, bars[at], window.Width, window.Height, occupied[at]);
            if (!away)
            {
                occupied[at] += slot.Height;
            }

            if (window.Left == slot.X && window.Top == slot.Y && window.Width == slot.Width && window.Height == slot.Height)
            {
                continue;
            }

            if (!Allow(window.Hwnd))
            {
                continue;
            }

            var flags = Native.SwpNoZOrder | Native.SwpNoActivate;
            if (window.Width == slot.Width && window.Height == slot.Height)
            {
                flags |= Native.SwpNoSize;
            }

            Native.SetWindowPos(window.Hwnd, 0, slot.X, slot.Y, slot.Width, slot.Height, flags);
        }
    }

    private bool Allow(nint hwnd)
    {
        var now = Environment.TickCount64;
        lock (_gate)
        {
            if (!_burst.TryGetValue(hwnd, out var burst) || now - burst.Start > 800)
            {
                burst = (now, 0);
            }

            burst = (burst.Start, burst.Count + 1);
            _burst[hwnd] = burst;
            return burst.Count <= 6;
        }
    }

    /// <summary>Every Bar that can be read: the monitor it is on, whole, and its own rectangle. False when there is none.</summary>
    private bool Frames(out TileRect[] screens, out TileRect[] bars)
    {
        var found = new List<(TileRect Screen, TileRect Bar)>(_bars.Length);
        foreach (var hwnd in _bars)
        {
            if (hwnd == 0 || !Native.GetWindowRect(hwnd, out var rect))
            {
                continue;
            }

            var bounds = ScreenMetrics.Of(hwnd);
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (bounds.Width <= 0 || bounds.Height <= 0 || width <= 0 || height <= 0)
            {
                continue;
            }

            found.Add((new TileRect(bounds.X, bounds.Y, bounds.Width, bounds.Height), new TileRect(rect.Left, rect.Top, width, height)));
        }

        screens = [.. found.Select(pair => pair.Screen)];
        bars = [.. found.Select(pair => pair.Bar)];
        return found.Count > 0;
    }

    private bool IsShell(nint hwnd)
    {
        _ = Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return false;
        }

        if (pid == _shellPid)
        {
            return true;
        }

        lock (_gate)
        {
            if (_otherProcesses.Contains(pid))
            {
                return false;
            }
        }

        if (!ImageIsShell(pid))
        {
            lock (_gate)
            {
                if (_otherProcesses.Count > 64)
                {
                    _otherProcesses.Clear();
                }

                _otherProcesses.Add(pid);
            }

            return false;
        }

        _shellPid = pid;
        Retarget(pid);
        return true;
    }

    internal static bool ImageIsShell(uint pid)
    {
        var process = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, pid);
        if (process == 0)
        {
            return false;
        }

        try
        {
            var name = new char[520];
            var size = name.Length;
            if (!Native.QueryFullProcessImageName(process, 0, name, ref size) || size <= 0)
            {
                return false;
            }

            return new string(name, 0, size).EndsWith("ShellExperienceHost.exe", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Native.CloseHandle(process);
        }
    }

    /// <summary>
    /// Waits on one shell host process and remembers that it ended. The wait is the thread pool's, so the answer costs
    /// a read of a flag where it used to cost opening the process and reading its image every five seconds.
    /// </summary>
    private sealed class ShellExit : IDisposable
    {
        private readonly Process _process;
        private int _gone;

        private ShellExit(Process process) => _process = process;

        /// <summary>True once the process has ended. Set on a pool thread.</summary>
        public bool Gone => Volatile.Read(ref _gone) == 1;

        /// <summary>Starts waiting on <paramref name="pid"/>. Null when it cannot be opened to wait on, or is not the shell host.</summary>
        public static ShellExit? Watch(uint pid)
        {
            Process? process = null;
            try
            {
                process = Process.GetProcessById((int)pid);
                var watch = new ShellExit(process);
                process.Exited += (_, _) => Volatile.Write(ref watch._gone, 1);
                process.EnableRaisingEvents = true;

                // Asked once the process is held open. An id that passed to another program before that is not waited on.
                if (ImageIsShell(pid))
                {
                    return watch;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
            }

            process?.Dispose();
            return null;
        }

        public void Dispose() => _process.Dispose();
    }

    private static string ClassOf(nint hwnd)
    {
        var buffer = new char[64];
        var length = Native.GetClassName(hwnd, buffer, buffer.Length);
        return length <= 0 ? "" : new string(buffer, 0, length);
    }
}
