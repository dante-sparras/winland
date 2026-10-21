namespace Winland;

/// <summary>
/// A spring curve from a <c>spring</c> line, as Winland's animation library steps one: a mass on a spring
/// with damping, let go from 0 at rest and pulled to 1, measured in seconds. It runs until it is within
/// <see cref="Epsilon"/> of 1 and slower than <see cref="Epsilon"/> a second, so its time is its own and not the
/// animation's speed.
/// </summary>
public sealed record SpringCurve(double Stiffness, double Damping, double Mass) : Easing
{
    /// <summary>How close to 1, and how slow, the spring is when it has settled.</summary>
    public const double Epsilon = 0.001;

    // The longest a spring runs, so one that barely moves still ends.
    private const double LongestSeconds = 60;

    /// <summary>How long the spring takes to settle, in milliseconds.</summary>
    public double Milliseconds { get; } = Settle(Stiffness, Damping, Mass);

    public override double Ease(double x)
    {
        if (double.IsNaN(x) || x <= 0)
            return 0;
        if (x >= 1)
            return 1;

        return At(Stiffness, Damping, Mass, x * Milliseconds / 1000).Value;
    }

    // The value and velocity of a spring let go from 0 at rest, after the given seconds. It is solved exactly for an
    // underdamped, a critically damped and an overdamped spring, as Winland's library solves each step.
    private static (double Value, double Velocity) At(double stiffness, double damping, double mass, double seconds)
    {
        mass = Math.Max(mass, 0.0001);
        stiffness = Math.Max(stiffness, 0.0001);
        damping = Math.Max(damping, 0);
        const double Start = -1;
        var omega0 = Math.Sqrt(stiffness / mass);
        var gamma = damping / (2 * mass);
        if (gamma < omega0)
        {
            var omegaD = Math.Sqrt((omega0 * omega0) - (gamma * gamma));
            var decay = Math.Exp(-gamma * seconds);
            var sin = Math.Sin(omegaD * seconds);
            var cos = Math.Cos(omegaD * seconds);
            var displacement = decay * ((Start * cos) + (gamma * Start / omegaD * sin));
            var velocity = -decay * (omega0 * omega0 * Start / omegaD) * sin;
            return (1 + displacement, velocity);
        }

        if (Math.Abs(gamma - omega0) <= Math.Max(omega0, 1) * 0.0001)
        {
            var decay = Math.Exp(-gamma * seconds);
            var b = gamma * Start;
            return (1 + (decay * (Start + (b * seconds))), -decay * gamma * b * seconds);
        }

        var root = Math.Sqrt((gamma * gamma) - (omega0 * omega0));
        var r1 = -gamma + root;
        var r2 = -gamma - root;
        var a = -(r2 * Start) / (r1 - r2);
        var rest = Start - a;
        var e1 = Math.Exp(r1 * seconds);
        var e2 = Math.Exp(r2 * seconds);
        return (1 + (a * e1) + (rest * e2), (a * r1 * e1) + (rest * r2 * e2));
    }

    // The first millisecond at which the spring has settled.
    private static double Settle(double stiffness, double damping, double mass)
    {
        for (var ms = 1; ms <= LongestSeconds * 1000; ms++)
        {
            var (value, velocity) = At(stiffness, damping, mass, ms / 1000.0);
            if (Math.Abs(1 - value) <= Epsilon && Math.Abs(velocity) <= Epsilon)
                return ms;
        }

        return LongestSeconds * 1000;
    }
}
