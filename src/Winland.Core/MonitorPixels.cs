namespace Winland;

/// <summary>
/// Converts between the physical pixels Windows reports and the logical pixels a monitor rule uses.
/// </summary>
/// <remarks>
/// Windows gives each monitor a rectangle on the virtual screen in physical pixels, plus a scale
/// (DPI divided by 96). Logical length is that physical length divided by the scale, rounded half
/// away from zero. A scale-2 monitor that is 3840 physical pixels wide is 1920 logical pixels wide,
/// and automatic positions pack against that logical width. Multiplying a logical position by the
/// same scale returns the physical origin stored on the monitor. Placements stay in physical pixels.
/// A scale that is not finite, or is outside (0, 1000), is treated as 1.
/// </remarks>
public static class MonitorPixels
{
    public static int Logical(int physical, double scale) =>
        Round(physical / Factor(scale));

    public static int Physical(int logical, double scale) =>
        Round(logical * Factor(scale));

    private static double Factor(double scale) =>
        scale is > 0 and < 1000 && double.IsFinite(scale) ? scale : 1;

    private static int Round(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded >= int.MaxValue)
            return int.MaxValue;
        if (rounded <= int.MinValue)
            return int.MinValue;

        return (int)rounded;
    }
}
