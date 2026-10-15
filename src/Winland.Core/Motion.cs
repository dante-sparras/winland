namespace Winland;

/// <summary>What happens to a window when its motion lands.</summary>
public enum MotionEnd
{
    /// <summary>The window stays where the motion left it, which is its Layout rectangle.</summary>
    None,

    /// <summary>The window is hidden and then put back on <see cref="Motion.Rest"/>.</summary>
    Hide,

    /// <summary>The window is asked to close. If it stays open it is put back on <see cref="Motion.Rest"/>.</summary>
    Close,
}

/// <summary>
/// One window's animation, planned by the core and stepped by the adapter. A channel whose timing is null
/// does not animate: its goal applies at once.
/// </summary>
/// <remarks>
/// A motion that starts while another runs on the same window begins each running channel from the value
/// it shows at that moment, unless the motion warps to its own start.
/// </remarks>
public sealed record Motion(WindowId Window, Rectangle To)
{
    /// <summary>
    /// The rectangle the window shows when the motion starts. A running motion starts from where the window
    /// is instead, unless <see cref="Warp"/> is set. Null is <see cref="To"/>.
    /// </summary>
    public Rectangle? From { get; init; }

    /// <summary>Start from <see cref="From"/> even when the window is already moving.</summary>
    public bool Warp { get; init; }

    public Timing? Move { get; init; }

    /// <summary>
    /// The fade the window shows when the motion starts, from 0 to 1. A running fade starts from where it is
    /// instead, unless <see cref="Warp"/> is set. Null is the fade the window shows now.
    /// </summary>
    public double? FadeFrom { get; init; }

    public double FadeTo { get; init; } = 1;

    public Timing? Fade { get; init; }

    /// <summary>The window's alpha after the motion, from its decoration. The fade multiplies it.</summary>
    public byte AlphaTo { get; init; } = 255;

    /// <summary>The alpha the window had before, when it changes.</summary>
    public byte? AlphaFrom { get; init; }

    public Timing? Alpha { get; init; }

    /// <summary>The border color after the motion, from its decoration. Null leaves the border alone.</summary>
    public BorderColor? BorderTo { get; init; }

    public BorderColor? BorderFrom { get; init; }

    public Timing? Border { get; init; }

    public MotionEnd End { get; init; }

    /// <summary>
    /// The Layout rectangle. It equals <see cref="To"/> unless the motion hides or closes the window, which then
    /// lands here after <see cref="End"/>.
    /// </summary>
    public Rectangle Rest { get; init; } = To;
}
