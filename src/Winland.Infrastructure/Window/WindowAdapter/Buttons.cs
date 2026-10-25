using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

// Winland has no minimize: a client's request to minimize is ignored. A client's request to maximize
// is maximize mode 1. Windows carries both out before it tells anyone, so they are undone here.
public sealed partial class WindowAdapter
{
    /// <summary>Brings back a managed window the system just minimized.</summary>
    internal void UndoMinimize(nint handle)
    {
        var hwnd = new HWND(handle);
        if (_core.Manages(new WindowId(handle)) && PInvoke.IsIconic(hwnd))
            _ = PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
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
        var outcome = _core.Handle(appeared);
        NoteManaged(window, outcome, appeared);
        Apply(outcome);
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

        var outcome = _core.Handle(new WindowMaximizeRequested(window));
        Apply(outcome);
        _settling[window] = Environment.TickCount64 + SettleMilliseconds;
        return outcome;
    }

    // Electron windows restore a size of their own a moment after they leave the maximized state, which undoes
    // the placement made just before. For a short while after a maximize request, a frame that is not where
    // the Layout holds the window is put back.
    private void PutBackIfDrifted(HWND hwnd, WindowId window)
    {
        if (!_settling.TryGetValue(window, out var until))
            return;

        if (Environment.TickCount64 > until)
        {
            _ = _settling.TryRemove(window, out _);
            return;
        }

        if (_moving == window || _sizing == window || PInvoke.IsIconic(hwnd) || !TryVisible(window, out var visible))
            return;

        Apply(_core.Handle(new WindowDrifted(window, visible)));
    }
}
