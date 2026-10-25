namespace Winland.Domain;

/// <summary>
/// Picks how one Client's corners are drawn. DWM draws circular corners of 8 and 4 pixels and scales
/// them with the window's DPI, so a radius of either uses that preference. Any other radius is cut
/// as a region instead.
/// </summary>
public static class CornerShape
{
    public const int Round = 8;

    public const int RoundSmall = 4;

    private const int MaxSteps = 32;

    public static CornerChoice For(int rounding, double scale)
    {
        if (rounding <= 0)
            return CornerChoice.Square;

        if (rounding == Round)
            return new CornerChoice(CornerPreference.Round, null);

        if (rounding == RoundSmall)
            return new CornerChoice(CornerPreference.RoundSmall, null);

        var factor = scale is > 0 and < 1000 && double.IsFinite(scale) ? scale : 1;
        var radius = Math.Round(rounding * factor, MidpointRounding.AwayFromZero);
        if (radius < 1)
            return CornerChoice.Square;

        return new CornerChoice(CornerPreference.DoNotRound, new CornerRegion((int)Math.Min(radius, int.MaxValue / 2)));
    }

    /// <summary>
    /// The outline of a <paramref name="width"/> by <paramref name="height"/> frame with these corners,
    /// clockwise from the left end of the top left corner. A radius larger than half the shorter
    /// side is cut to that half. Each corner is a circle.
    /// </summary>
    public static IReadOnlyList<Point> Outline(int width, int height, CornerRegion region)
    {
        if (width <= 0 || height <= 0)
            return [];

        var radius = Math.Min(region.Radius, Math.Min(width, height) / 2);
        if (radius <= 0)
            return [new Point(0, 0), new Point(width, 0), new Point(width, height), new Point(0, height)];

        var steps = Math.Clamp(radius, 1, MaxSteps);
        var near = new (double Along, double Across)[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var angle = Math.PI / 2 * i / steps;
            near[i] = (Math.Max(0, Math.Cos(angle)) * radius, Math.Max(0, Math.Sin(angle)) * radius);
        }

        var points = new List<Point>(4 * (steps + 1));
        foreach (var (along, across) in near)
            Add(points, radius - along, radius - across);
        foreach (var (along, across) in near)
            Add(points, width - radius + across, radius - along);
        foreach (var (along, across) in near)
            Add(points, width - radius + along, height - radius + across);
        foreach (var (along, across) in near)
            Add(points, radius - across, height - radius + along);

        if (points.Count > 1 && points[^1] == points[0])
            points.RemoveAt(points.Count - 1);

        return points;
    }

    private static void Add(List<Point> points, double x, double y)
    {
        var point = new Point(
            (int)Math.Round(x, MidpointRounding.AwayFromZero),
            (int)Math.Round(y, MidpointRounding.AwayFromZero));
        if (points.Count == 0 || points[^1] != point)
            points.Add(point);
    }
}

/// <summary>The corners of a rectangle that are round.</summary>
[Flags]
public enum Corners
{
    None = 0,
    TopLeft = 1,
    TopRight = 2,
    BottomLeft = 4,
    BottomRight = 8,
    All = TopLeft | TopRight | BottomLeft | BottomRight,
}

/// <summary>Corners cut from the window: a circular radius in physical pixels.</summary>
public readonly record struct CornerRegion(int Radius);

/// <summary>The DWM corner preference for one Client, and the region to cut when no preference matches the radius.</summary>
public readonly record struct CornerChoice(CornerPreference Preference, CornerRegion? Region)
{
    public static CornerChoice Square { get; } = new(CornerPreference.DoNotRound, null);
}
