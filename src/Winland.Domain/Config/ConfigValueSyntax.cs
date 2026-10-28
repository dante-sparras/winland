using System.Globalization;
using static Winland.Domain.OrdinalText;

namespace Winland.Domain;

public static class ConfigValueSyntax
{
    public static bool TryParseBool(string text, out bool value)
    {
        if (Is(text, "true") || Is(text, "yes") || Is(text, "on") || text == "1")
        {
            value = true;
            return true;
        }

        if (Is(text, "false") || Is(text, "no") || Is(text, "off") || text == "0")
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    public static bool TryParseVec2(string text, out Vec2 value)
    {
        value = default;
        var space = text.IndexOf(' ');
        if (space <= 0 || space != text.LastIndexOf(' '))
            return false;

        var left = text[..space];
        var right = text[(space + 1)..];
        if (left.Length == 0
            || right.Length == 0
            || !double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        value = new Vec2(x, y);
        return true;
    }

    public static bool TryParseGradient(string text, out GradientValue gradient)
    {
        gradient = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        BorderColor? first = null;
        var colors = 0;
        double? angle = null;
        foreach (var part in parts)
        {
            if (TryAngle(part, out var degrees))
            {
                angle = degrees;
                continue;
            }

            if (!TryColor(part, out var parsed))
                return false;

            first ??= parsed;
            colors++;
        }

        if (first is null)
            return false;

        gradient = new GradientValue(first.Value, angle ?? 0, angle is not null, colors);
        return true;
    }

    /// <summary>Every color of a gradient value, in the order the value lists them. Its angle is skipped.</summary>
    public static bool TryParseColors(string text, out BorderColor[] colors)
    {
        colors = [];
        if (!TryParseGradient(text, out _))
            return false;

        var found = new List<BorderColor>();
        foreach (var part in text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryColor(part, out var parsed))
                found.Add(parsed);
        }

        colors = [.. found];
        return true;
    }

    private static bool TryColor(string token, out BorderColor color)
    {
        if (TryWrapped(token, "rgba", 8, out var rgba))
        {
            color = new BorderColor((byte)rgba, (byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8));
            return true;
        }

        if (TryWrapped(token, "rgb", 6, out var rgb))
        {
            color = new BorderColor(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return true;
        }

        if (token.Length == 10
            && token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            color = new BorderColor((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            return true;
        }

        color = default;
        return false;
    }

    private static bool TryWrapped(string token, string name, int digits, out uint value)
    {
        value = 0;
        if (token.Length < name.Length + 2 + digits
            || !token.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            || token[name.Length] != '('
            || token[^1] != ')')
            return false;

        var hex = token.AsSpan(name.Length + 1, token.Length - name.Length - 2).Trim();
        return hex.Length == digits
            && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryAngle(string token, out double degrees)
    {
        degrees = 0;
        if (token.Length < 4 || !token.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
            return false;

        return double.TryParse(token.AsSpan(0, token.Length - 3), NumberStyles.Float, CultureInfo.InvariantCulture, out degrees);
    }
}

public readonly record struct Vec2(double X, double Y);

public readonly record struct GradientValue(BorderColor Color, double AngleDegrees, bool HasAngle, int Colors);
