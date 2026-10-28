using System.Globalization;
using System.Text.Json;

namespace Winland.Domain;

/// <summary>The value types Options are declared with. A number outside its range is clamped.</summary>
internal static class OptionValue
{
    internal static OptionValue<bool> Bool { get; } = new(
        "bool",
        (string text, out bool value) => ConfigValueSyntax.TryParseBool(text.Trim(), out value),
        value => value ? "true" : "false");

    /// <summary>The reference config writes an unset string as <c>[[EMPTY]]</c>, which reads as an empty one.</summary>
    internal static OptionValue<string> Text { get; } = new(
        "str",
        (string text, out string value) =>
        {
            value = Unset(text.Trim());
            return true;
        },
        Quote);

    internal static OptionValue<BorderColor> Color { get; } = new(
        "str",
        (string text, out BorderColor value) => BorderColor.TryParse(text.Trim(), out value),
        value => Quote(Hex(value)));

    /// <summary>Every color a gradient lists. Its angle is read and not kept.</summary>
    internal static OptionValue<BorderColor[]> Colors { get; } = new(
        "gradient",
        (string text, out BorderColor[] value) => ConfigValueSyntax.TryParseColors(text, out value) && value.Length > 0,
        value => Quote(string.Join(' ', value.Select(Hex))));

    /// <summary>One to four edges, as CSS writes them: top, right, bottom, left.</summary>
    internal static OptionValue<EdgeInsets> Gaps { get; } = new("css", TryGaps, value => Quote(string.Create(
        CultureInfo.InvariantCulture,
        $"{value.Top} {value.Right} {value.Bottom} {value.Left}")));

    internal static OptionValue<int> Int(int min = int.MinValue, int max = int.MaxValue, IReadOnlyDictionary<string, int>? names = null) => new(
        "int",
        (string text, out int value) =>
        {
            text = text.Trim();
            if (names is not null && names.TryGetValue(text, out value))
                return true;

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return false;

            value = Math.Clamp(value, min, max);
            return true;
        },
        value => value.ToString(CultureInfo.InvariantCulture));

    internal static OptionValue<double> Number(double min = double.MinValue, double max = double.MaxValue) => new(
        "float",
        (string text, out double value) =>
        {
            if (!TryDouble(text, out value))
                return false;

            value = Math.Clamp(value, min, max);
            return true;
        },
        value => JsonSerializer.Serialize(value));

    internal static OptionValue<float> Single(float min, float max) => new(
        "float",
        (string text, out float value) =>
        {
            value = 0;
            if (!TryDouble(text, out var parsed))
                return false;

            value = Math.Clamp((float)parsed, min, max);
            return true;
        },
        value => JsonSerializer.Serialize(value));

    /// <summary>Two numbers split by one space, each clamped to the range.</summary>
    internal static OptionValue<Vec2> Pair(double min, double max) => new(
        "vec2",
        (string text, out Vec2 value) =>
        {
            if (!ConfigValueSyntax.TryParseVec2(text.Trim(), out value))
                return false;

            value = new Vec2(Math.Clamp(value.X, min, max), Math.Clamp(value.Y, min, max));
            return true;
        },
        value => JsonSerializer.Serialize(new[] { value.X, value.Y }));

    /// <summary>One of <paramref name="names"/>, in any case. The reply names it as the first name that maps to it.</summary>
    internal static OptionValue<T> Named<T>(IReadOnlyDictionary<string, T> names)
        where T : struct => new(
        "str",
        (string text, out T value) => names.TryGetValue(text.Trim(), out value),
        value => Quote(names.First(pair => EqualityComparer<T>.Default.Equals(pair.Value, value)).Key));

    /// <summary>One of <paramref name="choices"/>, in any case. It is kept as the choice spells it.</summary>
    internal static OptionValue<string> OneOf(params string[] choices) => new(
        "str",
        (string text, out string value) =>
        {
            var trimmed = text.Trim();
            value = choices.FirstOrDefault(choice => choice.Equals(trimmed, StringComparison.OrdinalIgnoreCase)) ?? "";
            return value.Length > 0;
        },
        Quote);

    internal static OptionValue<T> Of<T>(string kind, OptionReader<T> read, Func<T, string> json) => new(kind, read, json);

    internal static string Quote(string text) => JsonSerializer.Serialize(text);

    private static string Unset(string text) => text == "[[EMPTY]]" ? "" : text;

    private static string Hex(BorderColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"0x{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");

    private static bool TryDouble(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static bool TryGaps(string text, out EdgeInsets gaps)
    {
        gaps = EdgeInsets.Zero;
        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 4)
            return false;

        // A gap below zero would overlap the tiles, so it is clamped to zero as other numbers are clamped to their range.
        var count = parts.Length;
        var values = new int[count];
        for (var i = 0; i < count; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                return false;

            values[i] = Math.Max(0, values[i]);
        }

        gaps = count switch
        {
            1 => EdgeInsets.Uniform(values[0]),
            2 => new EdgeInsets(values[0], values[1], values[0], values[1]),
            3 => new EdgeInsets(values[0], values[1], values[2], values[1]),
            _ => new EdgeInsets(values[0], values[1], values[2], values[3]),
        };
        return true;
    }
}

/// <summary>How one type of Option value is read from a config line, and written in a <c>getoption</c> reply.</summary>
internal sealed record OptionValue<T>(string Kind, OptionReader<T> Read, Func<T, string> Json);

internal delegate bool OptionReader<T>(string text, out T value);
