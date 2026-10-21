using System.Globalization;
using System.Text.Json;

namespace Winland;

internal static partial class OptionCatalog
{
    private enum CatalogKind
    {
        Bool,
        Int,
        Float,
        String,
        Vec2,
        Css,
        Color,
        Gradient,
        Weight,
    }

    private sealed record CatalogOption(string Name, string Description, CatalogKind Kind)
    {
        public long Integer { get; init; }

        public float X { get; init; }

        public float Y { get; init; }

        public string? Text { get; init; }

        public bool HasMin { get; init; }

        public bool HasMax { get; init; }

        public long Min { get; init; }

        public long Max { get; init; }

        public float MinF { get; init; }

        public float MaxF { get; init; }

        public bool HasVec { get; init; }

        public float VecMinX { get; init; }

        public float VecMinY { get; init; }

        public float VecMaxX { get; init; }

        public float VecMaxY { get; init; }

        public (string Key, long Value)[]? Map { get; init; }

        public string[]? Choices { get; init; }
    }

    private readonly record struct Resolved(bool Set, string Option);

    private static readonly Dictionary<string, CatalogOption> ByName;

    private static readonly (string Name, long Value)[] Weights =
    [
        ("thin", 100),
        ("ultralight", 200),
        ("light", 300),
        ("book", 380),
        ("normal", 400),
        ("medium", 500),
        ("semibold", 600),
        ("bold", 700),
        ("ultrabold", 800),
        ("heavy", 900),
        ("ultraheavy", 1000),
    ];

    static OptionCatalog()
    {
        ByName = new Dictionary<string, CatalogOption>(Table.Length, StringComparer.Ordinal);
        foreach (var option in Table)
        {
            if (!ByName.TryAdd(option.Name, option))
                throw new InvalidOperationException($"Duplicate config option '{option.Name}'.");
        }
    }

    private static CatalogOption Bool(string name, string description, bool value) =>
        new(name, description, CatalogKind.Bool) { Integer = value ? 1 : 0 };

    private static CatalogOption Int(
        string name,
        string description,
        long value,
        long? min = null,
        long? max = null,
        (string Key, long Value)[]? map = null) =>
        new(name, description, CatalogKind.Int)
        {
            Integer = value,
            HasMin = min is not null,
            HasMax = max is not null,
            Min = min ?? 0,
            Max = max ?? 0,
            Map = map,
        };

    private static CatalogOption Float(string name, string description, float value, float? min = null, float? max = null) =>
        new(name, description, CatalogKind.Float)
        {
            X = value,
            HasMin = min is not null,
            HasMax = max is not null,
            MinF = min ?? 0,
            MaxF = max ?? 0,
        };

    private static CatalogOption Str(string name, string description, string value, string[]? choices = null) =>
        new(name, description, CatalogKind.String) { Text = value, Choices = choices };

    private static CatalogOption Vec(
        string name,
        string description,
        float x,
        float y,
        float? minX = null,
        float? minY = null,
        float? maxX = null,
        float? maxY = null) =>
        new(name, description, CatalogKind.Vec2)
        {
            X = x,
            Y = y,
            HasVec = minX is not null || minY is not null || maxX is not null || maxY is not null,
            VecMinX = minX ?? 0,
            VecMinY = minY ?? 0,
            VecMaxX = maxX ?? 0,
            VecMaxY = maxY ?? 0,
        };

    private static CatalogOption Css(string name, string description, long value, long? min = null, long? max = null) =>
        new(name, description, CatalogKind.Css)
        {
            Integer = value,
            HasMin = min is not null,
            HasMax = max is not null,
            Min = min ?? 0,
            Max = max ?? 0,
        };

    private static CatalogOption Color(string name, string description, long packed) =>
        new(name, description, CatalogKind.Color) { Integer = packed };

    private static CatalogOption Gradient(string name, string description, long packed) =>
        new(name, description, CatalogKind.Gradient) { Integer = packed };

    private static CatalogOption Weight(string name, string description, long value = 400) =>
        new(name, description, CatalogKind.Weight) { Integer = value };

    public static string Describe(string name, ConfigDocument? config)
    {
        ArgumentNullException.ThrowIfNull(name);
        var document = config ?? Empty;
        if (TryValue(name, document, out var kind, out var token))
            return Honored(name, kind, token, InSettings(document, name));

        if (!ByName.TryGetValue(name, out var spec))
            return "no such option";

        var resolved = Resolve(spec, document);
        return ReferenceJson(name, OptionKey(spec.Kind), resolved.Option, resolved.Set);
    }

    // A stored keyword is set even when the value does not apply. A number outside its range is clamped.
    private static Resolved Resolve(CatalogOption spec, ConfigDocument document)
    {
        var set = false;
        var option = DefaultOption(spec);
        foreach (var row in document.Settings)
        {
            if (row.Keyword != spec.Name)
                continue;

            set = true;
            if (TryAccept(spec, row.Value, out var nextOption))
                option = nextOption;
        }

        return new Resolved(set, option);
    }

    private static bool TryAccept(CatalogOption spec, string text, out string option)
    {
        option = "";
        switch (spec.Kind)
        {
            case CatalogKind.Bool:
                if (!ConfigValueSyntax.TryParseBool(text, out var flag))
                    return false;

                option = flag ? "true" : "false";
                return true;
            case CatalogKind.Int:
                if (!TryInt(spec, text, out var number))
                    return false;

                option = Integer(number);
                return true;
            case CatalogKind.Float:
                if (!TryFloat(spec, text, out var numberF))
                    return false;

                option = Fixed6(numberF);
                return true;
            case CatalogKind.String:
                option = Quote(text);
                return true;
            case CatalogKind.Vec2:
                if (!TryVec(spec, text, out var x, out var y))
                    return false;

                option = "[" + FormatGeneral(x) + "," + FormatGeneral(y) + "]";
                return true;
            case CatalogKind.Css:
                if (!TryCss(spec, text, out var css))
                    return false;

                option = Quote(css);
                return true;
            case CatalogKind.Color:
                if (!TryParseColor(text, out var parsed))
                    return false;

                option = Integer((long)GetAsHex(unchecked((ulong)parsed)));
                return true;
            case CatalogKind.Gradient:
                if (!TryParseGradient(text, out var gradient))
                    return false;

                option = Quote(gradient);
                return true;
            case CatalogKind.Weight:
                if (!TryWeight(text, out var weight))
                    return false;

                option = Quote(Integer(weight));
                return true;
            default:
                return false;
        }
    }

    private static bool TryInt(CatalogOption spec, string text, out long value)
    {
        if (spec.Map is not null)
        {
            foreach (var (key, mapped) in spec.Map)
            {
                if (key == text)
                {
                    value = mapped;
                    return true;
                }
            }
        }

        if (text.StartsWith("0x", StringComparison.Ordinal))
        {
            if (!TryHex(text, out var hex) || hex > long.MaxValue)
            {
                value = 0;
                return false;
            }

            value = (long)hex;
        }
        else if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        if (spec.HasMin && value < spec.Min)
            value = spec.Min;
        if (spec.HasMax && value > spec.Max)
            value = spec.Max;
        return true;
    }

    private static bool TryFloat(CatalogOption spec, string text, out float value)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !float.IsFinite(value))
            return false;

        if (spec.HasMin && value < spec.MinF)
            value = spec.MinF;
        if (spec.HasMax && value > spec.MaxF)
            value = spec.MaxF;
        return true;
    }

    private static bool TryVec(CatalogOption spec, string text, out float x, out float y)
    {
        x = 0;
        y = 0;
        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
            || !float.IsFinite(x)
            || !float.IsFinite(y))
            return false;

        if (!spec.HasVec)
            return true;

        x = Math.Clamp(x, spec.VecMinX, spec.VecMaxX);
        y = Math.Clamp(y, spec.VecMinY, spec.VecMaxY);
        return true;
    }

    private static bool TryCss(CatalogOption spec, string text, out string rendered)
    {
        rendered = "";
        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        var count = Math.Min(parts.Length, 4);
        var edges = new long[count];
        for (var i = 0; i < count; i++)
        {
            if (!long.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out edges[i]))
                return false;
        }

        var top = edges[0];
        var right = count > 1 ? edges[1] : top;
        var bottom = count > 2 ? edges[2] : top;
        var left = count > 3 ? edges[3] : right;
        FitEdge(spec, ref top);
        FitEdge(spec, ref right);
        FitEdge(spec, ref bottom);
        FitEdge(spec, ref left);

        rendered = Integer(top) + " " + Integer(right) + " " + Integer(bottom) + " " + Integer(left);
        return true;
    }

    private static void FitEdge(CatalogOption spec, ref long edge)
    {
        if (spec.HasMin && edge < spec.Min)
            edge = spec.Min;
        if (spec.HasMax && edge > spec.Max)
            edge = spec.Max;
    }

    // Names omit semilight. A number is clamped to 100..1000.
    private static bool TryWeight(string text, out long value)
    {
        var name = text.Trim().ToLowerInvariant();
        foreach (var (key, weight) in Weights)
        {
            if (key != name)
                continue;

            value = weight;
            return true;
        }

        if (!long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return false;

        value = Math.Clamp(value, 100, 1000);
        return true;
    }

    private static bool TryParseGradient(string text, out string rendered)
    {
        rendered = "";
        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        var colors = new List<string>();
        var radians = 0f;
        foreach (var part in parts)
        {
            if (part.EndsWith("deg", StringComparison.Ordinal) && part.Length > 3)
            {
                if (!float.TryParse(part[..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees))
                    return false;

                radians = (float)(degrees * Math.PI / 180.0);
                continue;
            }

            if (!TryParseColor(part, out var parsed))
                return false;

            colors.Add(GetAsHex(unchecked((ulong)parsed)).ToString("x", CultureInfo.InvariantCulture));
        }

        if (colors.Count == 0)
            return false;

        var degreesOut = (int)(radians * 180.0 / Math.PI);
        rendered = string.Join(' ', colors) + " " + degreesOut.ToString(CultureInfo.InvariantCulture) + "deg";
        return true;
    }

    private static bool TryParseColor(string text, out long value)
    {
        value = 0;
        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (hex.Length == 3)
            {
                if (!TryHex(hex[..1], out var r) || !TryHex(hex[1..2], out var g) || !TryHex(hex[2..3], out var b))
                    return false;

                value = 0xFF000000L | ((long)(r | (r << 4)) << 16) | ((long)(g | (g << 4)) << 8) | (long)(b | (b << 4));
                return true;
            }

            if (hex.Length == 6)
            {
                if (!TryPair(hex, 0, out var r) || !TryPair(hex, 2, out var g) || !TryPair(hex, 4, out var b))
                    return false;

                value = 0xFF000000L | (r << 16) | (g << 8) | b;
                return true;
            }

            if (hex.Length == 8)
            {
                if (!TryPair(hex, 0, out var r) || !TryPair(hex, 2, out var g) || !TryPair(hex, 4, out var b) || !TryPair(hex, 6, out var a))
                    return false;

                value = (a << 24) | (r << 16) | (g << 8) | b;
                return true;
            }

            return false;
        }

        if (text.StartsWith("0x", StringComparison.Ordinal))
        {
            if (!TryHex(text, out var bits))
                return false;

            value = unchecked((long)bits);
            return true;
        }

        if (text.StartsWith("rgba(", StringComparison.Ordinal) && text.EndsWith(')'))
            return TryRgba(text[5..^1].Trim(), out value);

        if (text.StartsWith("rgb(", StringComparison.Ordinal) && text.EndsWith(')'))
            return TryRgb(text[4..^1].Trim(), out value);

        if (uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var bare))
        {
            value = bare;
            return true;
        }

        return false;
    }

    private static bool TryRgba(string inner, out long value)
    {
        value = 0;
        if (CountChar(inner, ',') == 3)
        {
            var parts = inner.Split(',');
            if (parts.Length != 4
                || !byte.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r)
                || !byte.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var g)
                || !byte.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var b)
                || !float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha))
                return false;

            var scaled = Math.Floor(alpha * 255f);
            if (scaled < 0)
                return false;

            value = ((long)scaled << 24) | ((long)r << 16) | ((long)g << 8) | b;
            return true;
        }

        if (inner.Length == 8 && TryHex(inner, out var rgba))
        {
            value = unchecked((long)((rgba >> 8) + (0x1000000UL * (rgba & 0xFF))));
            return true;
        }

        return false;
    }

    private static bool TryRgb(string inner, out long value)
    {
        value = 0;
        if (CountChar(inner, ',') == 2)
        {
            var parts = inner.Split(',');
            if (parts.Length != 3
                || !byte.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r)
                || !byte.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var g)
                || !byte.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
                return false;

            value = 0xFF000000L | ((long)r << 16) | ((long)g << 8) | b;
            return true;
        }

        if (inner.Length == 6 && TryHex(inner, out var rgb))
        {
            value = unchecked((long)(rgb + 0xFF000000UL));
            return true;
        }

        return false;
    }

    private static bool TryPair(string hex, int start, out long value)
    {
        if (!TryHex(hex.Substring(start, 2), out var bits))
        {
            value = 0;
            return false;
        }

        value = (long)bits;
        return true;
    }

    private static bool TryHex(string text, out ulong bits)
    {
        var body = text.StartsWith("0x", StringComparison.Ordinal) ? text[2..] : text;
        return ulong.TryParse(body, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bits);
    }

    private static int CountChar(string text, char value)
    {
        var count = 0;
        foreach (var character in text)
        {
            if (character == value)
                count++;
        }

        return count;
    }

    // Each channel is a float division by 255, times 255, truncated toward zero.
    private static uint GetAsHex(ulong bits)
    {
        static uint Channel(ulong value, int shift)
        {
            var component = (float)(((value >> shift) & 0xff) / 255.0);
            var scaled = component * 255f;
            return scaled <= 0 ? 0 : (uint)scaled;
        }

        return (Channel(bits, 24) << 24)
            | (Channel(bits, 16) << 16)
            | (Channel(bits, 8) << 8)
            | Channel(bits, 0);
    }

    private static string DefaultToken(CatalogOption spec) => spec.Kind switch
    {
        CatalogKind.Bool => spec.Integer == 0 ? "false" : "true",
        CatalogKind.Int => Integer(spec.Integer),
        CatalogKind.Float => FormatGeneral(spec.X),
        CatalogKind.String => Quote(spec.Text ?? ""),
        CatalogKind.Vec2 => "[" + FormatGeneral(spec.X) + ", " + FormatGeneral(spec.Y) + "]",
        CatalogKind.Css => Quote(CssDefault(spec.Integer)),
        CatalogKind.Color => Quote(spec.Integer.ToString("x", CultureInfo.InvariantCulture)),
        CatalogKind.Gradient => Quote(GradientDefault(spec.Integer)),
        CatalogKind.Weight => Quote(Integer(spec.Integer)),
        _ => "null",
    };

    private static string DefaultOption(CatalogOption spec) => spec.Kind switch
    {
        CatalogKind.Float => Fixed6(spec.X),
        CatalogKind.Vec2 => "[" + FormatGeneral(spec.X) + "," + FormatGeneral(spec.Y) + "]",
        CatalogKind.Color => Integer(spec.Integer),
        _ => DefaultToken(spec),
    };

    private static string CssDefault(long edge) => Integer(edge) + " " + Integer(edge) + " " + Integer(edge) + " " + Integer(edge);

    private static string GradientDefault(long packed) =>
        GetAsHex(unchecked((ulong)packed)).ToString("x", CultureInfo.InvariantCulture) + " 0deg";

    private static string OptionKey(CatalogKind kind) => kind switch
    {
        CatalogKind.Bool => "bool",
        CatalogKind.Int or CatalogKind.Color => "int",
        CatalogKind.Float => "float",
        CatalogKind.String => "str",
        CatalogKind.Vec2 => "vec2",
        CatalogKind.Css => "css",
        CatalogKind.Gradient => "gradient",
        CatalogKind.Weight => "font_weight",
        _ => "custom",
    };

    private static string Honored(string name, string kind, string token, bool set) =>
        "{\"option\":" + JsonSerializer.Serialize(name) + ",\"" + kind + "\":" + token + ",\"set\":" + (set ? "true" : "false") + "}";

    private static string ReferenceJson(string name, string key, string token, bool set) =>
        "{\"option\": " + JsonSerializer.Serialize(name) + ", \"" + key + "\": " + token + ", \"set\": " + (set ? "true" : "false") + " }";

    private static bool InSettings(ConfigDocument document, string name)
    {
        foreach (var setting in document.Settings)
        {
            if (setting.Keyword == name)
                return true;
        }

        return false;
    }

    private static string FormatGeneral(float value)
    {
        var text = value.ToString("G6", CultureInfo.InvariantCulture);
        return text.Replace('E', 'e');
    }

    private static string Fixed6(float value) => value.ToString("F6", CultureInfo.InvariantCulture);

    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Quote(string text) => JsonSerializer.Serialize(text);
}
