namespace Winland;

/// <summary>
/// Turns one touch screen's frames of fingers into <see cref="TouchDown"/>, <see cref="TouchMoved"/> and
/// <see cref="TouchUp"/>. A finger that comes down while the screen's input is not wanted is never reported, but one
/// already reported is followed until it lifts. Positions run from 0 to 1 across the screen.
/// </summary>
public sealed class TouchScreen(long device)
{
    private readonly Dictionary<int, (double X, double Y)> _down = [];

    // Fingers that came down while the input was not wanted, until they lift.
    private readonly HashSet<int> _ignored = [];

    /// <summary>How many reported fingers are down.</summary>
    public int Count => _down.Count;

    /// <summary>Adds a frame of the fingers down, by contact id. <paramref name="wanted"/> lets new fingers be reported.</summary>
    public IReadOnlyList<Input> Frame(IReadOnlyDictionary<int, (double X, double Y)> down, bool wanted, Rectangle? display)
    {
        ArgumentNullException.ThrowIfNull(down);
        var inputs = new List<Input>();
        _ignored.RemoveWhere(id => !down.ContainsKey(id));
        foreach (var id in _down.Keys.ToArray())
        {
            if (down.ContainsKey(id))
                continue;

            _down.Remove(id);
            inputs.Add(new TouchUp(device, id));
        }

        foreach (var (id, point) in down)
        {
            if (_down.TryGetValue(id, out var before))
            {
                if (before != point)
                {
                    _down[id] = point;
                    inputs.Add(new TouchMoved(device, id, point.X, point.Y, display));
                }

                continue;
            }

            if (!wanted || _ignored.Contains(id))
            {
                _ignored.Add(id);
                continue;
            }

            _down[id] = point;
            inputs.Add(new TouchDown(device, id, point.X, point.Y, display));
        }

        return inputs;
    }

    /// <summary>Ends every reported finger as cancelled, when the screen goes away.</summary>
    public IReadOnlyList<Input> Cancel()
    {
        var inputs = _down.Keys.Select(id => (Input)new TouchUp(device, id, Cancelled: true)).ToList();
        _down.Clear();
        _ignored.Clear();
        return inputs;
    }

    /// <summary>
    /// Turns a point on the touch screen, from 0 to 1 along its own axes, to the same point on its display, which Windows
    /// has turned clockwise by 90 degrees times <paramref name="quarterTurns"/>.
    /// </summary>
    public static (double X, double Y) Orient(double x, double y, int quarterTurns) => (((quarterTurns % 4) + 4) % 4) switch
    {
        1 => (y, 1 - x),
        2 => (1 - x, 1 - y),
        3 => (1 - y, x),
        _ => (x, y),
    };
}
