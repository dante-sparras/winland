namespace Winland;

/// <summary>
/// Finds a two-finger tap in precision touchpad reports, for <c>tap_button_map = lmr</c>: two fingers down, no more,
/// lifted within <see cref="TimeoutMs"/> of the first touching, and none of them moved <see cref="MoveMm"/>
/// millimetres. Two fingers that scroll, pinch or rest are no tap.
/// </summary>
public sealed class TouchpadTap
{
    /// <summary>How long a tap may last from the first finger down to the last one up.</summary>
    public const long TimeoutMs = 180;

    /// <summary>How far, in millimetres, a finger can move and still tap.</summary>
    public const double MoveMm = 1.5;

    private readonly Dictionary<int, (double X, double Y)> _start = [];
    private long _since;
    private int _most;
    private bool _moved;

    /// <summary>Adds a report of the fingers down. True when it ends a two-finger tap.</summary>
    public bool Frame(IReadOnlyDictionary<int, (double X, double Y)> down, long now)
    {
        ArgumentNullException.ThrowIfNull(down);
        if (down.Count == 0)
        {
            var tapped = _start.Count > 0 && _most == 2 && !_moved && now - _since <= TimeoutMs;
            _start.Clear();
            _most = 0;
            _moved = false;
            return tapped;
        }

        if (_start.Count == 0)
            _since = now;

        _most = Math.Max(_most, down.Count);
        foreach (var (id, point) in down)
        {
            if (!_start.TryGetValue(id, out var first))
            {
                _start[id] = point;
                continue;
            }

            var dx = point.X - first.X;
            var dy = point.Y - first.Y;
            if ((dx * dx) + (dy * dy) >= MoveMm * MoveMm)
                _moved = true;
        }

        return false;
    }
}
