using System.Globalization;

namespace Winland.Domain;

/// <summary>How a window property's value is read, written and shown.</summary>
internal enum PropKind
{
    Bool,
    Int,
    Float,
    Text,
    Size,
    Alpha,
    AlphaOverride,
    Color,
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

/// <summary>One opacity fraction and whether it replaces the configured one, as a window's opacity props hold it.</summary>
internal readonly record struct PropAlpha(float Value, bool Overridden)
{
    public static PropAlpha Identity { get; } = new(1f, false);
}

/// <summary>
/// The window properties of Winland's <c>getprop</c> and <c>setprop</c>: their kinds, and how a value is
/// read from text and shown. A property's value comes from <c>setprop</c>, then the window rules, then the config.
/// </summary>
internal static class WindowProps
{
    private static readonly Dictionary<string, PropKind> Kinds = new(StringComparer.Ordinal)
    {
        ["animation"] = PropKind.Text,
        ["max_size"] = PropKind.Size,
        ["min_size"] = PropKind.Size,
        ["opacity"] = PropKind.Alpha,
        ["opacity_inactive"] = PropKind.Alpha,
        ["opacity_fullscreen"] = PropKind.Alpha,
        ["opacity_override"] = PropKind.AlphaOverride,
        ["opacity_inactive_override"] = PropKind.AlphaOverride,
        ["opacity_fullscreen_override"] = PropKind.AlphaOverride,
        ["active_border_color"] = PropKind.Color,
        ["inactive_border_color"] = PropKind.Color,
        ["allows_input"] = PropKind.Bool,
        ["decorate"] = PropKind.Bool,
        ["focus_on_activate"] = PropKind.Bool,
        ["keep_aspect_ratio"] = PropKind.Bool,
        ["nearest_neighbor"] = PropKind.Bool,
        ["no_anim"] = PropKind.Bool,
        ["no_blur"] = PropKind.Bool,
        ["no_focus"] = PropKind.Bool,
        ["no_max_size"] = PropKind.Bool,
        ["no_shortcuts_inhibit"] = PropKind.Bool,
        ["opaque"] = PropKind.Bool,
        ["force_rgbx"] = PropKind.Bool,
        ["sync_fullscreen"] = PropKind.Bool,
        ["immediate"] = PropKind.Bool,
        ["render_unfocused"] = PropKind.Bool,
        ["no_follow_mouse"] = PropKind.Bool,
        ["no_screen_share"] = PropKind.Bool,
        ["no_auto_hdr"] = PropKind.Bool,
        ["persistent_size"] = PropKind.Bool,
        ["stay_focused"] = PropKind.Bool,
        ["border_size"] = PropKind.Int,
        ["rounding"] = PropKind.Int,
        ["rounding_power"] = PropKind.Float,
        ["scroll_mouse"] = PropKind.Float,
        ["scroll_touchpad"] = PropKind.Float,
    };

    // Properties that cannot go below zero, as Winland clamps them.
    private static readonly HashSet<string> NonNegative = new(StringComparer.Ordinal) { "border_size", "rounding" };

    public static bool TryKind(string name, out PropKind kind) => Kinds.TryGetValue(name, out kind);

    /// <summary>The key a property is stored under. An <c>_override</c> flag lives in the opacity it belongs to.</summary>
    public static string StorageKey(string name) =>
        name.EndsWith("_override", StringComparison.Ordinal) ? name[..^"_override".Length] : name;

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
            case PropKind.Float:
                if (text.StartsWith("relative", StringComparison.Ordinal))
                {
                    if (!TryFloat(After(text), out var change))
                        return false;
                    next = (float)effective + change;
                    return true;
                }

                if (!TryFloat(text, out var value))
                    return false;
                next = value;
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
        return tokens.Length == 1 && BorderColor.TryParse(tokens[0], out color, out _);
    }

    /// <summary>
    /// Winland's integer reading: hexadecimal after <c>0x</c>, 1 for text starting with <c>true</c>, <c>on</c>
    /// or <c>yes</c>, 0 for <c>false</c>, <c>off</c> or <c>no</c>, and otherwise a whole number.
    /// </summary>
    public static bool TryInteger(string text, out long value)
    {
        value = 0;
        if (text.StartsWith("0x", StringComparison.Ordinal))
            return long.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);

        if (text.StartsWith("true", StringComparison.Ordinal) || text.StartsWith("on", StringComparison.Ordinal) || text.StartsWith("yes", StringComparison.Ordinal))
        {
            value = 1;
            return true;
        }

        if (text.StartsWith("false", StringComparison.Ordinal) || text.StartsWith("off", StringComparison.Ordinal) || text.StartsWith("no", StringComparison.Ordinal))
            return true;

        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
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
        PropKind.AlphaOverride => Wrap(name, ((PropAlpha)value).Overridden ? "true" : "false", json),
        PropKind.Color => json ? $"{{\"{name}\": \"{Hex((BorderColor)value)}\"}}" : Hex((BorderColor)value),
        PropKind.Bool => Wrap(name, (bool)value ? "true" : "false", json),
        PropKind.Int => Wrap(name, ((long)value).ToString(CultureInfo.InvariantCulture), json),
        PropKind.Float => Wrap(name, Number((float)value), json),
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
