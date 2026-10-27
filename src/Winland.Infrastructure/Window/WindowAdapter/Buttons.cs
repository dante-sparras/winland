using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

// Winland has no minimize: a client's request to minimize is ignored. A client's request to maximize
// is maximize mode 1. Windows carries both out before it tells anyone, so they are undone here. A client
// that covers its monitor by itself asked for fullscreen mode 2.
public sealed partial class WindowAdapter
{
    /// <summary>Brings back a managed window the system just minimized.</summary>
    internal void UndoMinimize(nint handle)
    {
        var hwnd = new HWND(handle);
        // Put back without activating it, so Win+D or a game that minimizes itself when it loses focus does not
        // take the focus as each window comes back.
        if (_core.Manages(new WindowId(handle)) && PInvoke.IsIconic(hwnd))
            _ = PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
    }

    private void AdoptShown(HWND hwnd, WindowId window)
    {
        if (!TryDescribe(hwnd, window, out var appeared))
            return;

        // A managed window described again may name a different class, as a Store frame does once its hosted
        // window appears. The class it had when it was adopted stays. Showing it again still follows.
        if (_core.Manages(window))
            Apply(_core.Handle(new WindowClassChanged(window, appeared.Traits.Class)));

        // A window that opens maximized asked for it before it mapped, which the core ignores. A window that was
        // maximized before Winland started is adopted elsewhere, and stays maximized.
        appeared = appeared with { ShownMaximized = PInvoke.IsZoomed(hwnd), Opened = true };
        var opened = !_core.Manages(window);
        var outcome = _core.Handle(appeared);
        NoteManaged(window, outcome, appeared);
        Apply(outcome);

        // VLC sizes its window to the video a moment after it opens. A tile is put back for a short while, as after a maximize.
        if (opened && outcome.State.Clients.Any(client => client.Window == window && !client.Floating))
            _settling[window] = Environment.TickCount64 + SettleMilliseconds;
    }

    /// <summary>Takes a managed window out of the system's maximized state and asks the core to maximize it its own way.</summary>
    internal Outcome RequestMaximize(nint handle)
    {
        var window = new WindowId(handle);
        if (!_core.Manages(window))
            return _core.Handle(new WindowMaximizeRequested(window));

        var hwnd = new HWND(handle);
        if (PInvoke.IsZoomed(hwnd))
            _ = PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_RESTORE);

        // A window that maximizes itself again just after Winland restored it, with no click or key from the user,
        // would flip its maximize mode back and forth with every round. It is only put back where it was.
        if (_settling.TryGetValue(window, out var until) && Environment.TickCount64 < until
            && !InputClock.Recent(unchecked((uint)Environment.TickCount)) && TryVisible(window, out var landed))
        {
            var drifted = _core.Handle(new WindowDrifted(window, landed));
            Apply(drifted);
            return drifted;
        }

        var outcome = _core.Handle(new WindowMaximizeRequested(window));
        Apply(outcome);
        _settling[window] = Environment.TickCount64 + SettleMilliseconds;
        return outcome;
    }

    // A browser that shows a video fullscreen drops its frame and covers its monitor, and puts both back when the video
    // leaves. Each change is reported once. The window keeps its own frame: Winland would otherwise keep the frameless
    // style as the one to put back. A window Winland is moving is somewhere on the way, not where it asked to be.
    // Stryker disable once all : handles a window event, which needs a real window.
    private void ReportFullscreen(HWND hwnd, WindowId window)
    {
        if (!_core.Manages(window) || _motions.Running(window) || PInvoke.IsIconic(hwnd) || !TryVisible(window, out var visible))
            return;

        var monitor = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL);
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (monitor.IsNull || !PInvoke.GetMonitorInfo(monitor, ref info))
            return;

        var covers = visible == new Rectangle(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right - info.rcMonitor.left, info.rcMonitor.bottom - info.rcMonitor.top);
        if (covers == _ownFullscreen.ContainsKey(window))
            return;

        if (covers)
            _ownFullscreen[window] = 0;
        else
            _ownFullscreen.TryRemove(window, out _);

        Apply(_core.Handle(new WindowFullscreenRequested(window, covers)));
    }

    // Electron windows restore a size of their own a moment after they leave the maximized state, which undoes
    // the placement made just before. For a short while after a maximize request, or after a tile opens, a frame
    // that is not where the Layout holds the window is put back. A window on its way somewhere, or one that went
    // fullscreen by itself, is where it should be.
    private void PutBackIfDrifted(HWND hwnd, WindowId window)
    {
        if (!_settling.TryGetValue(window, out var until))
            return;

        if (Environment.TickCount64 > until)
        {
            _ = _settling.TryRemove(window, out _);
            return;
        }

        if (_moving == window || _sizing == window || _motions.Running(window) || _ownFullscreen.ContainsKey(window)
            || PInvoke.IsIconic(hwnd) || !TryVisible(window, out var visible))
            return;

        Apply(_core.Handle(new WindowDrifted(window, visible)));
    }
}
