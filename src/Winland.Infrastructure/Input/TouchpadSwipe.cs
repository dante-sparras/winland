namespace Winland.Infrastructure;

/// <summary>One finger in a precision touchpad report, in millimetres from the pad's origin.</summary>
internal readonly record struct TouchContact(int Id, bool Down, double X, double Y);

/// <summary>
/// Gathers a digitizer's reports into frames. A report with a contact count opens a frame of that many contacts, and
/// later reports with a count of 0 carry the rest of it, as precision touchpads report in hybrid mode.
/// </summary>
internal sealed class ContactFrame
{
    private readonly List<TouchContact> _pending = [];
    private int _expected;

    /// <summary>Adds one report. Returns the frame it finished, or null while the frame waits for more.</summary>
    internal IReadOnlyList<TouchContact>? Add(int contactCount, IReadOnlyList<TouchContact> contacts)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        if (contactCount > 0)
        {
            _expected = contactCount;
            _pending.Clear();
        }

        if (_expected == 0)
            return null;

        foreach (var contact in contacts)
        {
            if (_pending.Count >= _expected)
                break;

            _pending.Add(contact);
        }

        if (_pending.Count < _expected)
            return null;

        _expected = 0;
        return _pending;
    }
}

/// <summary>
/// Turns precision touchpad reports into swipe inputs. Fingers that come down together wait until they travel, which
/// is a swipe of three or more fingers. Two fingers that travel are a scroll and give nothing. The mean movement of
/// the fingers that stayed down is each update. A finger lifted ends the gesture, and more fingers end it cancelled
/// and start another. Once a finger lifts, a new gesture waits until every finger is up. Each finished frame is also
/// handed to <see cref="Framed"/>.
/// </summary>
internal sealed class TouchpadSwipe
{
    // About 300 per inch, close to the pixel distance a touchpad swipe travels elsewhere.
    internal const double PixelsPerMillimetre = 300 / 25.4;

    internal const int MinimumFingers = 3;

    // How far the fingers' middle travels, in millimetres, before the swipe is known.
    internal const double SwipeStart = 1;

    private readonly ContactFrame _frames = new();
    private readonly Dictionary<int, (double X, double Y)> _last = [];
    private int _fingers;
    private bool _swiping;
    private (double X, double Y) _startCentre;
    private bool _waitForLift;

    /// <summary>Called with the fingers down in each finished frame, by contact id, in millimetres.</summary>
    internal Action<IReadOnlyDictionary<int, (double X, double Y)>>? Framed { get; set; }

    /// <summary>
    /// Adds one report. A report with a contact count opens a frame of that many contacts, and later reports with a
    /// count of 0 carry the rest of it. Returns the inputs a finished frame produced.
    /// </summary>
    internal IReadOnlyList<Input> Report(int contactCount, IReadOnlyList<TouchContact> contacts) =>
        _frames.Add(contactCount, contacts) is { } frame ? Frame(frame) : [];

    private List<Input> Frame(IReadOnlyList<TouchContact> contacts)
    {
        var inputs = new List<Input>(2);
        var down = new Dictionary<int, (double X, double Y)>();
        foreach (var contact in contacts)
        {
            if (contact.Down)
                down[contact.Id] = (contact.X, contact.Y);
        }

        Framed?.Invoke(down);
        if (_fingers > 0)
        {
            if (down.Count == _fingers)
                Step(down, inputs);
            else if (down.Count > _fingers)
            {
                End(inputs, cancelled: true);
                Start(down);
            }
            else
            {
                End(inputs, cancelled: false);
                _waitForLift = down.Count > 0;
            }
        }
        else if (_waitForLift)
        {
            _waitForLift = down.Count > 0;
        }
        else if (down.Count >= MinimumFingers)
        {
            Start(down);
        }

        _last.Clear();
        foreach (var (id, point) in down)
            _last[id] = point;

        return inputs;
    }

    private void Start(Dictionary<int, (double X, double Y)> down)
    {
        _fingers = down.Count;
        _swiping = false;
        _startCentre = Centre(down);
    }

    private void Step(Dictionary<int, (double X, double Y)> down, List<Input> inputs)
    {
        var centre = Centre(down);
        if (!_swiping)
        {
            var travel = Math.Sqrt(Math.Pow(centre.X - _startCentre.X, 2) + Math.Pow(centre.Y - _startCentre.Y, 2));
            if (travel < SwipeStart)
                return;

            // The first update carries the whole travel since the fingers came down.
            _swiping = true;
            inputs.Add(new SwipeBegan(_fingers));
            inputs.Add(new SwipeUpdated(_fingers, (centre.X - _startCentre.X) * PixelsPerMillimetre, (centre.Y - _startCentre.Y) * PixelsPerMillimetre) { TimeMs = Environment.TickCount64 });
            return;
        }

        if (TryMove(down, out var dx, out var dy))
            inputs.Add(new SwipeUpdated(_fingers, dx, dy) { TimeMs = Environment.TickCount64 });
    }

    private void End(List<Input> inputs, bool cancelled)
    {
        if (_swiping)
            inputs.Add(new SwipeEnded(cancelled) { TimeMs = Environment.TickCount64 });

        _fingers = 0;
        _swiping = false;
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

    private static (double X, double Y) Centre(Dictionary<int, (double X, double Y)> down)
    {
        double x = 0, y = 0;
        foreach (var point in down.Values)
        {
            x += point.X;
            y += point.Y;
        }

        return (x / down.Count, y / down.Count);
    }
}
