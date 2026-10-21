namespace Winland.Domain;

/// <summary>
/// <c>input:scroll_method on_button_down</c>: while <see cref="Button"/> is held, moving the pointer scrolls instead.
/// With <see cref="Lock"/> a click of the button turns the scrolling on until the next click.
/// </summary>
public sealed record ButtonScrollOptions(bool Enabled, string Button, bool Lock, bool Natural)
{
    public static ButtonScrollOptions Off { get; } = new(false, "mouse:274", false, false);
}

/// <summary>What the hook does with one press, release or move while button scrolling is on.</summary>
public readonly record struct ButtonScrollStep(bool Swallow, bool Click, int WheelX, int WheelY)
{
    public static ButtonScrollStep Pass { get; } = new(false, false, 0, 0);

    public static ButtonScrollStep Taken { get; } = new(true, false, 0, 0);
}

/// <summary>
/// Scrolling on button down, as libinput does it: a press of the scroll button is held back; moving the pointer while it
/// is down scrolls by the motion, a wheel notch for each 15 pixels, and the cursor stays; a release with no scrolling
/// gives the click back, or with the lock on turns scrolling on until the next click. Used on the hook thread.
/// </summary>
public sealed class ButtonScroller
{
    /// <summary>The pixels of motion one wheel notch takes.</summary>
    public const double PixelsPerNotch = 15;

    private const int Notch = 120;

    private ButtonScrollOptions _options = ButtonScrollOptions.Off;
    private State _state;
    private bool _scrolled;
    private double _restX;
    private double _restY;

    private enum State
    {
        Idle,
        Held,
        Locked,
        LockedHeld,
    }

    /// <summary>Takes new options. A change ends any scrolling under way.</summary>
    public void Configure(ButtonScrollOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options == _options)
            return;

        _options = options;
        _state = State.Idle;
    }

    public bool Scrolling => _state != State.Idle;

    public ButtonScrollStep Press(string button)
    {
        if (!_options.Enabled || button != _options.Button)
            return ButtonScrollStep.Pass;

        _state = _state == State.Locked ? State.LockedHeld : State.Held;
        _scrolled = false;
        return ButtonScrollStep.Taken;
    }

    public ButtonScrollStep Release(string button)
    {
        if (!_options.Enabled || button != _options.Button || _state == State.Idle)
            return ButtonScrollStep.Pass;

        switch (_state)
        {
            case State.LockedHeld:
                _state = State.Idle;
                return ButtonScrollStep.Taken;
            case State.Held when !_scrolled && _options.Lock:
                _state = State.Locked;
                return ButtonScrollStep.Taken;
            case State.Held when !_scrolled:
                _state = State.Idle;
                return new ButtonScrollStep(true, true, 0, 0);
            default:
                _state = State.Idle;
                return ButtonScrollStep.Taken;
        }
    }

    /// <summary>The pointer would move by <paramref name="dx"/>, <paramref name="dy"/>: while scrolling it scrolls instead.</summary>
    public ButtonScrollStep Move(int dx, int dy)
    {
        if (_state == State.Idle)
            return ButtonScrollStep.Pass;

        if (dx == 0 && dy == 0)
            return ButtonScrollStep.Taken;

        _scrolled = true;
        var sign = _options.Natural ? -1 : 1;
        _restX += dx * Notch / PixelsPerNotch;
        _restY += dy * Notch / PixelsPerNotch;
        var wheelX = (int)Math.Truncate(_restX);
        var wheelY = (int)Math.Truncate(_restY);
        _restX -= wheelX;
        _restY -= wheelY;

        // Moving down scrolls down, which is a negative wheel turn, and moving right scrolls right.
        return new ButtonScrollStep(true, false, sign * wheelX, -sign * wheelY);
    }
}
