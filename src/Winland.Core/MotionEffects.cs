namespace Winland;

/// <summary>
/// The spring and mesh of <c>decoration:wobble</c>, with the limits the pinned release gives each value.
/// </summary>
public sealed record WobbleSettings(
    int Mesh,
    double Stiffness,
    double Damping,
    double Mass,
    double Intensity,
    double ValueEpsilon,
    double VelocityEpsilon)
{
    /// <summary>The documented values.</summary>
    public static WobbleSettings Documented { get; } = new(12, 200, 12, 1, 0.2, 0.25, 2);
}

/// <summary>
/// The effects a window's motions are drawn with: a wobbling mesh, and motion blur. A window has none while it is
/// fullscreen, and no wobble while its <c>no_wobble</c> property is set.
/// </summary>
/// <param name="Wobble">The wobble, or null for none.</param>
/// <param name="BlurSamples">The motion blur's sample count, from 2 to 64, or 0 for no blur.</param>
public sealed record MotionEffects(WobbleSettings? Wobble, int BlurSamples)
{
    /// <summary>Whether anything is drawn with an effect.</summary>
    public bool Any => Wobble is not null || BlurSamples > 1;
}
