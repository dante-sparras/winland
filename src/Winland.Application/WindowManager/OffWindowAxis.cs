namespace Winland.Application;

/// <summary>A wheel turn over the ring around a window: the window, and the point inside it nearest the pointer.</summary>
public readonly record struct OffWindowScroll(WindowId Window, Point Inside);

public sealed partial class WindowManager
{
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

}
