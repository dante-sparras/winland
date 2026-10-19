namespace Winland;

/// <summary>
/// <c>input:touchpad:drag_lock</c>: tap and drag done from the fingers, because Windows' own has no lock. A finger that
/// taps and lifts within <see cref="TapMs"/>, then touches again within <see cref="DragWindowMs"/> and moves, holds the
/// button down. Lifting keeps it down: for <see cref="LockMs"/> in the timeout mode, where touching again carries the
/// drag on, or until the next tap in the sticky mode. The finger's cursor moves are Windows' own, so a step only ever
/// presses or releases. More than one finger, or a finger that moves before it lifts, is no tap.
/// </summary>
public sealed class TouchpadTapDrag
{
    /// <summary>The lock mode that lets the button go a moment after the finger lifts.</summary>
    public const int Timeout = 1;

    /// <summary>The lock mode that keeps the button down until the next tap.</summary>
    public const int Sticky = 2;

    /// <summary>How long a tap may last.</summary>
    public const long TapMs = 180;

    /// <summary>How long after a tap the finger can touch again to start a drag.</summary>
    public const long DragWindowMs = 300;

    /// <summary>How long the button stays down after the finger lifts, in the timeout mode.</summary>
    public const long LockMs = 300;

    /// <summary>How far, in millimetres, a finger can move and still tap, or hold still for a drag to wait.</summary>
    public const double MoveMm = 1.5;

    private State _state;
    private (double X, double Y) _from;
    private long _since;
    private int _mode;

    private enum State
    {
        Idle,
        First,
        Tapped,
        Second,
        Dragging,
        Locked,
        Touched,
        Spoiled,
    }

    /// <summary>Whether the button is held down.</summary>
    public bool Holding => _state is State.Dragging or State.Locked or State.Touched;

    /// <summary>When the held button is let go unless the finger comes back, or null when nothing waits.</summary>
    public long? ReleaseAt => _state == State.Locked && _mode == Timeout ? _since + LockMs : null;

    /// <summary>
    /// Adds a report of the fingers down, with <paramref name="mode"/> the lock mode: 0 for none, <see cref="Timeout"/>
    /// or <see cref="Sticky"/>.
    /// </summary>
    public TouchpadDragStep Frame(int mode, IReadOnlyDictionary<int, (double X, double Y)> down, long now)
    {
        ArgumentNullException.ThrowIfNull(down);
        var wasHolding = Holding;
        _mode = mode;
        if (mode is not (Timeout or Sticky))
        {
            _state = State.Idle;
            return Step(wasHolding);
        }

        if (Expire(now) is { Release: true } expired)
            return expired;

        Advance(down, now);
        return Step(wasHolding);
    }

    /// <summary>Lets the button go once the finger has been up for <see cref="LockMs"/> in the timeout mode.</summary>
    public TouchpadDragStep Expire(long now)
    {
        if (_state != State.Locked || _mode != Timeout || now < _since + LockMs)
            return TouchpadDragStep.None;

        _state = State.Idle;
        return Step(true);
    }

    private TouchpadDragStep Step(bool wasHolding) => new(Press: !wasHolding && Holding, 0, 0, Release: wasHolding && !Holding);

    private void Advance(IReadOnlyDictionary<int, (double X, double Y)> down, long now)
    {
        switch (_state)
        {
            case State.Idle:
                Touch(down, State.First, now);
                break;

            case State.First:
                if (down.Count == 0)
                {
                    _state = now - _since <= TapMs ? State.Tapped : State.Idle;
                    _since = now;
                }
                else if (down.Count > 1 || Moved(down))
                    _state = State.Spoiled;

                break;

            case State.Tapped:
                if (now - _since > DragWindowMs)
                {
                    _state = State.Idle;
                    Advance(down, now);
                }
                else
                    Touch(down, State.Second, now);

                break;

            case State.Second:
                if (down.Count == 0)
                    _state = State.Idle;
                else if (down.Count > 1)
                    _state = State.Spoiled;
                else if (Moved(down))
                    _state = State.Dragging;

                break;

            case State.Dragging:
                if (down.Count == 0)
                {
                    _state = State.Locked;
                    _since = now;
                }
                else if (down.Count > 1)
                    _state = State.Spoiled;

                break;

            case State.Locked:
                if (down.Count == 1)
                {
                    _from = Only(down);
                    _since = now;
                    _state = _mode == Sticky ? State.Touched : State.Dragging;
                }
                else if (down.Count > 1)
                    _state = State.Spoiled;

                break;

            case State.Touched:
                if (down.Count == 0)
                {
                    // A tap ends a sticky drag. A touch held longer leaves it locked.
                    _state = now - _since <= TapMs ? State.Idle : State.Locked;
                    _since = now;
                }
                else if (down.Count > 1)
                    _state = State.Spoiled;
                else if (Moved(down))
                    _state = State.Dragging;

                break;

            default:
                if (down.Count == 0)
                    _state = State.Idle;

                break;
        }
    }

    private void Touch(IReadOnlyDictionary<int, (double X, double Y)> down, State next, long now)
    {
        if (down.Count == 0)
            return;

        if (down.Count > 1)
        {
            _state = State.Spoiled;
            return;
        }

        _from = Only(down);
        _since = now;
        _state = next;
    }

    private bool Moved(IReadOnlyDictionary<int, (double X, double Y)> down)
    {
        var at = Only(down);
        var dx = at.X - _from.X;
        var dy = at.Y - _from.Y;
        return (dx * dx) + (dy * dy) >= MoveMm * MoveMm;
    }

    private static (double X, double Y) Only(IReadOnlyDictionary<int, (double X, double Y)> down)
    {
        foreach (var point in down.Values)
            return point;

        return default;
    }
}
