namespace Winland.Domain;

public sealed record CursorBehavior(bool HideOnKeyPress, double InactiveTimeoutSeconds)
{
    /// <summary><c>cursor:invisible</c>: the cursor is never shown.</summary>
    public bool Invisible { get; init; }

    /// <summary><c>cursor:warp_back_after_non_mouse_input</c>: a mouse move after another input moved the cursor starts where the mouse left it.</summary>
    public bool WarpBackAfterNonMouseInput { get; init; }

    /// <summary><c>cursor:hide_on_touch</c>: touch hides the cursor until the mouse moves.</summary>
    public bool HideOnTouch { get; init; } = true;

    /// <summary><c>cursor:hide_on_tablet</c>: a pen hides the cursor until the mouse moves.</summary>
    public bool HideOnTablet { get; init; }

    /// <summary><c>cursor:hotspot_padding</c>: how many logical pixels the cursor keeps from the outer edges of the monitors.</summary>
    public int HotspotPadding { get; init; }
}
