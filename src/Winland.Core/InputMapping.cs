namespace Winland;

public static class InputMapping
{
    // Windows keyboard delay is 0..3: 250, 500, 750 and 1000 ms. Ties take the longer delay.
    public static int KeyboardDelay(int milliseconds)
    {
        var clamped = Math.Clamp(milliseconds, 0, 2000);
        ReadOnlySpan<int> steps = [250, 500, 750, 1000];
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < steps.Length; i++)
        {
            var distance = Math.Abs(clamped - steps[i]);
            if (distance < bestDistance || (distance == bestDistance && i > best))
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    // System speed 0 is about 2.5 repeats per second and 31 is about 30.
    public static int KeyboardSpeed(int repeatsPerSecond)
    {
        var cps = Math.Clamp(repeatsPerSecond, 0, 200);
        var speed = (int)Math.Round((cps - 2.5) * 31.0 / 27.5, MidpointRounding.AwayFromZero);
        return Math.Clamp(speed, 0, 31);
    }

    // Sensitivity -1..1 maps onto pointer speed 1..20, and 0 maps to 10.
    public static int MouseSpeed(double sensitivity)
    {
        var clamped = Math.Clamp(sensitivity, -1d, 1d);
        var speed = (int)Math.Round(10d + (clamped * 10d), MidpointRounding.AwayFromZero);
        return Math.Clamp(speed, 1, 20);
    }

    // A factor of 1 is the usual 3 lines per notch.
    public static int WheelLines(double scrollFactor)
    {
        var factor = Math.Clamp(scrollFactor, 0d, 2d);
        var lines = (int)Math.Round(factor * 3d, MidpointRounding.AwayFromZero);
        return Math.Clamp(lines, 0, 100);
    }
}
