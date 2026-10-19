namespace Winland.Windows;

/// <summary>One finger in a precision touchpad report, in millimetres from the pad's origin.</summary>
internal readonly record struct TouchContact(int Id, bool Down, double X, double Y);

/// <summary>
/// Turns precision touchpad reports into swipe inputs. Three or more fingers down together start a swipe, the
/// mean movement of the fingers that stayed down is its update, and a finger lifted ends it. More fingers ending
/// the swipe cancel it and start another. Once a finger lifts, a new swipe waits until every finger is up.
/// </summary>
internal sealed class TouchpadSwipe
{
    // About 300 per inch, close to the pixel distance a touchpad swipe travels elsewhere.
    internal const double PixelsPerMillimetre = 300 / 25.4;

    internal const int MinimumFingers = 3;

    private readonly List<TouchContact> _pending = [];
    private readonly Dictionary<int, (double X, double Y)> _last = [];
    private int _expected;
    private int _fingers;
    private bool _waitForLift;

    /// <summary>
    /// Adds one report. A report with a contact count opens a frame of that many contacts, and later reports with a
    /// count of 0 carry the rest of it. Returns the inputs a finished frame produced.
    /// </summary>
    internal IReadOnlyList<Input> Report(int contactCount, IReadOnlyList<TouchContact> contacts)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        if (contactCount > 0)
        {
            _expected = contactCount;
            _pending.Clear();
        }

        if (_expected == 0)
            return [];

        foreach (var contact in contacts)
        {
            if (_pending.Count >= _expected)
                break;

            _pending.Add(contact);
        }

        if (_pending.Count < _expected)
            return [];

        _expected = 0;
        return Frame(_pending);
    }

    private List<Input> Frame(List<TouchContact> contacts)
    {
        var inputs = new List<Input>(2);
        var down = new Dictionary<int, (double X, double Y)>();
        foreach (var contact in contacts)
        {
            if (contact.Down)
                down[contact.Id] = (contact.X, contact.Y);
        }

        if (_fingers > 0)
        {
            if (down.Count == _fingers)
            {
                if (TryMove(down, out var dx, out var dy))
                    inputs.Add(new SwipeUpdated(_fingers, dx, dy) { TimeMs = Environment.TickCount64 });
            }
            else if (down.Count > _fingers)
            {
                inputs.Add(new SwipeEnded(Cancelled: true) { TimeMs = Environment.TickCount64 });
                _fingers = down.Count;
                inputs.Add(new SwipeBegan(_fingers));
            }
            else
            {
                inputs.Add(new SwipeEnded { TimeMs = Environment.TickCount64 });
                _fingers = 0;
                _waitForLift = down.Count > 0;
            }
        }
        else if (_waitForLift)
        {
            _waitForLift = down.Count > 0;
        }
        else if (down.Count >= MinimumFingers)
        {
            _fingers = down.Count;
            inputs.Add(new SwipeBegan(_fingers));
        }

        _last.Clear();
        foreach (var (id, point) in down)
            _last[id] = point;

        return inputs;
    }

    private bool TryMove(Dictionary<int, (double X, double Y)> down, out double dx, out double dy)
    {
        dx = 0;
        dy = 0;
        var seen = 0;
        foreach (var (id, point) in down)
        {
            if (!_last.TryGetValue(id, out var before))
                continue;

            dx += point.X - before.X;
            dy += point.Y - before.Y;
            seen++;
        }

        if (seen == 0)
            return false;

        dx = dx / seen * PixelsPerMillimetre;
        dy = dy / seen * PixelsPerMillimetre;
        return dx != 0 || dy != 0;
    }
}
