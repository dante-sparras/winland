namespace Winland;

/// <summary>What the touchpad drag does with one report: press the button, move the cursor, release the button.</summary>
public readonly record struct TouchpadDragStep(bool Press, double Dx, double Dy, bool Release)
{
    public static TouchpadDragStep None { get; } = default;

    public bool Any => Press || Release || Dx != 0 || Dy != 0;
}

/// <summary>
/// <c>input:touchpad:drag_3fg</c>: three fingers, or four, that travel together hold the left button down and move the
/// cursor with them, as libinput's three-finger drag does. Lifting them keeps the button down for
/// <see cref="LiftMs"/>, so the fingers can be put down again to carry on. A finger more than that ends the drag at once,
/// and so does the time running out. Positions are in millimetres, and the cursor moves at
/// <see cref="PixelsPerMillimetre"/>.
/// </summary>
public sealed class TouchpadDrag
{
    /// <summary>About 300 per inch, as Winland counts a touchpad swipe.</summary>
    public const double PixelsPerMillimetre = 300 / 25.4;

    /// <summary>How far, in millimetres, the fingers' middle travels before the drag presses the button.</summary>
    public const double Start = 1;

    /// <summary>How long the button stays down after the fingers lift.</summary>
    public const long LiftMs = 700;

    private readonly Dictionary<int, (double X, double Y)> _last = [];
    private State _state;
    private (double X, double Y) _startCentre;
    private long _liftedAt;
    private int _fingers;

    private enum State
    {
        Idle,
        Pending,
        Dragging,
        Lifted,
    }

    /// <summary>Whether the button is held down.</summary>
    public bool Holding => _state is State.Dragging or State.Lifted;

    /// <summary>When the held button is let go unless fingers come back, or null when nothing waits.</summary>
    public long? ReleaseAt => _state == State.Lifted ? _liftedAt + LiftMs : null;

    /// <summary>Adds a report of the fingers down, with <paramref name="fingers"/> the count that drags, 0 for none.</summary>
    public TouchpadDragStep Frame(int fingers, IReadOnlyDictionary<int, (double X, double Y)> down, long now)
    {
        ArgumentNullException.ThrowIfNull(down);
        var step = Advance(fingers, down, now);
        _last.Clear();
        foreach (var (id, point) in down)
            _last[id] = point;

        return step;
    }

    /// <summary>Lets the button go once the fingers have been up for <see cref="LiftMs"/>.</summary>
    public TouchpadDragStep Expire(long now)
    {
        if (_state != State.Lifted || now < _liftedAt + LiftMs)
            return TouchpadDragStep.None;

        _state = State.Idle;
        return new TouchpadDragStep(false, 0, 0, Release: true);
    }

    private TouchpadDragStep Advance(int fingers, IReadOnlyDictionary<int, (double X, double Y)> down, long now)
    {
        if (fingers != _fingers)
        {
            var held = Holding;
            _fingers = fingers;
            _state = State.Idle;
            if (held)
                return new TouchpadDragStep(false, 0, 0, Release: true);
        }

        if (fingers == 0)
            return TouchpadDragStep.None;

        switch (_state)
        {
            case State.Idle:
                if (down.Count == fingers)
                {
                    _state = State.Pending;
                    _startCentre = Centre(down);
                }

                return TouchpadDragStep.None;

            case State.Pending:
                if (down.Count != fingers)
                {
                    _state = State.Idle;
                    return TouchpadDragStep.None;
                }

                var centre = Centre(down);
                var dx = centre.X - _startCentre.X;
                var dy = centre.Y - _startCentre.Y;
                if (Math.Sqrt((dx * dx) + (dy * dy)) < Start)
                    return TouchpadDragStep.None;

                // The press carries the whole travel since the fingers came down.
                _state = State.Dragging;
                return new TouchpadDragStep(true, dx * PixelsPerMillimetre, dy * PixelsPerMillimetre, false);

            case State.Dragging:
                if (down.Count == fingers)
                    return Move(down);

                if (down.Count > fingers)
                {
                    _state = State.Idle;
                    return new TouchpadDragStep(false, 0, 0, Release: true);
                }

                // Fingers rarely lift together, so fewer of them is the start of the lift.
                _state = State.Lifted;
                _liftedAt = now;
                return TouchpadDragStep.None;

            default:
                if (down.Count > fingers)
                {
                    _state = State.Idle;
                    return new TouchpadDragStep(false, 0, 0, Release: true);
                }

                if (down.Count == fingers)
                    _state = State.Dragging;

                return TouchpadDragStep.None;
        }
    }

    private TouchpadDragStep Move(IReadOnlyDictionary<int, (double X, double Y)> down)
    {
        double dx = 0, dy = 0;
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
            return TouchpadDragStep.None;

        return new TouchpadDragStep(false, dx / seen * PixelsPerMillimetre, dy / seen * PixelsPerMillimetre, false);
    }

    private static (double X, double Y) Centre(IReadOnlyDictionary<int, (double X, double Y)> down)
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
