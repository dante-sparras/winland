using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// <c>input:scroll_points</c>: the scroll acceleration curve, as libinput takes it. The value is a step and then
/// the factors the wheel's speed is multiplied by: the first factor is at speed 0, the next one a step faster, and so on.
/// Between two points the factor is interpolated in a straight line, and a speed past the last point gets the last factor.
/// Speed is in scroll units per millisecond, where one wheel notch is <see cref="UnitsPerNotch"/> units.
/// </summary>
public sealed class ScrollCurve
{
    /// <summary>The scroll units one wheel notch is worth, as libinput counts them.</summary>
    public const double UnitsPerNotch = 15;

    /// <summary>The most points libinput takes.</summary>
    public const int MaxPoints = 64;

    private readonly double _step;
    private readonly double[] _points;

    private ScrollCurve(double step, double[] points)
    {
        _step = step;
        _points = points;
    }

    /// <summary>The curve the points name, or null when they are unset or are not a step and at least two non-negative factors.</summary>
    public static ScrollCurve? Parse(string? points)
    {
        if (string.IsNullOrWhiteSpace(points))
            return null;

        var words = points.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length < 3 || words.Length - 1 > MaxPoints)
            return null;

        var numbers = new double[words.Length];
        for (var i = 0; i < words.Length; i++)
        {
            if (!double.TryParse(words[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]) || !double.IsFinite(numbers[i]) || numbers[i] < 0)
                return null;
        }

        return numbers[0] > 0 ? new ScrollCurve(numbers[0], numbers[1..]) : null;
    }

    /// <summary>The factor a wheel moving at <paramref name="speed"/> scroll units per millisecond is multiplied by.</summary>
    public double Factor(double speed)
    {
        var at = Math.Max(speed, 0) / _step;
        var index = (int)Math.Min(Math.Floor(at), _points.Length - 1);
        if (index >= _points.Length - 1)
            return _points[^1];

        return _points[index] + ((_points[index + 1] - _points[index]) * (at - index));
    }
}
