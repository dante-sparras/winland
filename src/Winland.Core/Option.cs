using System.Globalization;
using System.Text.Json;

namespace Winland.Core;

internal delegate bool OptionReader<T>(string text, out T value);

/// <summary>How one type of Option value is read from a config line, and written in a <c>getoption</c> reply.</summary>
internal sealed record OptionValue<T>(string Kind, OptionReader<T> Read, Func<T, string> Json);

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
        (string text, out BorderColor value) => BorderColor.TryParse(text.Trim(), out value, out _),
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
        if (parts.Length == 0)
            return false;

        var count = Math.Min(parts.Length, 4);
        var values = new int[count];
        for (var i = 0; i < count; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                return false;
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

/// <summary>An Option the config may set: its name, and whether a value reads as its type.</summary>
internal interface IOption
{
    string Name { get; }

    bool Reads(string text);
}

/// <summary>One Option of a record of options: its name, its value type, and the field of the record it sets.</summary>
internal sealed class Option<TOptions> : IOption
    where TOptions : class
{
    private readonly Func<string, bool> _reads;
    private readonly Func<TOptions, string, TOptions?> _apply;
    private readonly Func<TOptions, string> _reply;

    private Option(string name, Func<string, bool> reads, Func<TOptions, string, TOptions?> apply, Func<TOptions, string> reply)
    {
        Name = name;
        _reads = reads;
        _apply = apply;
        _reply = reply;
    }

    public string Name { get; }

    public static Option<TOptions> Of<T>(string name, OptionValue<T> value, Func<TOptions, T> get, Func<TOptions, T, TOptions> set)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Option<TOptions>(
            name,
            text => value.Read(text, out _),
            (options, text) => value.Read(text, out var read) ? set(options, read) : null,
            options => "\"" + value.Kind + "\":" + value.Json(get(options)));
    }

    public bool Reads(string text) => _reads(text);

    /// <summary><paramref name="options"/> with this Option set to <paramref name="text"/>, or null when the value does not read.</summary>
    public TOptions? Apply(TOptions options, string text) => _apply(options, text);

    /// <summary>The value's type and the value in <paramref name="options"/>, as <c>"int":4</c>.</summary>
    public string Reply(TOptions options) => _reply(options);
}

/// <summary>The Options one record of options declares, and the record a config's lines make of them.</summary>
internal sealed class OptionSet<TOptions>
    where TOptions : class
{
    private readonly Dictionary<string, Option<TOptions>> _byName;

    public OptionSet(IEnumerable<Option<TOptions>> options) =>
        _byName = options.ToDictionary(option => option.Name, StringComparer.Ordinal);

    public IEnumerable<IOption> All => _byName.Values;

    /// <summary><paramref name="options"/> with every line of <paramref name="document"/> that sets one of these Options, in order.</summary>
    public TOptions From(TOptions options, ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (var setting in document.Settings)
        {
            if (_byName.TryGetValue(setting.Keyword, out var option))
                options = option.Apply(options, setting.Value) ?? options;
        }

        return options;
    }

    /// <summary><paramref name="options"/> with one Option set, or null when there is no such Option or the value does not read.</summary>
    public TOptions? Apply(TOptions options, string name, string text) =>
        _byName.TryGetValue(name, out var option) ? option.Apply(options, text) : null;

    public bool TryReply(string name, TOptions applied, out string reply)
    {
        reply = "";
        if (!_byName.TryGetValue(name, out var option))
            return false;

        reply = option.Reply(applied);
        return true;
    }
}

/// <summary>Every Option Winland reads. A line that names anything else warns.</summary>
internal static class OptionTable
{
    private static readonly Dictionary<string, IOption> ByName = Build();

    internal static IEnumerable<string> Names => ByName.Keys;

    internal static IOption? Find(string name) => ByName.GetValueOrDefault(name);

    /// <summary>The <c>getoption</c> reply for an Option whose value <paramref name="value"/> wrote.</summary>
    internal static string Reply(string name, string value, bool set) =>
        "{\"option\":" + OptionValue.Quote(name) + "," + value + ",\"set\":" + (set ? "true" : "false") + "}";

    private static Dictionary<string, IOption> Build()
    {
        var table = new Dictionary<string, IOption>(StringComparer.Ordinal);
        IEnumerable<IOption>[] sets =
        [
            LayoutOptions.Options.All,
            ScrollingOptions.Options.All,
            MasterOptions.Options.All,
            DecorationOptions.Options.All,
            GroupOptions.Options.All,
            SessionOptions.Options.All,
            BindOptions.Options.All,
            CursorOptions.Options.All,
            InputOptions.Options.All,
            GestureOptions.Options.All,
            AnimationOptions.Options.All,
            DebugOptions.Options.All,
        ];
        foreach (var set in sets)
        {
            foreach (var option in set)
                table.Add(option.Name, option);
        }

        return table;
    }
}
