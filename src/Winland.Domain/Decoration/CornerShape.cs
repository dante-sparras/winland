namespace Winland.Domain;

/// <summary>
/// Picks how one Client's corners are drawn. The radius is <c>rounding * rounding_power / 2</c>
/// logical pixels, so a squarer curve keeps the same perceived roundness. DWM draws circular
/// corners of 8 and 4 pixels and scales them with the window's DPI, so a circle of either
/// radius uses that preference. Any other radius or curve is cut as a region instead.
/// </summary>
public static class CornerShape
{
    public const int Round = 8;

    public const int RoundSmall = 4;

    private const int MaxSteps = 32;

    public static CornerChoice For(int rounding, float power, double scale)
    {
        if (rounding <= 0)
            return CornerChoice.Square;

        if (power == 2f && rounding == Round)
            return new CornerChoice(CornerPreference.Round, null);

        if (power == 2f && rounding == RoundSmall)
            return new CornerChoice(CornerPreference.RoundSmall, null);

        var factor = scale is > 0 and < 1000 && double.IsFinite(scale) ? scale : 1;
        var radius = Math.Round(rounding * (power / 2.0) * factor, MidpointRounding.AwayFromZero);
        if (radius < 1)
            return CornerChoice.Square;

        return new CornerChoice(CornerPreference.DoNotRound, new CornerRegion((int)Math.Min(radius, int.MaxValue / 2), power));
    }

    /// <summary>
    /// The outline of a <paramref name="width"/> by <paramref name="height"/> frame with these corners,
    /// clockwise from the left end of the top left corner. A radius larger than half the shorter
    /// side is cut to that half. Each corner follows <c>|x|^power + |y|^power = radius^power</c>.
    /// </summary>
    public static IReadOnlyList<Point> Outline(int width, int height, CornerRegion region)
    {
        if (width <= 0 || height <= 0)
            return [];

        var radius = Math.Min(region.Radius, Math.Min(width, height) / 2);
        if (radius <= 0)
            return [new Point(0, 0), new Point(width, 0), new Point(width, height), new Point(0, height)];

        var power = float.IsFinite(region.Power) ? Math.Clamp(region.Power, 1f, 10f) : 2f;
        var steps = Math.Clamp(radius, 1, MaxSteps);
        var near = new (double Along, double Across)[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var angle = Math.PI / 2 * i / steps;
            near[i] = (Curve(Math.Cos(angle), power) * radius, Curve(Math.Sin(angle), power) * radius);
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

    private static double Curve(double value, float power) =>
        Math.Pow(Math.Max(0, value), 2.0 / power);

    private static void Add(List<Point> points, double x, double y)
    {
        var point = new Point(
            (int)Math.Round(x, MidpointRounding.AwayFromZero),
            (int)Math.Round(y, MidpointRounding.AwayFromZero));
        if (points.Count == 0 || points[^1] != point)
            points.Add(point);
    }
}
