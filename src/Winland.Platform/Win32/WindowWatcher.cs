namespace Winland.Platform.Win32;

/// <summary>
/// Asks for a retile as soon as a window is shown, hidden, destroyed, or cloaked, and when a window Winoma keeps hidden takes the keyboard.
/// The events are for every window object in every program, so nearly all of them are dropped here.
/// They arrive on the thread that made the watcher, or on the watcher's own thread when it was asked for one.
/// </summary>
public sealed class WindowWatcher : IDisposable
{
    private const uint SkipOwnProcess = 0x0002;
    private const uint EventCreate = 0x8000;
    private const uint EventDestroy = 0x8001;
    private const uint EventForeground = 0x0003;
    private readonly Native.WinEventProc _callback;
    private readonly Action _changed;
    private readonly Func<nint, bool>? _known;
    private readonly Func<nint, bool>? _counts;
    private readonly Action<nint>? _made;
    private readonly Action<nint>? _shown;
    private const uint EventShow = 0x8002;
    private const uint EventUncloaked = 0x8018;
    private readonly Thread? _thread;
    private readonly ManualResetEventSlim? _ready;
    private uint _threadId;
    private int _disposed;
    private nint _hook;
    private nint _cloak;
    private nint _foreground;

    /// <summary>
    /// Starts listening. <paramref name="changed"/> is raised for top-level window show, hide, destroy, cloak, and uncloak,
    /// and when the keyboard moves to a window this desktop has hidden.
    /// </summary>
    /// <param name="changed">Raised for each of those events.</param>
    /// <param name="known">Told of a destroyed window, and says whether it was one that counted. Null takes every destroy.</param>
    /// <param name="counts">
    /// Asked of a top-level window that was shown, hidden, cloaked, or uncloaked. False drops the event: a tooltip, a menu,
    /// or a popup changes neither the tiling nor the bar. Null takes every one.
    /// </param>
    /// <param name="ownThread">
    /// True to take the events on a thread of the watcher's own, so the dropped ones never wake the caller's thread.
    /// Every callback then runs on that thread, and must be safe there. False takes them on the calling thread, which must pump messages.
    /// </param>
    /// <param name="made">
    /// Told of a top-level window a program just made that it may show as its window (<see cref="WindowGateway.MayBecomeTile"/>),
    /// before it is shown. Null does not listen for windows being made.
    /// </param>
    /// <param name="shown">
    /// Told of every top-level window that was shown or uncloaked, before <paramref name="counts"/> is asked: a window kept clear until it is shown
    /// starts its fade here. It must return at once.
    /// </param>
    public WindowWatcher(Action changed, Func<nint, bool>? known = null, Func<nint, bool>? counts = null, bool ownThread = false, Action<nint>? made = null, Action<nint>? shown = null)
    {
        _changed = changed;
        _known = known;
        _counts = counts;
        _made = made;
        _shown = shown;
        _callback = OnEvent;
        if (!ownThread)
        {
            Hook();
            return;
        }

        _ready = new ManualResetEventSlim(false);
        // A window that waits clear starts its fade from here, and a closed window's tile is given up from here, so it runs at the usual priority.
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "winoma-window-watch",
        };
        _thread.Start();
        _ready.Wait();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_thread is null)
        {
            Unhook();
            return;
        }

        // A hook is taken out by the thread that put it in.
        var thread = _threadId;
        if (thread != 0)
        {
            Native.PostThreadMessage(thread, Native.WmQuit, 0, 0);
        }
    }

    private void Run()
    {
        _threadId = Native.GetCurrentThreadId();
        Native.PeekMessage(out _, 0, 0, 0, 0);
        Hook();
        _ready!.Set();
        if (Volatile.Read(ref _disposed) == 0)
        {
            while (Native.GetMessage(out var message, 0, 0, 0))
            {
                Native.TranslateMessage(ref message);
                Native.DispatchMessage(ref message);
            }
        }

        Unhook();
    }

    private void Hook()
    {
        _hook = Native.SetWinEventHook(_made is null ? EventDestroy : EventCreate, 0x8003, 0, _callback, 0, 0, SkipOwnProcess);
        _cloak = Native.SetWinEventHook(0x8017, 0x8018, 0, _callback, 0, 0, SkipOwnProcess);
        if (_cloak == 0)
        {
            Log.Warning("Bar", "could not watch window cloak");
        }

        // Every move of the keyboard, Winoma's own too: the trail has to name the window that had it before (ForegroundTrail).
        _foreground = Native.SetWinEventHook(EventForeground, EventForeground, 0, _callback, 0, 0, 0);
        if (_foreground == 0)
        {
            Log.Warning("Bar", "could not watch the foreground window");
        }
    }

    private void Unhook()
    {
        if (_hook != 0)
        {
            Native.UnhookWinEvent(_hook);
            _hook = 0;
        }

        if (_cloak != 0)
        {
            Native.UnhookWinEvent(_cloak);
            _cloak = 0;
        }

        if (_foreground != 0)
        {
            Native.UnhookWinEvent(_foreground);
            _foreground = 0;
        }
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (idObject != 0 || idChild != 0)
        {
            return;
        }

        if (eventType == EventForeground)
        {
            // A window Winoma keeps hidden has the keyboard: it showed itself, or Windows handed the keyboard on when another window went away.
            // The shell tells the two apart, and follows it or takes the keyboard back. Any other move changes nothing Winoma keeps.
            ForegroundTrail.Note(hwnd);
            if (hwnd != 0 && HiddenLedger.Windows.Way(hwnd) is not null)
            {
                _changed();
            }

            return;
        }

        if (eventType == EventCreate)
        {
            // Nothing is on the glass yet, so nothing is retiled. The window is only moved to where it will be shown.
            if (_made is { } made && WindowGateway.MayBecomeTile(hwnd))
            {
                made(hwnd);
            }

            return;
        }

        // The window object is also every control inside a window, and programs show, hide, and destroy those all day.
        // Each one would drop the census and run a whole turn of the desktop. A destroyed window can no longer be asked,
        // so it counts only when the last census saw it.
        if (eventType == EventDestroy)
        {
            if (_known is { } known && !known(hwnd))
            {
                return;
            }
        }
        else if (hwnd != 0)
        {
            if (Native.GetAncestor(hwnd, Native.GaRoot) != hwnd)
            {
                return;
            }

            if (eventType is EventShow or EventUncloaked)
            {
                _shown?.Invoke(hwnd);
            }

            // A tooltip, a menu, and a popup are top-level too, and one opens and closes with every hover.
            if (_counts is { } counts && !counts(hwnd))
            {
                return;
            }
        }

        WindowSignals.Bump();
        _changed();
    }
}
