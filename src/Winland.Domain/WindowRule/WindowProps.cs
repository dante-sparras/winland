using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// The window properties of Winland's <c>getprop</c> and <c>setprop</c>: their kinds, and how a value is
/// read from text and shown. A property's value comes from <c>setprop</c>, then the window rules, then the config.
/// </summary>
internal static class WindowProps
{
    private static readonly Dictionary<string, WindowProp> ByName =
        WindowProp.All.ToDictionary(prop => prop.Name, StringComparer.Ordinal);

    // Properties that cannot go below zero, as Winland clamps them.
    private static readonly HashSet<string> NonNegative = new(StringComparer.Ordinal) { "border_size", "rounding" };

    public static bool TryKind(string name, out PropKind kind)
    {
        var found = ByName.TryGetValue(name, out var prop);
        kind = found ? prop!.Kind : default;
        return found;
    }

    /// <summary>Whether a window rule named <paramref name="name"/> only sets that <paramref name="kind"/> of property.</summary>
    public static bool IsRuleEffect(string name, PropKind kind) =>
        ByName.TryGetValue(name, out var prop) && prop.IsRuleEffect && prop.Kind == kind;

    /// <summary>Winland's truthy: <c>1</c>, or text starting with <c>true</c>, <c>yes</c> or <c>on</c>.</summary>
    public static bool Truthy(string text)
    {
        if (text == "1")
            return true;

        var lower = text.ToLowerInvariant();
        return lower.StartsWith("true", StringComparison.Ordinal)
            || lower.StartsWith("yes", StringComparison.Ordinal)
            || lower.StartsWith("on", StringComparison.Ordinal);
    }

    /// <summary>
    /// The new value of a bool, int, float or text property, as <c>setprop</c> reads it. <paramref name="layered"/> is
    /// the value <c>setprop</c> or a rule holds, if any, and <paramref name="effective"/> what the property is now,
    /// the config's value included. Returns false when the text does not parse, which changes nothing. A null
    /// <paramref name="next"/> unsets the <c>setprop</c> value.
    /// </summary>
    public static bool TryTrivial(string name, PropKind kind, string text, object? layered, object effective, out object? next)
    {
        next = null;
        if (text == "unset")
            return true;

        switch (kind)
        {
            case PropKind.Bool:
                // Toggling flips what a layer holds, or false when none holds anything.
                next = text == "toggle" ? layered is not true : Truthy(text);
                return true;
            case PropKind.Int:
                if (text.StartsWith("relative", StringComparison.Ordinal))
                {
                    if (!long.TryParse(After(text), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var step))
                        return false;
                    next = Clamp(name, (long)effective + step);
                    return true;
                }

                if (!long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
                    return false;
                next = Clamp(name, whole);
                return true;
            case PropKind.Text:
                next = text;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// One border color as <c>setprop</c> reads it. A gradient, an angle, or text that is not one color
    /// changes nothing.
    /// </summary>
    public static bool TryReadColor(string text, out BorderColor color)
    {
        color = default;
        var tokens = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 1 && BorderColor.TryParse(tokens[0], out color);
    }

    public static bool TryFloat(string text, out float value) =>
        float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    /// <summary>The reply of <c>getprop</c>, as plain text or as Winland's one-member JSON object.</summary>
    public static string Format(string name, PropKind kind, object value, bool json, bool animationSet = true) => kind switch
    {
        PropKind.Text when !json => animationSet ? (string)value : "(unset)",
        PropKind.Text => $"{{\"{name}\": \"{Escape((string)value)}\"}}",
        PropKind.Size => FormatSize(name, (PropSize)value, json),
        PropKind.Alpha => Wrap(name, Number(((PropAlpha)value).Value), json),
        PropKind.Color => json ? $"{{\"{name}\": \"{Hex((BorderColor)value)}\"}}" : Hex((BorderColor)value),
        PropKind.Bool => Wrap(name, (bool)value ? "true" : "false", json),
        PropKind.Int => Wrap(name, ((long)value).ToString(CultureInfo.InvariantCulture), json),
        _ => "",
    };

    private static string FormatSize(string name, PropSize size, bool json)
    {
        if (!json)
            return $"{Number(size.Width)} {Number(size.Height)}";

        static string Side(double value) => double.IsPositiveInfinity(value) ? "null" : Number(value);
        return $"{{\"{name}\": [{Side(size.Width)},{Side(size.Height)}]}}";
    }

    private static string Wrap(string name, string value, bool json) => json ? $"{{\"{name}\": {value}}}" : value;

    // Hexadecimal ARGB, as a border color is shown, with no angle.
    private static string Hex(BorderColor color)
    {
        var argb = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        return argb.ToString("x", CultureInfo.InvariantCulture);
    }

    // The shortest text that reads back as the same number, with inf for infinity, as std::format writes it.
    private static string Number(double value) =>
        double.IsPositiveInfinity(value) ? "inf" : value.ToString("R", CultureInfo.InvariantCulture);

    private static string Number(float value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string After(string text)
    {
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? "" : text[(space + 1)..];
    }

    private static long Clamp(string name, long value) => NonNegative.Contains(name) ? Math.Max(0, value) : value;
}

/// <summary>One opacity fraction, as a window's opacity props hold it.</summary>
internal readonly record struct PropAlpha(float Value)
{
    public static PropAlpha Identity { get; } = new(1f);
}

/// <summary>A min_size or max_size: a width and height in pixels, either of them infinite.</summary>
internal readonly record struct PropSize(double Width, double Height)
{
    public static PropSize Unbounded { get; } = new(double.PositiveInfinity, double.PositiveInfinity);

    /// <summary>The size as a Layout limit. An infinite or out of range side is no limit.</summary>
    public Size? ToLimit() =>
        double.IsFinite(Width) && double.IsFinite(Height) && Width is >= 0 and <= int.MaxValue && Height is >= 0 and <= int.MaxValue
            ? new Size((int)Math.Round(Width), (int)Math.Round(Height))
            : null;
}
