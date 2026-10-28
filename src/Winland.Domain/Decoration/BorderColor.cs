namespace Winland.Domain;

public readonly record struct BorderColor(byte A, byte R, byte G, byte B)
{
    /// <summary>Reads one color. A gradient or an angle does not read.</summary>
    public static bool TryParse(string text, out BorderColor color)
    {
        color = default;
        if (!ConfigValueSyntax.TryParseGradient(text, out var parsed) || parsed.Colors > 1 || parsed.HasAngle)
            return false;

        color = parsed.Color;
        return true;
    }
}
