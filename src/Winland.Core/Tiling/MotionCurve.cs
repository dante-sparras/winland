namespace Winland.Core.Tiling;

/// <summary>
/// How long a window takes to move, open, and close, and the curve each follows. The figures are Omarchy Quattro's, from the
/// <c>hl.curve</c> and <c>hl.animation</c> lines of its <c>looknfeel.lua</c>, where a speed of 1 is 100 milliseconds.
/// A Workspace change has no animation there, and none here.
/// </summary>
public static class MotionCurve
{
    /// <summary>How long a Glide takes, in milliseconds: Quattro's <c>windows</c> leaf, speed 3.79.</summary>
    public const int GlideMilliseconds = 379;

    /// <summary>How long a window that opens takes to fade in, in milliseconds: Quattro's <c>fadeIn</c> leaf, speed 1.73.</summary>
    public const int OpenFadeMilliseconds = 173;

    /// <summary>How long a window that is closed takes to fade out, in milliseconds: Quattro's <c>fadeOut</c> leaf, speed 1.46.</summary>
    public const int CloseFadeMilliseconds = 146;

    /// <summary>Quattro's <c>easeOutQuint</c>: most of the way at once, then a long settle. A Glide follows it.</summary>
    /// <param name="progress">How much of the time has passed, from 0 to 1.</param>
    /// <returns>How much of the way is covered, from 0 to 1.</returns>
    public static double EaseOutQuint(double progress) => Bezier(0.23, 1, 0.32, 1, progress);

    /// <summary>Quattro's <c>almostLinear</c>. A fade follows it.</summary>
    /// <param name="progress">How much of the time has passed, from 0 to 1.</param>
    /// <returns>How much of the way is covered, from 0 to 1.</returns>
    public static double AlmostLinear(double progress) => Bezier(0.5, 0.5, 0.75, 1, progress);

    /// <summary>
    /// How opaque a window is while it fades, from 0 for clear to 255 for solid.
    /// </summary>
    /// <param name="elapsedMilliseconds">Time since the fade started.</param>
    /// <param name="durationMilliseconds">How long the fade takes.</param>
    /// <param name="opening">True for a window fading in, false for one fading out.</param>
    /// <returns>255 at the end of a fade in and 0 at the end of a fade out, and the other at its start.</returns>
    public static byte Alpha(long elapsedMilliseconds, int durationMilliseconds, bool opening)
    {
        var progress = durationMilliseconds <= 0 ? 1 : Math.Clamp(elapsedMilliseconds / (double)durationMilliseconds, 0, 1);
        var shown = AlmostLinear(progress);
        return (byte)Math.Round(255 * (opening ? shown : 1 - shown), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// A cubic bezier from 0,0 to 1,1, as CSS and Hyprland write a curve: the value at <paramref name="progress"/> along the time axis.
    /// </summary>
    /// <param name="x1">The first control point's time.</param>
    /// <param name="y1">The first control point's value.</param>
    /// <param name="x2">The second control point's time.</param>
    /// <param name="y2">The second control point's value.</param>
    /// <param name="progress">How much of the time has passed. Below 0 reads as 0, and above 1 as 1.</param>
    /// <returns>Exactly 0 at the start and exactly 1 at the end.</returns>
    public static double Bezier(double x1, double y1, double x2, double y2, double progress)
    {
        if (progress <= 0)
        {
            return 0;
        }

        if (progress >= 1)
        {
            return 1;
        }

        // The curve gives time and value for a parameter. The time rises with it, so the parameter for a time is found by halving.
        double low = 0;
        double high = 1;
        var at = progress;
        for (var i = 0; i < 32; i++)
        {
            at = (low + high) / 2;
            if (Along(x1, x2, at) < progress)
            {
                low = at;
            }
            else
            {
                high = at;
            }
        }

        return Along(y1, y2, at);
    }

    private static double Along(double first, double second, double at)
    {
        var rest = 1 - at;
        return (3 * rest * rest * at * first) + (3 * rest * at * at * second) + (at * at * at);
    }
}
