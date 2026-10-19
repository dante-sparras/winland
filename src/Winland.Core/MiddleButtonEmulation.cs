namespace Winland;

/// <summary>The left or right button, as the mouse hook sees it.</summary>
public enum EmulatedButton
{
    Left,
    Right,
}

/// <summary>
/// What the emulation does with one button event. <see cref="Swallow"/> keeps the event from the programs.
/// <see cref="MiddleDown"/> and <see cref="MiddleUp"/> send the middle button. <see cref="Press"/> and
/// <see cref="Release"/> send a held-back button's press, and its release, to the programs after all.
/// </summary>
public readonly record struct MiddleStep(bool Swallow, bool MiddleDown = false, bool MiddleUp = false, EmulatedButton? Press = null, EmulatedButton? Release = null)
{
    public static MiddleStep Pass { get; } = default;

    public bool Any => Swallow || MiddleDown || MiddleUp || Press is not null || Release is not null;
}

/// <summary>
/// <c>input:touchpad:middle_button_emulation</c>: the left and right buttons pressed together make a middle click, as
/// libinput's do. A first press made while a finger is on the touchpad is held back for <see cref="WindowMs"/>. If the
/// other button comes in that time, both are swallowed and the middle button goes down, and up once both are released.
/// Otherwise the held press is sent after all, and a press released within the time is sent as a click. A press with no
/// finger on the touchpad, or with the other button already down, goes through at once. Used from the mouse hook and a
/// timer, so it locks.
/// </summary>
public sealed class MiddleButtonEmulation
{
    /// <summary>How long a first press waits for the other button. libinput waits about 50 ms.</summary>
    public const long WindowMs = 50;

    private readonly Lock _gate = new();
    private State _state;
    private EmulatedButton _held;
    private long _since;
    private bool _leftDown;
    private bool _rightDown;
    private bool _leftEmulated;
    private bool _rightEmulated;

    private enum State
    {
        Idle,
        Held,
        Emulating,
    }

    /// <summary>When a held press is sent unless the other button comes, or null when none is held.</summary>
    public long? ExpiresAt
    {
        get
        {
            lock (_gate)
                return _state == State.Held ? _since + WindowMs : null;
        }
    }

    /// <summary>A button went down. <paramref name="touched"/> is whether a finger is on the touchpad.</summary>
    public MiddleStep Down(EmulatedButton button, long now, bool touched, bool enabled)
    {
        lock (_gate)
        {
            var late = ExpireLocked(now);
            if (_state == State.Emulating)
                return new MiddleStep(true);

            if (_state == State.Held)
            {
                if (button == _held)
                    return new MiddleStep(true);

                _state = State.Emulating;
                _leftEmulated = _rightEmulated = true;
                return new MiddleStep(true, MiddleDown: true);
            }

            if (enabled && touched && !_leftDown && !_rightDown)
            {
                _state = State.Held;
                _held = button;
                _since = now;
                return new MiddleStep(true);
            }

            Mark(button, true);
            return late;
        }
    }

    /// <summary>A button was released.</summary>
    public MiddleStep Up(EmulatedButton button, long now)
    {
        lock (_gate)
        {
            var late = ExpireLocked(now);
            if (_state == State.Emulating)
            {
                if (button == EmulatedButton.Left)
                    _leftEmulated = false;
                else
                    _rightEmulated = false;

                if (_leftEmulated || _rightEmulated)
                    return new MiddleStep(true);

                _state = State.Idle;
                return new MiddleStep(true, MiddleUp: true);
            }

            if (_state == State.Held && button == _held)
            {
                _state = State.Idle;
                return new MiddleStep(true, Press: button, Release: button);
            }

            // The held press was due but not yet sent: its release must not overtake it, so both go as a click.
            Mark(button, false);
            return late.Press == button ? new MiddleStep(true, Press: button, Release: button) : late;
        }
    }

    /// <summary>Sends a held press after all once the other button has had <see cref="WindowMs"/> to come.</summary>
    public MiddleStep Expire(long now)
    {
        lock (_gate)
            return ExpireLocked(now);
    }

    private MiddleStep ExpireLocked(long now)
    {
        if (_state != State.Held || now < _since + WindowMs)
            return MiddleStep.Pass;

        _state = State.Idle;
        Mark(_held, true);
        return new MiddleStep(false, Press: _held);
    }

    private void Mark(EmulatedButton button, bool down)
    {
        if (button == EmulatedButton.Left)
            _leftDown = down;
        else
            _rightDown = down;
    }
}
