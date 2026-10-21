namespace Winland;

public readonly record struct BorderColor(byte A, byte R, byte G, byte B)
{
    public static bool TryParse(string text, out BorderColor color, out bool gradient)
    {
        color = default;
        gradient = false;
        if (!ConfigValueSyntax.TryParseGradient(text, out var parsed))
            return false;

        gradient = parsed.Colors > 1 || parsed.HasAngle;
        if (gradient)
            return false;

        color = parsed.Color;
        return true;
    }
}
