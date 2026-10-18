using System.Globalization;

namespace Winland;

/// <summary>The corner preference DWM draws. <see cref="Round"/> and <see cref="RoundSmall"/> are circular corners of 8 and 4 pixels at 96 DPI.</summary>
public enum CornerPreference
{
    DoNotRound,
    Round,
    RoundSmall,
}

/// <summary>
/// How one Client is decorated. <see cref="Region"/> is set when DWM has no corner of that radius,
/// and the corners are then cut from the window instead. <see cref="Blur"/> asks for a blurred
/// backdrop and a false <see cref="Shadow"/> turns DWM's shadow off.
/// </summary>
public readonly record struct Decoration(
    ClientId Client,
    BorderColor Border,
    CornerPreference Corners,
    byte Alpha = 255,
    bool Borderless = false)
{
    public CornerRegion? Region { get; init; }

    public bool Blur { get; init; }

    public bool Shadow { get; init; } = true;
}

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
    };

    public BorderColor ActiveBorder { get; init; }

    public BorderColor InactiveBorder { get; init; }

    /// <summary>The corner radius in logical pixels. 0 draws square corners.</summary>
    public int Rounding { get; init; }

    /// <summary>The corner curve: 1 is a straight cut, 2 is a circle, larger values are squarer. Clamped to 1 through 10.</summary>
    public float RoundingPower { get; init; } = 2;

    public bool Blur { get; init; } = true;

    public bool Shadow { get; init; } = true;

    public float ActiveOpacity { get; init; } = 1;

    public float InactiveOpacity { get; init; } = 1;

    public float FullscreenOpacity { get; init; } = 1;

    public bool DimInactive { get; init; }

    public float DimStrength { get; init; } = 0.5f;

    public bool DimModal { get; init; } = true;

    public float DimSpecial { get; init; } = 0.2f;

    /// <summary><c>decoration:dim_around</c>: how much a window with the dim_around property dims what lies under it.</summary>
    public float DimAround { get; init; } = 0.4f;

    // Adding an entry here applies that keyword and stops the config warning.
    private static readonly Dictionary<string, Func<DecorationOptions, string, DecorationOptions>> Appliers = new(StringComparer.Ordinal)
    {
        ["general:col.active_border"] = (options, value) => BorderColor.TryParse(value, out var active, out _) ? options with { ActiveBorder = active } : options,
        ["general:col.inactive_border"] = (options, value) => BorderColor.TryParse(value, out var inactive, out _) ? options with { InactiveBorder = inactive } : options,
        ["decoration:rounding"] = (options, value) => TryParseRounding(value, out var rounding) ? options with { Rounding = rounding } : options,
        ["decoration:rounding_power"] = (options, value) => TryParsePower(value, out var power) ? options with { RoundingPower = power } : options,
        ["decoration:blur:enabled"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { Blur = enabled } : options,
        ["decoration:shadow:enabled"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { Shadow = enabled } : options,
        ["decoration:active_opacity"] = (options, value) => TryParseUnit(value, out var opacity) ? options with { ActiveOpacity = opacity } : options,
        ["decoration:inactive_opacity"] = (options, value) => TryParseUnit(value, out var opacity) ? options with { InactiveOpacity = opacity } : options,
        ["decoration:fullscreen_opacity"] = (options, value) => TryParseUnit(value, out var opacity) ? options with { FullscreenOpacity = opacity } : options,
        ["decoration:dim_inactive"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { DimInactive = enabled } : options,
        ["decoration:dim_strength"] = (options, value) => TryParseUnit(value, out var strength) ? options with { DimStrength = strength } : options,
        ["decoration:dim_modal"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { DimModal = enabled } : options,
        ["decoration:dim_special"] = (options, value) => TryParseUnit(value, out var strength) ? options with { DimSpecial = strength } : options,
        ["decoration:dim_around"] = (options, value) => TryParseUnit(value, out var strength) ? options with { DimAround = strength } : options,
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

    private static bool TryParseRounding(string text, out int rounding) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out rounding) && rounding >= 0;

    private static bool TryParsePower(string text, out float power)
    {
        power = 0;
        if (!float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !float.IsFinite(parsed))
            return false;

        power = Math.Clamp(parsed, 1f, 10f);
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
