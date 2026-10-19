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

    /// <summary>The border Winland draws outside the window, or null for none.</summary>
    public BorderRing? Ring { get; init; }

    /// <summary>The shadow Winland draws under the window, or null for none.</summary>
    public DropShadow? DrawnShadow { get; init; }
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

    /// <summary>Every color of <c>general:col.active_border</c> and its angle. Null for the one color of <see cref="ActiveBorder"/>.</summary>
    public BorderGradient? ActiveBorderGradient { get; init; }

    public BorderGradient? InactiveBorderGradient { get; init; }

    /// <summary>The corner radius in logical pixels. 0 draws square corners.</summary>
    public int Rounding { get; init; }

    /// <summary>The corner curve: 1 is a straight cut, 2 is a circle, larger values are squarer. Clamped to 1 through 10.</summary>
    public float RoundingPower { get; init; } = 2;

    public bool Blur { get; init; } = true;

    public bool Shadow { get; init; } = true;

    /// <summary>
    /// Whether the config set any shadow option past <c>decoration:shadow:enabled</c>. Winland then draws the shadow
    /// itself and turns DWM's off. Otherwise DWM's shadow stays.
    /// </summary>
    public bool ShadowDrawn { get; init; }

    /// <summary><c>decoration:shadow:range</c>: how far the shadow reaches past the border, in logical pixels.</summary>
    public int ShadowRange { get; init; } = 4;

    /// <summary><c>decoration:shadow:render_power</c>: how fast the shadow falls off, from 1 to 4.</summary>
    public int ShadowPower { get; init; } = 3;

    /// <summary><c>decoration:shadow:sharp</c>: a flat shadow in its first color, with no falloff.</summary>
    public bool ShadowSharp { get; init; }

    /// <summary><c>decoration:shadow:color</c>.</summary>
    public BorderGradient ShadowColor { get; init; } = BorderGradient.Of(new BorderColor(0xEE, 0x1A, 0x1A, 0x1A));

    /// <summary><c>decoration:shadow:color_inactive</c>, or null to use <see cref="ShadowColor"/> on every window.</summary>
    public BorderGradient? ShadowColorInactive { get; init; }

    /// <summary><c>decoration:shadow:offset</c>, in logical pixels.</summary>
    public Vec2 ShadowOffset { get; init; }

    /// <summary><c>decoration:shadow:scale</c>, from 0.05 to 2. The shadow is drawn at no more than 1.</summary>
    public float ShadowScale { get; init; } = 1;

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
        ["general:col.active_border"] = (options, value) => BorderColor.TryParse(value, out var active, out _) && BorderGradient.TryParse(value, out var activeGradient)
            ? options with { ActiveBorder = active, ActiveBorderGradient = activeGradient }
            : options,
        ["general:col.inactive_border"] = (options, value) => BorderColor.TryParse(value, out var inactive, out _) && BorderGradient.TryParse(value, out var inactiveGradient)
            ? options with { InactiveBorder = inactive, InactiveBorderGradient = inactiveGradient }
            : options,
        ["decoration:rounding"] = (options, value) => TryParseRounding(value, out var rounding) ? options with { Rounding = rounding } : options,
        ["decoration:rounding_power"] = (options, value) => TryParsePower(value, out var power) ? options with { RoundingPower = power } : options,
        ["decoration:blur:enabled"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { Blur = enabled } : options,
        ["decoration:shadow:enabled"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { Shadow = enabled } : options,
        ["decoration:shadow:range"] = (options, value) => TryParseWhole(value, 0, 100, out var range) ? options with { ShadowRange = range, ShadowDrawn = true } : options,
        ["decoration:shadow:render_power"] = (options, value) => TryParseWhole(value, 1, 4, out var power) ? options with { ShadowPower = power, ShadowDrawn = true } : options,
        ["decoration:shadow:sharp"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var sharp) ? options with { ShadowSharp = sharp, ShadowDrawn = true } : options,
        ["decoration:shadow:color"] = (options, value) => BorderGradient.TryParse(value, out var color) ? options with { ShadowColor = color, ShadowDrawn = true } : options,
        ["decoration:shadow:color_inactive"] = (options, value) => BorderGradient.TryParse(value, out var color) ? options with { ShadowColorInactive = color, ShadowDrawn = true } : options,
        ["decoration:shadow:offset"] = (options, value) => TryParseOffset(value, out var offset) ? options with { ShadowOffset = offset, ShadowDrawn = true } : options,
        ["decoration:shadow:scale"] = (options, value) => TryParseScale(value, out var scale) ? options with { ShadowScale = scale, ShadowDrawn = true } : options,
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

    private static bool TryParseWhole(string text, int min, int max, out int value)
    {
        value = 0;
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return false;

        value = Math.Clamp(parsed, min, max);
        return true;
    }

    // Each axis from -250 to 250, as the pinned release's validator allows.
    private static bool TryParseOffset(string text, out Vec2 offset)
    {
        offset = default;
        if (!ConfigValueSyntax.TryParseVec2(text.Trim(), out var parsed) || !double.IsFinite(parsed.X) || !double.IsFinite(parsed.Y))
            return false;

        offset = new Vec2(Math.Clamp(parsed.X, -250, 250), Math.Clamp(parsed.Y, -250, 250));
        return true;
    }

    private static bool TryParseScale(string text, out float scale)
    {
        scale = 0;
        if (!float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !float.IsFinite(parsed))
            return false;

        scale = Math.Clamp(parsed, 0.05f, 2f);
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
