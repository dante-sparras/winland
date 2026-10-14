
using Winland.Core.Tiling;

namespace Winland.Platform.Win32;

/// <summary>Monitor rectangles and DPI conversion.</summary>
public static class ScreenMetrics
{
    /// <summary>Outer bounds of the monitor that contains <paramref name="window"/>, in physical pixels.</summary>
    public static ScreenBounds Of(nint window) => Bounds(window, whole: false);

    /// <summary>
    /// Outer bounds of the screen Windows has <paramref name="window"/> on, in physical pixels: the whole screen,
    /// even while one screen is two monitors. For a surface Windows itself sizes to the screen, such as its notification windows.
    /// </summary>
    internal static ScreenBounds Whole(nint window) => Bounds(window, whole: true);

    private static ScreenBounds Bounds(nint window, bool whole)
    {
        var monitor = Native.MonitorFromWindow(window, 2);
        var info = new Native.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>() };
        if (monitor != 0 && Native.GetMonitorInfo(monitor, ref info))
        {
            var rect = whole ? info.Monitor : Part(window, info.Monitor);
            return new ScreenBounds(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
        }

        return new ScreenBounds(0, 0, 1920, 1080);
    }

    /// <summary>Physical pixels per device-independent pixel for <paramref name="window"/>.</summary>
    /// <param name="window">A window on the monitor to measure.</param>
    /// <returns>One when the window has no DPI.</returns>
    public static double Scale(nint window)
    {
        var dpi = Native.GetDpiForWindow(window);
        return dpi == 0 ? 1 : dpi / 96.0;
    }

    /// <summary>Converts device-independent pixels to physical pixels for <paramref name="window"/>.</summary>
    public static int Dips(nint window, int dips)
    {
        var dpi = Native.GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = 96;
        }

        return (int)Math.Ceiling(dips * dpi / 96.0);
    }

    /// <summary>The oldest monitor list that is still used, in milliseconds. The safety net behind <see cref="Invalidate"/>.</summary>
    private const long MaxAgeMs = 5000;

    // The development switch: every screen is listed as two monitors, its left and right halves (ScreenHalves).
    // Read once, when the first monitor is asked for. The desktop is started through WMI, so a file reaches it where a variable would not.
    private static readonly bool AsTwo = File.Exists(Path.Combine(DesktopFolder.Path, ScreenHalves.FileName));
    private static bool _toldAsTwo;

    // An arrange asks for the monitors three to six times, and each walk built a native thunk for its callback.
    // The list is kept until a display or work-area change is reported, the first Bar's monitor reads differently, or it is five seconds old.
    private static volatile MonitorList? _monitors;
    private static volatile AreaList? _areas;
    private static volatile AreaList? _screens;

    /// <summary>One monitor as Winoma lists it: its edges in physical pixels, and the handle Windows gives the screen it is on.</summary>
    private readonly record struct MonitorEdges(int Left, int Top, int Right, int Bottom, nint Monitor);

    [ThreadStatic]
    private static List<MonitorEdges>? _walk;

    // One delegate for the life of the process, so a walk builds no native thunk.
    private static readonly Native.MonitorEnumProc Visit = static (monitor, _, _, _) =>
    {
        var info = new Native.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>() };
        if (Native.GetMonitorInfo(monitor, ref info))
        {
            _walk!.Add(new MonitorEdges(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom, monitor));
        }

        return true;
    };

    /// <summary>
    /// Drops the kept monitor list, so the next read walks the monitors again. Called when Windows reports a display,
    /// DPI, or work-area change. Safe from any thread.
    /// </summary>
    public static void Invalidate() => _monitors = null;

    /// <summary>Full monitor rectangles, top to bottom then left to right, including the strip the bar reserves.</summary>
    public static IReadOnlyList<TileRect> Screens()
    {
        var monitors = Monitors();
        if (_screens is { } kept && ReferenceEquals(kept.From, monitors))
        {
            return kept.Rects;
        }

        var rects = Inset(monitors, [], ScreenEdge.Top);
        _screens = new AreaList(monitors, [], ScreenEdge.Top, rects);
        return rects;
    }

    /// <summary>
    /// Work areas of the attached monitors, top to bottom then left to right. Each monitor loses the strip of the Bar that lies on it,
    /// as thick as that Bar is, on <paramref name="edge"/> (<see cref="BarReserve"/>).
    /// </summary>
    /// <param name="bars">
    /// The outer rectangle of each Bar on the glass. Empty while the Bar is hidden. The same array while no Bar moved,
    /// so the areas made from it are kept.
    /// </param>
    /// <param name="edge">The edge every Bar is on.</param>
    public static IReadOnlyList<TileRect> Areas(TileRect[] bars, ScreenEdge edge)
    {
        var monitors = Monitors();
        if (_areas is { } kept && ReferenceEquals(kept.From, monitors) && ReferenceEquals(kept.Bars, bars) && kept.Edge == edge)
        {
            return kept.Rects;
        }

        var rects = Inset(monitors, bars, edge);
        _areas = new AreaList(monitors, bars, edge, rects);
        return rects;
    }

    private static MonitorList Monitors()
    {
        var now = Environment.TickCount64;
        if (_monitors is { } kept && now - kept.ReadAt < MaxAgeMs)
        {
            return kept;
        }

        var found = _walk ??= [];
        found.Clear();
        Native.EnumDisplayMonitors(0, 0, Visit, 0);
        found.Sort(static (a, b) => a.Top != b.Top ? a.Top.CompareTo(b.Top) : a.Left.CompareTo(b.Left));
        var walked = found.ToArray();
        var read = new MonitorList(AsTwo ? Halved(walked) : walked, walked, now);
        _monitors = read;
        return read;
    }

    /// <summary>
    /// Two monitors for each screen in <paramref name="walked"/>, in the same order. Both halves keep the screen's handle,
    /// so a half is told from the other by its left edge.
    /// </summary>
    private static MonitorEdges[] Halved(MonitorEdges[] walked)
    {
        var screens = new TileRect[walked.Length];
        for (var i = 0; i < walked.Length; i++)
        {
            screens[i] = new TileRect(walked[i].Left, walked[i].Top, walked[i].Right - walked[i].Left, walked[i].Bottom - walked[i].Top);
        }

        var halves = ScreenHalves.Split(screens);
        var listed = new MonitorEdges[halves.Count];
        for (var i = 0; i < listed.Length; i++)
        {
            var half = halves[i];
            listed[i] = new MonitorEdges(half.X, half.Y, half.X + half.Width, half.Y + half.Height, walked[i / 2].Monitor);
        }

        if (!_toldAsTwo)
        {
            _toldAsTwo = true;
            Log.Information("Screen", "each screen is two monitors, because " + ScreenHalves.FileName + " is in the user folder: "
                + string.Join(", ", halves.Select(static half => half.X + "," + half.Y + " " + half.Width + "x" + half.Height)));
        }

        return listed;
    }

    /// <summary>
    /// The monitor <paramref name="window"/> is on, from the screen Windows names for it: that screen,
    /// or the half of it the window is on while one screen is two monitors.
    /// </summary>
    internal static Native.Rect Part(nint window, Native.Rect screen)
    {
        if (!AsTwo)
        {
            return screen;
        }

        var whole = new TileRect(screen.Left, screen.Top, screen.Right - screen.Left, screen.Bottom - screen.Top);
        var at = window != 0 && Native.GetWindowRect(window, out var rect)
            ? new TileRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : whole;
        var half = ScreenHalves.Of(whole, at);
        return new Native.Rect { Left = half.X, Top = half.Y, Right = half.X + half.Width, Bottom = half.Y + half.Height };
    }

    private static TileRect[] Inset(MonitorList monitors, TileRect[] bars, ScreenEdge edge)
    {
        var found = monitors.Found;
        var screens = new TileRect[found.Length];
        for (var i = 0; i < screens.Length; i++)
        {
            var monitor = found[i];
            screens[i] = new TileRect(
                monitor.Left,
                monitor.Top,
                Math.Max(1, monitor.Right - monitor.Left),
                Math.Max(1, monitor.Bottom - monitor.Top));
        }

        // A Bar is told to its monitor by where it lies, so the two halves of one screen are two monitors here as everywhere.
        return bars.Length == 0 ? screens : BarReserve.Areas(screens, bars, edge);
    }

    /// <summary>
    /// The monitors as one walk found them, sorted top to bottom then left to right.
    /// <paramref name="Found"/> is what Winoma lists. <paramref name="Walked"/> is what Windows listed: the same array,
    /// or the screens <paramref name="Found"/> holds the halves of while one screen is two monitors.
    /// </summary>
    private sealed record MonitorList(
        MonitorEdges[] Found,
        MonitorEdges[] Walked,
        long ReadAt);

    /// <summary>The rectangles last made from a monitor list, for the Bars as they lay and their edge.</summary>
    private sealed record AreaList(MonitorList From, TileRect[] Bars, ScreenEdge Edge, TileRect[] Rects);

    /// <summary>
    /// Drops the kept monitor list when it does not hold <paramref name="monitor"/> with <paramref name="rect"/>.
    /// The bar loop reads the first Bar's monitor on every turn, so a display change it sees is never placed from an old list.
    /// </summary>
    private static void Check(nint monitor, Native.Rect rect)
    {
        if (_monitors is not { } kept)
        {
            return;
        }

        foreach (var found in kept.Walked)
        {
            if (found.Monitor == monitor)
            {
                if (found.Left != rect.Left || found.Top != rect.Top || found.Right != rect.Right || found.Bottom != rect.Bottom)
                {
                    Invalidate();
                }

                return;
            }
        }

        Invalidate();
    }

    internal static Native.Rect MonitorRect(nint source)
    {
        var window = source != 0 ? source : Native.GetForegroundWindow();
        var monitor = Native.MonitorFromWindow(window, 2);
        var info = new Native.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>() };
        if (monitor != 0 && Native.GetMonitorInfo(monitor, ref info))
        {
            Check(monitor, info.Monitor);
            return Part(window, info.Monitor);
        }

        var rect = new Native.Rect();
        Native.SystemParametersInfo(Native.SpiGetWorkArea, 0, ref rect, 0);
        return rect;
    }
}
