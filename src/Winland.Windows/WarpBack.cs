namespace Winland.Windows;

/// <summary>
/// <c>cursor:warp_back_after_non_mouse_input</c>: once something other than the mouse has moved the cursor, the next
/// mouse move starts from where the mouse last left it, as in the pinned release. Winland's own warps, touch, a pen and
/// moves another program injects are not the mouse.
/// </summary>
public sealed class WarpBack
{
    private readonly Lock _gate = new();
    private bool _enabled;
    private bool _lastInputMouse = true;
    private Point? _lastMouse;

    public void Configure(bool enabled)
    {
        lock (_gate)
            _enabled = enabled;
    }

    /// <summary>Something other than the mouse moved the cursor.</summary>
    public void NonMouse()
    {
        lock (_gate)
            _lastInputMouse = false;
    }

    /// <summary>
    /// The mouse moves the cursor from <paramref name="from"/> to <paramref name="to"/>. Returns where the cursor goes
    /// instead: the point the mouse last left it at, moved by the same amount, or null when the move stands.
    /// </summary>
    public Point? Mouse(Point from, Point to)
    {
        lock (_gate)
        {
            Point? instead = null;
            if (_enabled && !_lastInputMouse && _lastMouse is Point back)
                instead = new Point(back.X + (to.X - from.X), back.Y + (to.Y - from.Y));

            _lastInputMouse = true;
            _lastMouse = instead ?? to;
            return instead;
        }
    }
}
