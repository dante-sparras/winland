namespace Winland;

/// <summary>
/// The touchpad options Windows has no setting for, which Winland carries out itself: <c>scroll_factor</c> on the wheel
/// turns the touchpad makes, <c>flip_x</c> and <c>flip_y</c> on the cursor moves it makes, three- or four-finger drag,
/// a two-finger tap that is a middle click (<c>tap_button_map = lmr</c>), tap and drag with a lock (<c>drag_lock</c>), and the left and right buttons together as a middle click.
/// </summary>
/// <param name="ScrollFactor"><c>input:touchpad:scroll_factor</c>, 0 to 100.</param>
/// <param name="WheelFactor">The <c>input:scroll_factor</c> Windows' wheel lines already apply, or null when it is unset.</param>
/// <param name="FlipX"><c>input:touchpad:flip_x</c>.</param>
/// <param name="FlipY"><c>input:touchpad:flip_y</c>.</param>
/// <param name="DragFingers">How many fingers drag, 3 or 4, or 0 when <c>drag_3fg</c> is off.</param>
/// <param name="MiddleTap">Whether a two-finger tap is a middle click.</param>
/// <param name="DragLock">The tap and drag lock Winland carries out: 0 for none, 1 releases after a timeout, 2 at the next tap.</param>
/// <param name="MiddleEmulation">Whether the left and right buttons pressed together are a middle click.</param>
public sealed record TouchpadBehavior(double ScrollFactor, double? WheelFactor, bool FlipX, bool FlipY, int DragFingers, bool MiddleTap, int DragLock = 0, bool MiddleEmulation = false)
{
    public static TouchpadBehavior Default { get; } = new(1, null, false, false, 0, false);

    /// <summary>Whether the touchpad's cursor moves are turned around at all.</summary>
    public bool Flips => FlipX || FlipY;

    /// <summary>
    /// What a wheel turn is multiplied by. A touchpad's is <c>scroll_factor</c>, less the <c>input:scroll_factor</c>
    /// Windows' wheel lines already apply to it. Another wheel's is 1, except that a touchpad <c>scroll_factor</c> of 0
    /// stops every wheel, as the pinned release takes every axis event as the touchpad's then.
    /// </summary>
    public double WheelScale(bool touchpad) => WheelScale(touchpad, default);

    /// <summary>
    /// <see cref="WheelScale(bool)"/> over a window that sets its own factors: its <c>scroll_touchpad</c> replaces
    /// <c>scroll_factor</c> for a turn taken as the touchpad's, and its <c>scroll_mouse</c> replaces
    /// <c>input:scroll_factor</c> for another, each less the factor Windows' wheel lines already apply.
    /// </summary>
    public double WheelScale(bool touchpad, ScrollOverride window)
    {
        if (touchpad || ScrollFactor <= 0)
            return Beyond(window.Touchpad ?? ScrollFactor);

        return window.Mouse is double mouse ? Beyond(mouse) : 1;
    }

    // A factor as a multiple of what Windows' wheel lines already apply.
    private double Beyond(double factor)
    {
        if (factor <= 0)
            return 0;

        return WheelFactor is double lines && lines > 0 ? factor / lines : factor;
    }

    /// <summary>A touchpad's cursor move, turned around on the axes <c>flip_x</c> and <c>flip_y</c> name.</summary>
    public (double Dx, double Dy) Flip(double dx, double dy) => (FlipX ? -dx : dx, FlipY ? -dy : dy);
}
