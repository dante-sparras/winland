namespace Winland;

/// <summary>A damped spring, advanced by exact steps so that any step length and any stiffness stay stable.</summary>
internal static class Spring
{
    /// <summary>
    /// Moves <paramref name="value"/> and <paramref name="velocity"/> along the spring that pulls toward
    /// <paramref name="target"/> for <paramref name="seconds"/>. The acceleration is
    /// <c>(-stiffness * (value - target) - damping * velocity) / mass</c>.
    /// </summary>
    internal static void Advance(ref double value, ref double velocity, double target, double stiffness, double damping, double mass, double seconds)
    {
        if (seconds <= 0 || mass <= 0 || stiffness <= 0)
            return;

        var y0 = value - target;
        var v0 = velocity;
        var omega = Math.Sqrt(stiffness / mass);
        var zeta = damping / (2 * Math.Sqrt(stiffness * mass));
        double y;
        double v;
        if (Math.Abs(zeta - 1) < 1e-6)
        {
            var decay = Math.Exp(-omega * seconds);
            var b = v0 + omega * y0;
            y = decay * (y0 + b * seconds);
            v = decay * (b - omega * (y0 + b * seconds));
        }
        else if (zeta < 1)
        {
            var rate = zeta * omega;
            var damped = omega * Math.Sqrt(1 - zeta * zeta);
            var decay = Math.Exp(-rate * seconds);
            var cos = Math.Cos(damped * seconds);
            var sin = Math.Sin(damped * seconds);
            var b = (v0 + rate * y0) / damped;
            y = decay * (y0 * cos + b * sin);
            v = decay * (-rate * (y0 * cos + b * sin) + damped * (b * cos - y0 * sin));
        }
        else
        {
            var root = omega * Math.Sqrt(zeta * zeta - 1);
            var slow = -omega * zeta + root;
            var fast = -omega * zeta - root;
            var second = (v0 - slow * y0) / (fast - slow);
            var first = y0 - second;
            var a = first * Math.Exp(slow * seconds);
            var b = second * Math.Exp(fast * seconds);
            y = a + b;
            v = slow * a + fast * b;
        }

        value = target + y;
        velocity = v;
    }
}
