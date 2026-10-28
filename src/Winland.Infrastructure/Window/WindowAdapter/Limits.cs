using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winland.Infrastructure;

/// <summary>A window's own minimum and maximum size, read from the window and reported to the core when they change.</summary>
public sealed partial class WindowAdapter : IDisposable
{
    private void RereadLimits(HWND hwnd, WindowId window)
    {
        lock (_limitGate)
        {
            if (!_limits.ContainsKey(window))
                return;
        }

        if (!PInvoke.IsWindow(hwnd))
            return;

        var insets = InsetsFor(window, _desktop.Dpi(window));
        if (!WindowLimits.TryRead(hwnd, insets, out var min, out var max))
            return;

        lock (_limitGate)
        {
            if (!_limits.TryGetValue(window, out var prior) || (prior.Min == min && prior.Max == max))
                return;

            _limits[window] = (min, max);
        }

        Apply(_core.Handle(new WindowLimitsChanged(window, min, max)));
    }

    // WM_GETMINMAXINFO can take up to its timeout. A Client's limits are kept up to date as it moves, and a
    // window that is described again and again, as its title changes, is asked once in a while.
    private (Size? Min, Size? Max) LimitsOf(HWND hwnd, WindowId window)
    {
        var now = Environment.TickCount64;
        lock (_limitGate)
        {
            if (_limits.TryGetValue(window, out var kept))
                return kept;

            if (_probed.TryGetValue(window, out var probed) && now - probed.At < ProbeMilliseconds)
                return (probed.Min, probed.Max);
        }

        Size? min = null;
        Size? max = null;
        var insets = InsetsFor(window, _desktop.Dpi(window));
        if (WindowLimits.TryRead(hwnd, insets, out var readMin, out var readMax))
        {
            min = readMin;
            max = readMax;
        }

        lock (_limitGate)
        {
            if (_probed.Count >= ProbeLimit)
                _probed.Clear();

            _probed[window] = (min, max, now);
        }

        return (min, max);
    }

    private void ForgetProbe(WindowId window)
    {
        lock (_limitGate)
            _probed.Remove(window);
    }

    private void ForgetLimits(WindowId window)
    {
        lock (_limitGate)
            _limits.Remove(window);
    }
}
