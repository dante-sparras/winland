namespace Winland.Domain;

/// <summary>
/// Decides which Client the pointer focuses. Distances are logical pixels. Hit boxes are physical pixels.
/// </summary>
internal static class PointerFocus
{
    internal readonly record struct Target(WindowId Window, bool Floating, Rectangle Bounds);

    internal readonly record struct Decision(WindowId? Focus, WindowId? LastMouse, bool RememberMouse);

    internal static WindowId? At(IReadOnlyList<Target> targets, Point point)
    {
        ArgumentNullException.ThrowIfNull(targets);
        for (var i = targets.Count - 1; i >= 0; i--)
        {
            if (!targets[i].Floating)
                continue;

            if (Hits(targets[i], point))
                return targets[i].Window;
        }

        for (var i = 0; i < targets.Count; i++)
        {
            if (targets[i].Floating)
                continue;

            if (Hits(targets[i], point))
                return targets[i].Window;
        }

        return null;
    }

    internal static Decision Choose(
        int mode,
        double traveled,
        WindowId? under,
        bool underFloats,
        WindowId? keyboard,
        bool keyboardFloats,
        WindowId? lastMouse)
    {
        if (under is null)
            return new Decision(null, lastMouse, false);

        if (mode != 1)
        {
            var switches = mode != 3
                && keyboard is not null
                && under != keyboard
                && keyboardFloats != underFloats;
            return new Decision(switches ? under : null, lastMouse, false);
        }

        if (lastMouse == under && keyboard == under)
            return new Decision(null, lastMouse, false);

        if (keyboard != under && traveled > 0)
            return new Decision(under, under, true);

        return new Decision(null, under, true);
    }

    private static bool Hits(Target target, Point point)
    {
        var bounds = target.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return false;

        return point.X >= bounds.X && point.Y >= bounds.Y && point.X < bounds.X + bounds.Width && point.Y < bounds.Y + bounds.Height;
    }
}
