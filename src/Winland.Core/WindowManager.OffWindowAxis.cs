namespace Winland.Core;

/// <summary>A wheel turn over the ring around a window: the window, and the point inside it nearest the pointer.</summary>
public readonly record struct OffWindowScroll(WindowId Window, Point Inside);

/// <summary>The scroll factors a window sets for itself, each null when it sets none.</summary>
public readonly record struct ScrollOverride(double? Mouse, double? Touchpad)
{
    /// <summary>The window's factor for this wheel, or 1 when the window does not set one.</summary>
    public double Factor(bool touchpad)
    {
        var factor = touchpad ? Touchpad : Mouse;
        return factor ?? 1;
    }
}

public sealed partial class WindowManager
{
    /// <summary>
    /// <c>input:off_window_axis_events</c>: 0 drops a wheel turn over the ring around a window, 1 and 2 send it to the
    /// window at the nearest point inside it, and 3 also moves the cursor there. The mouse hook reads it with no lock.
    /// </summary>
    public int OffWindowAxisMode => _input.OffWindowAxisEvents;

    /// <summary>
    /// The window a wheel turn at <paramref name="pointer"/> is meant for when the pointer is not inside it but within
    /// its ring (the border, and with <c>resize_on_border</c> the grab area around it), or null when the pointer is inside
    /// a window or over none. It runs on the mouse hook for every wheel turn, so it never waits: with a ring of no size
    /// or the core busy it answers null at once.
    /// </summary>
    public OffWindowScroll? OffWindowTarget(Point pointer)
    {
        if (!_gate.TryEnter())
            return null;

        try
        {
            var options = ActiveOptions();
            var reach = BorderPointer.AxisReach(options);
            if (reach <= 0 || !TryRingTarget(pointer, options, reach, out var window, out var bounds, out _, out _))
                return null;

            return new OffWindowScroll(window, BorderPointer.Clamp(bounds, pointer));
        }
        finally
        {
            _gate.Exit();
        }
    }

    /// <summary>
    /// The <c>scroll_mouse</c> and <c>scroll_touchpad</c> of the window a wheel turn at <paramref name="pointer"/> goes to:
    /// the window under it, or the one whose ring it is in. Each is null when that window does not set it, or when the
    /// pointer is over no window. It runs on the mouse hook for every wheel turn, so with the core busy it answers none
    /// at once.
    /// </summary>
    public ScrollOverride ScrollOverrideAt(Point pointer)
    {
        if (!_gate.TryEnter())
            return default;

        try
        {
            var window = WindowUnder(pointer, shrink: false);
            if (window is null)
            {
                var options = ActiveOptions();
                var reach = BorderPointer.AxisReach(options);
                if (reach > 0 && TryRingTarget(pointer, options, reach, out var ringed, out _, out _, out _))
                    window = ringed;
            }

            return window is WindowId found
                ? new ScrollOverride(OwnProp(found, "scroll_mouse") as float?, OwnProp(found, "scroll_touchpad") as float?)
                : default;
        }
        finally
        {
            _gate.Exit();
        }
    }
}
