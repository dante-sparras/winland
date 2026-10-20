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
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static ZoomTween? _tween;
    private static double _tweenStart;
    private static double _factorShown = 1;
    private static Point? _pointer;
    private static bool _cursorHidden;
    private static bool _cursorShownHidden;
    private static Thread? _worker;
    private static bool _stopping;
    private static bool _started;
    private static (float Factor, int X, int Y) _shown = (1, 0, 0);
    private static bool _smoothing = true;

    /// <summary>Takes the zoom settings of the latest outcome.</summary>
    internal static void Configure(CursorZoom zoom)
    {
        lock (Gate)
        {
            if (zoom == _zoom || _stopping)
                return;

            // A new factor eases from the one on screen when the zoomFactor animation is on.
            if (zoom.Factor != _zoom.Factor && zoom.Animation is Timing timing && timing.Milliseconds > 0)
            {
                _tween = new ZoomTween(Volatile.Read(ref _factorShown), zoom.Factor, timing);
                _tweenStart = Clock.Elapsed.TotalMilliseconds;
            }
            else if (zoom.Factor != _zoom.Factor)
            {
                _tween = null;
            }

            _zoom = zoom;
            if (_worker is null && zoom.Factor <= 1 && _tween is null)
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
                bool easing;
                lock (Gate)
                    easing = _tween is not null;

                // While the factor eases the view is drawn about sixty times a second.
                _ = Wake.WaitOne(easing ? 16 : Timeout.Infinite);
                CursorZoom zoom;
                Point? pointer;
                bool hidden;
                lock (Gate)
                {
                    if (_stopping)
                        break;

                    zoom = _zoom;
                    if (_tween is ZoomTween tween)
                    {
                        var elapsed = Clock.Elapsed.TotalMilliseconds - _tweenStart;
                        zoom = zoom with { Factor = tween.At(elapsed) };
                        if (tween.Done(elapsed))
                            _tween = null;
                    }

                    pointer = _pointer;
                    hidden = _cursorHidden;
                }

                Volatile.Write(ref _factorShown, zoom.Factor);

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

        if (!Started())
            return;

        // cursor:zoom_disable_aa: the system magnifier's bitmap smoothing, which Windows' own Magnifier turns with
        // "Smooth edges of images and text". The export is not in the Windows SDK headers.
        var smoothing = !_zoom.DisableAntialiasing;
        if (smoothing != _smoothing && SetSmoothing(smoothing))
            _smoothing = smoothing;

        if (wanted == _shown)
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

    private static bool SetSmoothing(bool smoothing)
    {
        try
        {
            return MagSetFullscreenUseBitmapSmoothing(smoothing);
        }
        catch (EntryPointNotFoundException)
        {
            return true;
        }
    }

    [System.Runtime.InteropServices.DllImport("Magnification.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool MagSetFullscreenUseBitmapSmoothing([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool useSmoothing);

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
