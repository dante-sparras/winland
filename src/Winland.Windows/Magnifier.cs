using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Winland.Windows;

/// <summary>
/// Draws the cursor zoom and hides the cursor with the system magnifier, on its own thread so a pointer move never
/// waits for it. The magnifier zooms the whole desktop as one surface, so while the Monitor under the cursor is zoomed,
/// the others show the same factor of what lies beside it. It hides the cursor over every program, not only over
/// Winland's own windows. Both go away with this process, even when it is killed.
/// </summary>
internal static class Magnifier
{
    private static readonly Lock Gate = new();
    private static readonly AutoResetEvent Wake = new(false);
    private static readonly ZoomCamera Camera = new();
    private static CursorZoom _zoom = CursorZoom.None;
    private static Point? _pointer;
    private static bool _cursorHidden;
    private static bool _cursorShownHidden;
    private static Thread? _worker;
    private static bool _stopping;
    private static bool _started;
    private static (float Factor, int X, int Y) _shown = (1, 0, 0);

    /// <summary>Takes the zoom settings of the latest outcome.</summary>
    internal static void Configure(CursorZoom zoom)
    {
        lock (Gate)
        {
            if (zoom == _zoom || _stopping)
                return;

            _zoom = zoom;
            if (_worker is null && zoom.Factor <= 1)
                return;

            Start();
        }

        _ = Wake.Set();
    }

    /// <summary>Hides the cursor everywhere, or shows it again.</summary>
    internal static void HideCursor(bool hidden)
    {
        lock (Gate)
        {
            if (hidden == _cursorHidden || _stopping)
                return;

            _cursorHidden = hidden;
            Start();
        }

        _ = Wake.Set();
    }

    /// <summary>Follows the cursor to <paramref name="position"/>.</summary>
    internal static void Moved(Point position)
    {
        lock (Gate)
        {
            _pointer = position;
            if (_worker is null)
                return;
        }

        _ = Wake.Set();
    }

    /// <summary>Takes the zoom away, shows the cursor and ends the thread.</summary>
    internal static void Stop()
    {
        Thread? worker;
        lock (Gate)
        {
            worker = _worker;
            _stopping = true;
        }

        if (worker is null)
            return;

        _ = Wake.Set();
        _ = worker.Join(TimeSpan.FromSeconds(2));
    }

    private static void Start()
    {
        if (_worker is not null)
            return;

        _worker = new Thread(Run) { IsBackground = true, Name = "Winland magnifier" };
        _worker.Start();
    }

    private static void Run()
    {
        try
        {
            while (true)
            {
                _ = Wake.WaitOne();
                CursorZoom zoom;
                Point? pointer;
                bool hidden;
                lock (Gate)
                {
                    if (_stopping)
                        break;

                    zoom = _zoom;
                    pointer = _pointer;
                    hidden = _cursorHidden;
                }

                Show(zoom, pointer);
                ShowCursor(hidden);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine(exception.Message);
        }
        finally
        {
            if (_started)
            {
                _ = PInvoke.MagShowSystemCursor(true);
                _ = PInvoke.MagSetFullscreenTransform(1, 0, 0);
                _ = PInvoke.MagUninitialize();
                _started = false;
            }
        }
    }

    private static void Show(CursorZoom zoom, Point? known)
    {
        var pointer = known ?? Cursor();
        var wanted = (Factor: 1f, X: 0, Y: 0);

        // A screen point S shows the desktop point offset + S / factor, so the view's top-left lands on the Monitor's.
        if (Monitor(pointer) is Rectangle monitor && Camera.Follow(monitor, pointer, zoom) is ZoomView view)
        {
            wanted = (
                (float)view.Factor,
                (int)Math.Round(view.X - (monitor.X / view.Factor)),
                (int)Math.Round(view.Y - (monitor.Y / view.Factor)));
        }

        if (wanted == _shown || !Started())
            return;

        if (PInvoke.MagSetFullscreenTransform(wanted.Factor, wanted.X, wanted.Y))
            _shown = wanted;
    }

    // The system cursor stays hidden only while this process runs, so a Daemon that is killed leaves it visible.
    private static void ShowCursor(bool hidden)
    {
        if (hidden == _cursorShownHidden || !Started())
            return;

        if (PInvoke.MagShowSystemCursor(!hidden))
            _cursorShownHidden = hidden;
    }

    private static bool Started()
    {
        if (!_started && PInvoke.MagInitialize())
            _started = true;

        return _started;
    }

    private static Point Cursor() =>
        PInvoke.GetCursorPos(out var position) ? new Point(position.X, position.Y) : new Point(0, 0);

    private static unsafe Rectangle? Monitor(Point pointer)
    {
        var monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(pointer.X, pointer.Y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (monitor.IsNull || !PInvoke.GetMonitorInfo(monitor, &info))
            return null;

        var rect = info.rcMonitor;
        return new Rectangle(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
    }
}
