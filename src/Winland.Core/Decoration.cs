using System.Globalization;

namespace Winland;

public enum CornerPreference
{
    DoNotRound,
    Round,
}

public readonly record struct Decoration(
    ClientId Client,
    BorderColor Border,
    CornerPreference Corners,
    byte Alpha = 255,
    bool Borderless = false);

/// <summary>One opacity fraction from a window rule. <see cref="Overridden"/> replaces the configured fraction; otherwise the two multiply.</summary>
public readonly record struct OpacityFactor(float Value, bool Overridden)
{
    public static OpacityFactor Identity { get; } = new(1f, false);

    public float Apply(float configured) => Overridden ? Value : Value * configured;
}

/// <summary>Active, inactive and fullscreen fractions from one <c>opacity</c> rule.</summary>
public readonly record struct OpacityRule(OpacityFactor Active, OpacityFactor Inactive, OpacityFactor Fullscreen);

public sealed record DecorationOptions
{
    public static DecorationOptions Documented { get; } = new()
    {
        ActiveBorder = new BorderColor(0xFF, 0xFF, 0xFF, 0xFF),
        InactiveBorder = new BorderColor(0xFF, 0x44, 0x44, 0x44),
        Corners = CornerPreference.DoNotRound,
    };

    public BorderColor ActiveBorder { get; init; }

    public BorderColor InactiveBorder { get; init; }

    public CornerPreference Corners { get; init; }

    public float ActiveOpacity { get; init; } = 1;

    public float InactiveOpacity { get; init; } = 1;

    public float FullscreenOpacity { get; init; } = 1;

    public bool DimInactive { get; init; }

    public float DimStrength { get; init; } = 0.5f;

    public bool DimModal { get; init; } = true;

    public float DimSpecial { get; init; } = 0.2f;

    // Adding an entry here applies that keyword and stops the config warning.
    private static readonly Dictionary<string, Func<DecorationOptions, string, DecorationOptions>> Appliers = new(StringComparer.Ordinal)
    {
        ["general:col.active_border"] = (options, value) => BorderColor.TryParse(value, out var active, out _) ? options with { ActiveBorder = active } : options,
        ["general:col.inactive_border"] = (options, value) => BorderColor.TryParse(value, out var inactive, out _) ? options with { InactiveBorder = inactive } : options,
        ["decoration:rounding"] = (options, value) => TryParseRounding(value, out var corners) ? options with { Corners = corners } : options,
        ["decoration:active_opacity"] = (options, value) => TryParseUnit(value, out var opacity) ? options with { ActiveOpacity = opacity } : options,
        ["decoration:inactive_opacity"] = (options, value) => TryParseUnit(value, out var opacity) ? options with { InactiveOpacity = opacity } : options,
        ["decoration:fullscreen_opacity"] = (options, value) => TryParseUnit(value, out var opacity) ? options with { FullscreenOpacity = opacity } : options,
        ["decoration:dim_inactive"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { DimInactive = enabled } : options,
        ["decoration:dim_strength"] = (options, value) => TryParseUnit(value, out var strength) ? options with { DimStrength = strength } : options,
        ["decoration:dim_modal"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { DimModal = enabled } : options,
        ["decoration:dim_special"] = (options, value) => TryParseUnit(value, out var strength) ? options with { DimSpecial = strength } : options,
    };

    internal static IReadOnlySet<string> Keywords { get; } = Appliers.Keys.ToHashSet(StringComparer.Ordinal);

    public static DecorationOptions From(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = Documented;
        foreach (var setting in document.Settings)
            options = Apply(options, setting);
        return options;
    }

    private static DecorationOptions Apply(DecorationOptions options, Setting setting) =>
        Appliers.TryGetValue(setting.Keyword, out var apply) ? apply(options, setting.Value) : options;

    private static bool TryParseRounding(string text, out CornerPreference corners)
    {
        corners = CornerPreference.DoNotRound;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
            return false;

        corners = value == 0 ? CornerPreference.DoNotRound : CornerPreference.Round;
        return true;
    }

    private static bool TryParseUnit(string text, out float value)
    {
        value = 0;
        if (!float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !float.IsFinite(parsed))
            return false;

        value = Math.Clamp(parsed, 0f, 1f);
        return true;
    }
}
