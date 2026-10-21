namespace Winland;

public sealed partial class Core
{
    /// <summary>The button scrolling the mouse hook carries out. It reads it with no lock.</summary>
    public ButtonScrollOptions ButtonScroll => _input.ButtonScroll;

    /// <summary><c>input:rotation</c>, in degrees, which the mouse hook reads with no lock. A touchpad move is not turned.</summary>
    public int PointerRotation => _input.Rotation;

    /// <summary>The <c>input:scroll_points</c> curve the mouse hook carries out, or null. It reads it with no lock.</summary>
    public ScrollCurve? ScrollCurve { get; private set; }

    /// <summary><c>input:emulate_discrete_scroll</c>, which the mouse hook reads with no lock.</summary>
    public int DiscreteScrollMode => _input.EmulateDiscreteScroll;
}
