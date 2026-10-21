namespace Winland.Application;

public sealed partial class WindowManager
{
    // Whether the displays are on, as the last dpms left them, which Winland keeps for every display at once.
    private bool _displaysOn = true;

    /// <summary>
    /// Whether a key press or release should turn the displays back on: <c>misc:key_press_enables_dpms</c> is set and
    /// dpms turned them off. The keyboard hook reads it with no lock and sends <c>dpms on</c>.
    /// </summary>
    public bool KeyWakesDisplays => _config.Session.KeyPressEnablesDpms && !Volatile.Read(ref _displaysOn);

    // misc:mouse_move_enables_dpms: a pointer move, or the start of a swipe, after dpms off turns every display on, as
    // in Winland.
    private Outcome WakeDisplays(Input input, Outcome outcome)
    {
        if (input is not (PointerMoved or SwipeBegan) || !_config.Session.MouseMoveEnablesDpms || Volatile.Read(ref _displaysOn))
            return outcome;

        var woken = Dpms("on");
        return outcome with { DisplayPower = [.. outcome.DisplayPower, .. woken.DisplayPower] };
    }
}
