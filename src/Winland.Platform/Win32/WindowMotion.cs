
namespace Winland.Platform.Win32;

/// <summary>
/// Reports that a top-level window moved or resized, or that the foreground changed.
/// A video that goes full screen only moves its window, so this is how the bar learns to step behind it without polling.
/// The events arrive out of context on this class's own thread, so a burst of moves never waits on the bar or on the keyboard hook.
/// </summary>
public sealed class WindowMotion : IDisposable
{
    private const uint EventLocationChange = 0x800B;
    private const uint EventNameChange = 0x800C;
    private const uint SkipOwnProcess = 0x0002;
    private const int ObjectCursor = -9;
    private const long Transparent = 0x20;
    private readonly Action _moved;
    private readonly Action<nint>? _resized;
    private readonly Action<nint, nint>? _childMoved;
    private readonly Action<nint>? _renamed;
    private readonly Action? _pointerMoved;
    private readonly Action<nint>? _foreground;
    private readonly Native.WinEventProc _callback;
    private readonly Native.WinEventProc _ownCallback;
    private readonly Thread _thread;
    private uint _threadId;
    private int _disposed;

    /// <summary>Starts watching. <paramref name="moved"/> runs on the watch thread and must only signal and return.</summary>
    /// <param name="moved">Raised for a top-level move or resize, and for a foreground change.</param>
    /// <param name="resized">
    /// Raised first with the window, for a top-level move or resize. It may move that window back at once, ahead of the next frame.
    /// </param>
    /// <param name="childMoved">Raised with the top-level window and the child when a child window moves or resizes. It must return at once.</param>
    /// <param name="renamed">Raised with a top-level window whose title changed. It must return at once. It does not raise <paramref name="moved"/>.</param>
    /// <param name="pointerMoved">
    /// Raised when the pointer moved on screen, by the mouse or by a program, over any window, Winoma's own included. It must return at once.
    /// The mouse hook does not hear a move over an elevated window. It does not raise <paramref name="moved"/>.
    /// </param>
    /// <param name="foreground">
    /// Raised first with the window that took the foreground, and once with the window in front when the watch starts. It must return at once.
    /// </param>
    public WindowMotion(Action moved, Action<nint>? resized = null, Action<nint, nint>? childMoved = null, Action<nint>? renamed = null, Action? pointerMoved = null, Action<nint>? foreground = null)
    {
        _pointerMoved = pointerMoved;
        _foreground = foreground;
        ArgumentNullException.ThrowIfNull(moved);
        _moved = moved;
        _resized = resized;
        _childMoved = childMoved;
        _renamed = renamed;
        _callback = OnEvent;
        _ownCallback = OnOwnEvent;
        // An app that fills the monitor itself is put back from here, so this thread wakes ahead of ordinary work.
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "winoma-window-motion",
            Priority = resized is null ? ThreadPriority.BelowNormal : ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

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
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var location = Native.SetWinEventHook(EventLocationChange, EventLocationChange, 0, _callback, 0, 0, SkipOwnProcess);
        var foreground = Native.SetWinEventHook(Native.EventSystemForeground, Native.EventSystemForeground, 0, _callback, 0, 0, SkipOwnProcess);
        var name = _renamed is null ? 0 : Native.SetWinEventHook(EventNameChange, EventNameChange, 0, _callback, 0, 0, SkipOwnProcess);
        if (location == 0 || foreground == 0)
        {
            Log.Warning("Bar", "could not watch window moves; the bar loop still checks every five seconds");
        }

        // Windows reports a cursor move for the thread whose window is under the pointer.
        // Over the bar or a panel that thread is Winoma's, and the hooks above skip Winoma's own events.
        var ownCursor = _pointerMoved is null
            ? 0
            : Native.SetWinEventHook(EventLocationChange, EventLocationChange, 0, _ownCallback, (uint)Environment.ProcessId, 0, 0);

        // No event names the window that was already in front.
        InFront(Native.GetForegroundWindow());

        while (Native.GetMessage(out var message, 0, 0, 0))
        {
            Native.TranslateMessage(ref message);
            Native.DispatchMessage(ref message);
        }

        if (location != 0)
        {
            Native.UnhookWinEvent(location);
        }

        if (foreground != 0)
        {
            Native.UnhookWinEvent(foreground);
        }

        if (name != 0)
        {
            Native.UnhookWinEvent(name);
        }

        if (ownCursor != 0)
        {
            Native.UnhookWinEvent(ownCursor);
        }
    }

    /// <summary>Winoma's own windows report here. Only the cursor is taken: the bar and the panels are not tiles and never cover the bar.</summary>
    private void OnOwnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (idObject == ObjectCursor)
        {
            _pointerMoved?.Invoke();
        }
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (eventType == EventNameChange)
        {
            // Controls and the cursor report names here too. Only a top-level window's title can change what is tiled.
            if (idObject == 0 && idChild == 0 && hwnd != 0 && Native.GetAncestor(hwnd, Native.GaRoot) == hwnd)
            {
                _renamed?.Invoke(hwnd);
            }

            return;
        }

        if (eventType == EventLocationChange)
        {
            if (idObject == ObjectCursor)
            {
                _pointerMoved?.Invoke();
                return;
            }

            // Carets and child controls report here too. Only a top-level window moving can cover the bar.
            if (idObject != 0 || idChild != 0 || hwnd == 0)
            {
                return;
            }

            var root = Native.GetAncestor(hwnd, Native.GaRoot);
            if (root != hwnd)
            {
                // An app can lay its content out again after Winoma has, such as a terminal leaving its own fullscreen.
                if (root != 0 && _childMoved is { } child)
                {
                    try
                    {
                        child(root, hwnd);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Tiling", "could not align a tile's content", ex);
                    }
                }

                return;
            }

            // A tooltip that follows the pointer and a candidate list that follows the caret move all day.
            // Ask that before holding a tile: those windows are not tiles, and the hold reads style and scans the placement list.
            // A window that drops its caption to go fullscreen stays visible and is not a tool window, so it still reaches the hold.
            if (!Native.IsWindowVisible(hwnd)
                || ((long)Native.GetWindowLongPtr(hwnd, Native.GwlExStyle) & (Native.WsExToolWindow | Native.WsExNoActivate | Transparent)) != 0)
            {
                return;
            }

            try
            {
                _resized?.Invoke(hwnd);
            }
            catch (Exception ex)
            {
                Log.Error("Tiling", "could not hold a fullscreen app on its tile", ex);
            }
        }

        if (eventType == Native.EventSystemForeground)
        {
            InFront(hwnd);
        }

        _moved();
    }

    private void InFront(nint hwnd)
    {
        if (hwnd == 0 || _foreground is not { } foreground)
        {
            return;
        }

        try
        {
            foreground(hwnd);
        }
        catch (Exception ex)
        {
            Log.Error("Bar", "could not follow the window in front", ex);
        }
    }
}
