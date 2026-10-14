
using Winland.Core.Tiling;

namespace Winland.Platform.Win32;

/// <summary>Finds a visible window that covers a monitor, such as a fullscreen video.</summary>
internal static class ScreenFill
{
    // One delegate for the life of the process. A fresh lambda per walk built a new native thunk five times a second.
    // The walk's state is a local of the caller, handed over by address: the walk calls back before it returns.
    private static readonly Native.EnumWindowsProc FindCover = static (hwnd, state) =>
    {
        unsafe
        {
            var walk = (Walk*)state;
            if (!Covers(hwnd, walk->Screen, walk->Self, walk->Shell))
            {
                return true;
            }

            walk->Found = hwnd;
            return false;
        }
    };

    /// <summary>True when a window from another process fills <paramref name="screen"/>.</summary>
    public static bool Any(TileRect screen) => Find(screen) != 0;

    /// <summary>True when <paramref name="hwnd"/> fills <paramref name="screen"/>.</summary>
    public static bool This(nint hwnd, TileRect screen) =>
        hwnd != 0 && screen.Width > 0 && screen.Height > 0 && Covers(hwnd, screen, (uint)Environment.ProcessId, Native.GetShellWindow());

    /// <summary>The window that fills <paramref name="screen"/>, or zero.</summary>
    public static nint Find(TileRect screen)
    {
        if (screen.Width <= 0 || screen.Height <= 0)
        {
            return 0;
        }

        var walk = new Walk { Screen = screen, Self = (uint)Environment.ProcessId, Shell = Native.GetShellWindow() };
        unsafe
        {
            Native.EnumWindows(FindCover, (nint)(&walk));
        }

        return walk.Found;
    }

    private const long Transparent = 0x20;

    /// <summary>True when a topmost window still crosses <paramref name="bar"/>.</summary>
    /// <param name="bar">The bar rectangle. The walk only finds candidate windows.</param>
    public static bool TopmostOverlaps(TileRect bar)
    {
        if (bar.Width <= 0 || bar.Height <= 0)
        {
            return false;
        }

        var self = (uint)Environment.ProcessId;
        var hwnd = Native.GetWindow(Native.GetDesktopWindow(), Native.GwChild);
        if (hwnd == 0)
        {
            return false;
        }

        for (var step = 0; hwnd != 0 && step < 4096; step++)
        {
            var visible = Native.IsWindowVisible(hwnd);
            var iconic = Native.IsIconic(hwnd);
            var extended = (long)Native.GetWindowLongPtr(hwnd, Native.GwlExStyle);
            var topmost = (extended & Native.WsExTopmost) != 0;
            if (visible && !iconic && !topmost)
            {
                break;
            }

            if (visible && !iconic && topmost)
            {
                _ = Native.GetWindowThreadProcessId(hwnd, out var process);
                if (process != 0 && process != self
                    && (extended & (Native.WsExToolWindow | Native.WsExNoActivate | Transparent)) == 0
                    && !Cloaked(hwnd)
                    && !ShellClass(hwnd)
                    && Native.GetWindowRect(hwnd, out var rect))
                {
                    var window = new TileRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                    if (BarLayer.Crosses(window, bar))
                    {
                        return true;
                    }
                }
            }

            hwnd = Native.GetWindow(hwnd, Native.GwHwndNext);
        }

        return false;
    }

    private static bool Covers(nint hwnd, TileRect screen, uint self, nint shell)
    {
        if (!Native.IsWindowVisible(hwnd) || hwnd == shell)
        {
            return false;
        }

        // The rectangle first. It is one call, and at most a window or two fill a monitor,
        // so only those are asked their process, style, cloak, and class.
        if (!Native.GetWindowRect(hwnd, out var rect)
            || !ScreenCover.Fills(new TileRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top), screen)
            || Native.IsIconic(hwnd))
        {
            return false;
        }

        _ = Native.GetWindowThreadProcessId(hwnd, out var process);
        if (process == 0 || process == self)
        {
            return false;
        }

        var extended = (long)Native.GetWindowLongPtr(hwnd, Native.GwlExStyle);
        if ((extended & (Native.WsExToolWindow | Native.WsExNoActivate | Transparent)) != 0)
        {
            return false;
        }

        return !Cloaked(hwnd) && !ShellClass(hwnd);
    }

    private static bool ShellClass(nint hwnd)
    {
        // Read on the stack. The bar tick asks this of every visible window.
        Span<char> buffer = stackalloc char[32];
        int length;
        unsafe
        {
            fixed (char* chars = buffer)
            {
                length = Native.GetClassName(hwnd, chars, buffer.Length);
            }
        }

        if (length <= 0)
        {
            return false;
        }

        ReadOnlySpan<char> name = buffer[..length];
        return name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow";
    }

    private static bool Cloaked(nint hwnd) =>
        Native.DwmGetWindowAttribute(hwnd, Native.DwmwaCloaked, out var cloak, sizeof(int)) == 0 && cloak != 0;

    private struct Walk
    {
        public TileRect Screen;
        public uint Self;
        public nint Shell;
        public nint Found;
    }
}
