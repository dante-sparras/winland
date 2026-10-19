namespace Winland.Windows;

/// <summary>One finger in a precision touchpad report, in millimetres from the pad's origin.</summary>
internal readonly record struct TouchContact(int Id, bool Down, double X, double Y);

/// <summary>
/// Turns precision touchpad reports into swipe and pinch inputs. Fingers that come down together wait until they
/// either travel together, which is a swipe of three or more fingers, or spread apart or close, which is a pinch of two
/// or more. Two fingers that travel are a scroll and give nothing. The mean movement of the fingers that stayed down
/// is each update, and a pinch also reports how far they spread since it began. A finger lifted ends the gesture, and
/// more fingers end it cancelled and start another. Once a finger lifts, a new gesture waits until every finger is up.
/// Fingers of the count <see cref="DragFingers"/> names drag instead and give nothing, as libinput sends no gesture of
/// the count its three-finger drag takes. Each finished frame is also handed to <see cref="Framed"/>.
/// </summary>
internal sealed class TouchpadSwipe
{
    // About 300 per inch, close to the pixel distance a touchpad swipe travels elsewhere.
    internal const double PixelsPerMillimetre = 300 / 25.4;

    internal const int MinimumFingers = 3;

    internal const int MinimumPinchFingers = 2;

    // How far the fingers' middle travels, in millimetres, or how much their spread changes, before the kind is known.
    internal const double SwipeStart = 1;
    internal const double PinchStart = 0.08;

    private readonly List<TouchContact> _pending = [];
    private readonly Dictionary<int, (double X, double Y)> _last = [];
    private int _expected;
    private int _fingers;
    private Kind _kind;
    private (double X, double Y) _startCentre;
    private double _startSpread;
    private bool _waitForLift;

    private enum Kind
    {
        Undecided,
        Swipe,
        Pinch,
        Scroll,
        Drag,
    }

    /// <summary>The finger count <c>input:touchpad:drag_3fg</c> drags with, which is then no swipe or pinch. 0 for none.</summary>
    internal int DragFingers { get; set; }

    /// <summary>Called with the fingers down in each finished frame, by contact id, in millimetres.</summary>
    internal Action<IReadOnlyDictionary<int, (double X, double Y)>>? Framed { get; set; }

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
        else if (down.Count >= MinimumPinchFingers)
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
        _kind = down.Count == DragFingers ? Kind.Drag : Kind.Undecided;
        _startCentre = Centre(down);
        _startSpread = Spread(down, _startCentre);
    }

    private void Step(Dictionary<int, (double X, double Y)> down, List<Input> inputs)
    {
        var centre = Centre(down);
        var scale = _startSpread > 0 ? Spread(down, centre) / _startSpread : 1;
        if (_kind == Kind.Undecided)
        {
            var travel = Math.Sqrt(Math.Pow(centre.X - _startCentre.X, 2) + Math.Pow(centre.Y - _startCentre.Y, 2));
            if (Math.Abs(scale - 1) >= PinchStart)
            {
                _kind = Kind.Pinch;
                inputs.Add(new PinchBegan(_fingers));
                inputs.Add(new PinchUpdated(_fingers, (centre.X - _startCentre.X) * PixelsPerMillimetre, (centre.Y - _startCentre.Y) * PixelsPerMillimetre, scale) { TimeMs = Environment.TickCount64 });
                return;
            }

            if (travel < SwipeStart)
                return;

            if (_fingers < MinimumFingers)
            {
                _kind = Kind.Scroll;
                return;
            }

            // The first update carries the whole travel since the fingers came down.
            _kind = Kind.Swipe;
            inputs.Add(new SwipeBegan(_fingers));
            inputs.Add(new SwipeUpdated(_fingers, (centre.X - _startCentre.X) * PixelsPerMillimetre, (centre.Y - _startCentre.Y) * PixelsPerMillimetre) { TimeMs = Environment.TickCount64 });
            return;
        }

        if (_kind is Kind.Scroll or Kind.Drag)
            return;

        var moved = TryMove(down, out var dx, out var dy);
        if (_kind == Kind.Swipe)
        {
            if (moved)
                inputs.Add(new SwipeUpdated(_fingers, dx, dy) { TimeMs = Environment.TickCount64 });
            return;
        }

        // A pinch updates whenever its fingers move, even when only their spread changed.
        inputs.Add(new PinchUpdated(_fingers, dx, dy, scale) { TimeMs = Environment.TickCount64 });
    }

    private void End(List<Input> inputs, bool cancelled)
    {
        if (_kind == Kind.Swipe)
            inputs.Add(new SwipeEnded(cancelled) { TimeMs = Environment.TickCount64 });
        else if (_kind == Kind.Pinch)
            inputs.Add(new PinchEnded(cancelled) { TimeMs = Environment.TickCount64 });

        _fingers = 0;
        _kind = Kind.Undecided;
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

    // The fingers' mean distance from their middle.
    private static double Spread(Dictionary<int, (double X, double Y)> down, (double X, double Y) centre)
    {
        var total = 0.0;
        foreach (var point in down.Values)
            total += Math.Sqrt(Math.Pow(point.X - centre.X, 2) + Math.Pow(point.Y - centre.Y, 2));

        return total / down.Count;
    }
}
