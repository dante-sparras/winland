
namespace Winland.Platform.Win32;

/// <summary>
/// Fades another program's window in as it opens and out as it is closed (<see cref="WindowFades"/>). The window is made a layered
/// window with an opacity for as long as the fade lasts, and is an ordinary window again after it: Windows draws the fade, with the
/// window's own backdrop, and nothing of Winoma's is drawn over it.
/// A window that is clear carries the property <see cref="Property"/>, so a desktop that starts, and the watcher of one that died,
/// make every window solid that a desktop left clear (<see cref="RestoreLeft"/>).
/// It follows the Windows animation setting, as the Bar's own motion does.
/// </summary>
public sealed class WindowFade : IDisposable
{
    /// <summary>The property a window carries while Winoma holds it clear or fades it.</summary>
    public const string Property = "Winoma.Fade";

    // How long the thread rests between two looks at windows that only wait, in milliseconds. A show wakes it at once.
    private const int WaitPace = 15;

    private readonly object _gate = new();
    private readonly WindowFades _fades = new();
    private readonly Dictionary<long, Action> _closes = [];

    // The windows being made clear right now, each on a thread that may be waiting for the window to answer.
    private readonly HashSet<nint> _veiling = [];
    private readonly AutoResetEvent _wake = new(false);
    private Thread? _thread;
    private bool _disposed;

    /// <summary>
    /// A program made <paramref name="hwnd"/> and has not shown it. It is made clear, so its first frame is the first of its fade.
    /// A window that is already layered is its program's own to draw, and one that refuses the change is left alone.
    /// Runs on the thread that watches window events.
    /// </summary>
    /// <param name="hwnd">A top-level window that is not shown yet.</param>
    public void Made(nint hwnd)
    {
        if (hwnd == 0 || Native.IsWindowVisible(hwnd) || !Enabled())
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed || _fades.Holds(hwnd) || !_veiling.Add(hwnd))
            {
                return;
            }
        }

        // The style change waits on the window's own thread, which is busy starting its program. It waits on a thread of its own,
        // so the thread that hears every window event is never held by one program.
        Task.Run(() => Veil(hwnd)).Forget("Tiling", "veil a new window");
    }

    private void Veil(nint hwnd)
    {
        var clear = Layer(hwnd, 0, unshown: true);
        lock (_gate)
        {
            _veiling.Remove(hwnd);
            if (clear && !_disposed)
            {
                Log.Debug("Tiling", "window " + hwnd + " is clear until it is shown");
                _fades.Made(hwnd, Environment.TickCount64);
                Start();
                return;
            }
        }

        if (clear)
        {
            Unlayer(hwnd);
        }
    }

    /// <summary>A window was shown or uncloaked. One that waited clear starts its fade now, and not at the thread's next look.</summary>
    /// <param name="hwnd">The window.</param>
    public void Shown(nint hwnd)
    {
        lock (_gate)
        {
            if (!_fades.Holds(hwnd))
            {
                return;
            }
        }

        _wake.Set();
    }

    /// <summary>
    /// Fades <paramref name="hwnd"/> out, and runs <paramref name="close"/> when it is clear. A window that then stays open is made solid again.
    /// </summary>
    /// <param name="hwnd">A window on the glass.</param>
    /// <param name="close">Asks the window to close. Runs on the fade's thread.</param>
    /// <returns>False when nothing fades: the caller asks the window to close itself.</returns>
    public bool Close(nint hwnd, Action close)
    {
        ArgumentNullException.ThrowIfNull(close);
        // The style change waits on the window's own thread. One that has stopped answering closes with no fade.
        if (hwnd == 0 || !Native.IsWindowVisible(hwnd) || Native.IsHungAppWindow(hwnd) || !Enabled())
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            // One that is still fading in is layered already, and fades out from solid.
            if (!_fades.Holds(hwnd) && !Layer(hwnd, 255, unshown: false))
            {
                return false;
            }

            Native.SetLayeredWindowAttributes(hwnd, 0, 255, Native.LwaAlpha);
            Log.Debug("Tiling", "window " + hwnd + " fades out before it is asked to close");
            _fades.Close(hwnd, Environment.TickCount64);
            _closes[hwnd] = close;
            Start();
        }

        _wake.Set();
        return true;
    }

    /// <summary>
    /// Makes every window solid that a desktop left clear or fading. A desktop that starts calls this, and so does the watcher
    /// of a desktop that died: a window left clear is on the glass and cannot be seen.
    /// </summary>
    /// <returns>How many windows were made solid.</returns>
    public static int RestoreLeft()
    {
        var found = new List<nint>();
        Native.EnumWindows(
            (hwnd, _) =>
            {
                if (Native.GetProp(hwnd, Property) != 0)
                {
                    found.Add(hwnd);
                }

                return true;
            },
            0);
        foreach (var hwnd in found)
        {
            Unlayer(hwnd);
        }

        if (found.Count > 0)
        {
            Log.Information("Tiling", found.Count + " windows a desktop left clear are solid again");
        }

        return found.Count;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _wake.Set();
        RestoreLeft();
    }

    /// <summary>True while Windows animation effects are on.</summary>
    private static bool Enabled()
    {
        var on = 1;
        return !Native.SystemParametersInfo(Native.SpiGetClientAreaAnimation, 0, ref on, 0) || on != 0;
    }

    private void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "winoma-window-fade" };
        _thread.Start();
    }

    private void Run()
    {
        var frames = new List<FadeFrame>();
        while (true)
        {
            bool moving;
            lock (_gate)
            {
                if (_disposed || !_fades.Any)
                {
                    _thread = null;
                    _closes.Clear();
                    return;
                }

                frames.Clear();
                frames.AddRange(_fades.Step(Environment.TickCount64, Look));
                moving = _fades.Moving;
            }

            foreach (var frame in frames)
            {
                Draw(frame);
            }

            if (moving)
            {
                // One step for each frame the compositor draws.
                if (Native.DwmFlush() != 0)
                {
                    Thread.Sleep(4);
                }
            }
            else
            {
                _wake.WaitOne(WaitPace);
            }
        }
    }

    private void Draw(FadeFrame frame)
    {
        var hwnd = (nint)frame.Id;
        switch (frame.End)
        {
            case FadeEnd.Solid:
                Solidify(hwnd);
                lock (_gate)
                {
                    _closes.Remove(frame.Id);
                }

                break;
            case FadeEnd.AskToClose:
                Native.SetLayeredWindowAttributes(hwnd, 0, 0, Native.LwaAlpha);
                Action? close;
                lock (_gate)
                {
                    _closes.Remove(frame.Id, out close);
                }

                try
                {
                    close?.Invoke();
                }
                catch (Exception ex)
                {
                    Log.Warning("Tiling", "closing window " + frame.Id + " after its fade failed", ex);
                }

                break;
            default:
                Native.SetLayeredWindowAttributes(hwnd, 0, frame.Alpha, Native.LwaAlpha);
                break;
        }
    }

    private static FadeLook Look(long id)
    {
        var hwnd = (nint)id;
        if (!Native.IsWindow(hwnd))
        {
            return FadeLook.Gone;
        }

        // A window Windows or Winoma keeps cloaked is shown and not on the glass. Its fade waits for the cloak to come off.
        if (!Native.IsWindowVisible(hwnd)
            || (Native.DwmGetWindowAttribute(hwnd, Native.DwmwaCloaked, out var cloak, sizeof(int)) == 0 && cloak != 0))
        {
            return FadeLook.Hidden;
        }

        return Native.IsWindowEnabled(hwnd) ? FadeLook.Shown : FadeLook.Blocked;
    }

    /// <summary>Makes <paramref name="hwnd"/> a layered window at <paramref name="alpha"/>, and marks it. False when it is layered already or refuses.</summary>
    private static bool Layer(nint hwnd, byte alpha, bool unshown)
    {
        var style = (long)Native.GetWindowLongPtr(hwnd, Native.GwlExStyle);
        if ((style & Native.WsExLayered) != 0)
        {
            return false;
        }

        // The mark goes on first: a desktop that dies between the two leaves a mark on a solid window, and never a clear window without one.
        if (!Native.SetProp(hwnd, Property, 1))
        {
            return false;
        }

        Native.SetWindowLongPtr(hwnd, Native.GwlExStyle, (nint)(style | Native.WsExLayered));

        // The change waited for the window's thread. A program that showed its window meanwhile has drawn it solid already,
        // and making it clear now would blink it out.
        if (((long)Native.GetWindowLongPtr(hwnd, Native.GwlExStyle) & Native.WsExLayered) == 0
            || (unshown && Native.IsWindowVisible(hwnd))
            || !Native.SetLayeredWindowAttributes(hwnd, 0, alpha, Native.LwaAlpha))
        {
            Unlayer(hwnd);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Makes <paramref name="hwnd"/> solid at once, and then an ordinary window again without the mark. Taking the style off waits on
    /// the window's own thread, so that part runs on a thread of its own, and the fades of other windows go on.
    /// </summary>
    private static void Solidify(nint hwnd)
    {
        Native.SetLayeredWindowAttributes(hwnd, 0, 255, Native.LwaAlpha);
        Task.Run(() => Unlayer(hwnd)).Forget("Tiling", "end a fade");
    }

    /// <summary>Makes <paramref name="hwnd"/> solid and an ordinary window again, and takes the mark off.</summary>
    private static void Unlayer(nint hwnd)
    {
        if (!Native.IsWindow(hwnd))
        {
            return;
        }

        var style = (long)Native.GetWindowLongPtr(hwnd, Native.GwlExStyle);
        if ((style & Native.WsExLayered) != 0)
        {
            Native.SetLayeredWindowAttributes(hwnd, 0, 255, Native.LwaAlpha);
            Native.SetWindowLongPtr(hwnd, Native.GwlExStyle, (nint)(style & ~Native.WsExLayered));
        }

        Native.RemoveProp(hwnd, Property);
    }
}
