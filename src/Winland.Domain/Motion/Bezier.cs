namespace Winland.Domain;

/// <summary>
/// A cubic Bézier easing curve from (0, 0) to (1, 1) through two control points.
/// <see cref="Ease"/> takes the fraction of time passed and gives the fraction of the way travelled.
/// </summary>
public sealed record Bezier(double X1, double Y1, double X2, double Y2) : Easing
{
    /// <summary>The curve named <c>default</c>, which every leaf uses unless the config names another.</summary>
    public static Bezier Default { get; } = new(0, 0.75, 0.15, 1);

    /// <summary>The curve named <c>linear</c>.</summary>
    public static Bezier Linear { get; } = new(0, 0, 1, 1);

    /// <summary>
    /// The progress at time <paramref name="x"/>, from 0 to 1. A control point above 1 or below 0
    /// overshoots, so the result can leave that range before it lands on 1.
    /// </summary>
    public override double Ease(double x)
    {
        if (double.IsNaN(x) || x <= 0)
            return 0;
        if (x >= 1)
            return 1;

        return Component(Solve(x), Y1, Y2);
    }

    // The curve's x is not the parameter t. Newton steps find t for x, and bisection
    // takes over when the slope is too flat for them.
    private double Solve(double x)
    {
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var error = Component(t, X1, X2) - x;
            if (Math.Abs(error) < 1e-7)
                return t;

            var slope = Slope(t, X1, X2);
            if (Math.Abs(slope) < 1e-6)
                break;

            t -= error / slope;
            if (t is < 0 or > 1)
                break;
        }

        var low = 0.0;
        var high = 1.0;
        t = x;
        for (var i = 0; i < 64; i++)
        {
            var value = Component(t, X1, X2);
            if (Math.Abs(value - x) < 1e-7)
                return t;

            if (value < x)
                low = t;
            else
                high = t;

            t = (low + high) / 2;
        }

        return t;
    }

    private static double Component(double t, double first, double second)
    {
        var inverse = 1 - t;
        return (3 * inverse * inverse * t * first) + (3 * inverse * t * t * second) + (t * t * t);
    }

    private static double Slope(double t, double first, double second)
    {
        var inverse = 1 - t;
        return (3 * inverse * inverse * first) + (6 * inverse * t * (second - first)) + (3 * t * t * (1 - second));
    }
}
