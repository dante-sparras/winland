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

    /// <summary>The opacity part of <see cref="Alpha"/>, from 0 to 255. <c>fadeSwitch</c> animates it.</summary>
    public byte Opacity { get; init; } = Alpha;

    /// <summary>What dimming leaves of <see cref="Alpha"/>, from 0 to 255, where 255 is not dimmed. <c>fadeDim</c> animates it.</summary>
    public byte Keep { get; init; } = 255;

    public bool Blur { get; init; }

    /// <summary>The blurred wallpaper Winland draws under the window, or null for none. A Client with one has no <see cref="Blur"/>.</summary>
    public XrayBackdrop? Xray { get; init; }

    public bool Shadow { get; init; } = true;

    /// <summary>The border Winland draws outside the window, or null for none.</summary>
    public BorderRing? Ring { get; init; }

    /// <summary>The shadow Winland draws under the window, or null for none.</summary>
    public DropShadow? DrawnShadow { get; init; }

    /// <summary>The glow Winland draws inside the window's edge, or null for none.</summary>
    public InnerGlow? Glow { get; init; }

    /// <summary>The wobble and motion blur the window's motions are drawn with, or null for none.</summary>
    public MotionEffects? Motion { get; init; }
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

    /// <summary><c>decoration:blur:xray</c>: a translucent Client shows the blurred wallpaper alone, drawn by Winland, not DWM's backdrop.</summary>
    public bool Xray { get; init; }

    /// <summary><c>decoration:blur:ignore_opacity</c>: the blurred wallpaper does not take the window's opacity.</summary>
    public bool IgnoreOpacity { get; init; } = true;

    /// <summary>The blur options <c>decoration:blur:size</c> through <c>vibrancy_darkness</c> that the blurred wallpaper reads.</summary>
    public BlurTuning BlurTuning { get; init; } = BlurTuning.Documented;

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

    /// <summary>
    /// <c>decoration:border_part_of_window</c>: whether the shadow's box includes the border. False grows the shadow from the
    /// window alone, so the border is drawn over it.
    /// </summary>
    public bool BorderPartOfWindow { get; init; } = true;

    /// <summary><c>decoration:glow:enabled</c>: draw a glow inside each Client's edge.</summary>
    public bool Glow { get; init; }

    /// <summary><c>decoration:glow:range</c>: how far the glow reaches into the window, in logical pixels.</summary>
    public int GlowRange { get; init; } = 10;

    /// <summary><c>decoration:glow:render_power</c>: how fast the glow falls off, from 1 to 4.</summary>
    public int GlowPower { get; init; } = 3;

    /// <summary><c>decoration:glow:color</c>.</summary>
    public BorderGradient GlowColor { get; init; } = BorderGradient.Of(new BorderColor(0xEE, 0x33, 0xCC, 0xFF));

    /// <summary><c>decoration:glow:color_inactive</c>, or null to use <see cref="GlowColor"/> on every window.</summary>
    public BorderGradient? GlowColorInactive { get; init; }

    public float ActiveOpacity { get; init; } = 1;

    public float InactiveOpacity { get; init; } = 1;

    public float FullscreenOpacity { get; init; } = 1;

    public bool DimInactive { get; init; }

    public float DimStrength { get; init; } = 0.5f;

    public bool DimModal { get; init; } = true;

    public float DimSpecial { get; init; } = 0.2f;

    /// <summary><c>decoration:dim_around</c>: how much a window with the dim_around property dims what lies under it.</summary>
    public float DimAround { get; init; } = 0.4f;

    /// <summary><c>decoration:motion_blur:enabled</c>: smear a window that moves or resizes.</summary>
    public bool MotionBlur { get; init; }

    /// <summary><c>decoration:motion_blur:samples</c>, from 1 to 64: how many pictures a blur averages. 1 draws no blur.</summary>
    public int MotionBlurSamples { get; init; } = 7;

    /// <summary><c>decoration:wobble:enabled</c>: let a window that moves or resizes wobble.</summary>
    public bool Wobble { get; init; }

    /// <summary>The spring and mesh of the wobble, from the <c>decoration:wobble</c> keywords.</summary>
    public WobbleSettings WobbleSpring { get; init; } = WobbleSettings.Documented;

    /// <summary>
    /// The effects a window's motions are drawn with, or null for none. A fullscreen window has none, and a window
    /// with <c>no_wobble</c> does not wobble.
    /// </summary>
    internal MotionEffects? EffectsFor(bool noWobble, bool fullscreen)
    {
        if (fullscreen)
            return null;

        var effects = new MotionEffects(Wobble && !noWobble ? WobbleSpring : null, MotionBlur && MotionBlurSamples > 1 ? MotionBlurSamples : 0);
        return effects.Any ? effects : null;
    }

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
        ["decoration:blur:xray"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { Xray = enabled } : options,
        ["decoration:blur:ignore_opacity"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { IgnoreOpacity = enabled } : options,
        ["decoration:blur:size"] = (options, value) => TryParseWhole(value, 0, 100, out var size) ? options with { BlurTuning = options.BlurTuning with { Size = size } } : options,
        ["decoration:blur:passes"] = (options, value) => TryParseWhole(value, 0, 10, out var passes) ? options with { BlurTuning = options.BlurTuning with { Passes = passes } } : options,
        ["decoration:blur:noise"] = (options, value) => TryParseRange(value, 0f, 1f, out var noise) ? options with { BlurTuning = options.BlurTuning with { Noise = noise } } : options,
        ["decoration:blur:contrast"] = (options, value) => TryParseRange(value, 0f, 2f, out var contrast) ? options with { BlurTuning = options.BlurTuning with { Contrast = contrast } } : options,
        ["decoration:blur:brightness"] = (options, value) => TryParseRange(value, 0f, 2f, out var brightness) ? options with { BlurTuning = options.BlurTuning with { Brightness = brightness } } : options,
        ["decoration:blur:vibrancy"] = (options, value) => TryParseRange(value, 0f, 1f, out var vibrancy) ? options with { BlurTuning = options.BlurTuning with { Vibrancy = vibrancy } } : options,
        ["decoration:blur:vibrancy_darkness"] = (options, value) => TryParseRange(value, 0f, 1f, out var darkness) ? options with { BlurTuning = options.BlurTuning with { VibrancyDarkness = darkness } } : options,
        ["decoration:shadow:enabled"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { Shadow = enabled } : options,
        ["decoration:shadow:range"] = (options, value) => TryParseWhole(value, 0, 100, out var range) ? options with { ShadowRange = range, ShadowDrawn = true } : options,
        ["decoration:shadow:render_power"] = (options, value) => TryParseWhole(value, 1, 4, out var power) ? options with { ShadowPower = power, ShadowDrawn = true } : options,
        ["decoration:shadow:sharp"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var sharp) ? options with { ShadowSharp = sharp, ShadowDrawn = true } : options,
        ["decoration:shadow:color"] = (options, value) => BorderGradient.TryParse(value, out var color) ? options with { ShadowColor = color, ShadowDrawn = true } : options,
        ["decoration:shadow:color_inactive"] = (options, value) => BorderGradient.TryParse(value, out var color) ? options with { ShadowColorInactive = color, ShadowDrawn = true } : options,
        ["decoration:shadow:offset"] = (options, value) => TryParseOffset(value, out var offset) ? options with { ShadowOffset = offset, ShadowDrawn = true } : options,
        ["decoration:shadow:scale"] = (options, value) => TryParseScale(value, out var scale) ? options with { ShadowScale = scale, ShadowDrawn = true } : options,
        ["decoration:border_part_of_window"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var part) ? options with { BorderPartOfWindow = part } : options,
        ["decoration:glow:enabled"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { Glow = enabled } : options,
        ["decoration:glow:range"] = (options, value) => TryParseWhole(value, 0, 100, out var range) ? options with { GlowRange = range } : options,
        ["decoration:glow:render_power"] = (options, value) => TryParseWhole(value, 1, 4, out var power) ? options with { GlowPower = power } : options,
        ["decoration:glow:color"] = (options, value) => BorderGradient.TryParse(value, out var color) ? options with { GlowColor = color } : options,
        ["decoration:glow:color_inactive"] = (options, value) => BorderGradient.TryParse(value, out var color) ? options with { GlowColorInactive = color } : options,
        ["decoration:active_opacity"] = (options, value) => TryParseUnit(value, out var opacity) ? options with { ActiveOpacity = opacity } : options,
        ["decoration:inactive_opacity"] = (options, value) => TryParseUnit(value, out var opacity) ? options with { InactiveOpacity = opacity } : options,
        ["decoration:fullscreen_opacity"] = (options, value) => TryParseUnit(value, out var opacity) ? options with { FullscreenOpacity = opacity } : options,
        ["decoration:dim_inactive"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { DimInactive = enabled } : options,
        ["decoration:dim_strength"] = (options, value) => TryParseUnit(value, out var strength) ? options with { DimStrength = strength } : options,
        ["decoration:dim_modal"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { DimModal = enabled } : options,
        ["decoration:dim_special"] = (options, value) => TryParseUnit(value, out var strength) ? options with { DimSpecial = strength } : options,
        ["decoration:dim_around"] = (options, value) => TryParseUnit(value, out var strength) ? options with { DimAround = strength } : options,
        ["decoration:motion_blur:enabled"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { MotionBlur = enabled } : options,
        ["decoration:motion_blur:samples"] = (options, value) => TryParseWhole(value, 1, 64, out var samples) ? options with { MotionBlurSamples = samples } : options,
        ["decoration:wobble:enabled"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { Wobble = enabled } : options,
        ["decoration:wobble:mesh"] = (options, value) => TryParseWhole(value, 2, 32, out var mesh) ? options with { WobbleSpring = options.WobbleSpring with { Mesh = mesh } } : options,
        ["decoration:wobble:stiffness"] = (options, value) => TryParseDoubleRange(value, 0.0001, 1000, out var stiffness) ? options with { WobbleSpring = options.WobbleSpring with { Stiffness = stiffness } } : options,
        ["decoration:wobble:damping"] = (options, value) => TryParseDoubleRange(value, 0, 1000, out var damping) ? options with { WobbleSpring = options.WobbleSpring with { Damping = damping } } : options,
        ["decoration:wobble:mass"] = (options, value) => TryParseDoubleRange(value, 0.0001, 1000, out var mass) ? options with { WobbleSpring = options.WobbleSpring with { Mass = mass } } : options,
        ["decoration:wobble:intensity"] = (options, value) => TryParseDoubleRange(value, 0, 2, out var intensity) ? options with { WobbleSpring = options.WobbleSpring with { Intensity = intensity } } : options,
        ["decoration:wobble:value_epsilon"] = (options, value) => TryParseDoubleRange(value, 0, 100, out var epsilon) ? options with { WobbleSpring = options.WobbleSpring with { ValueEpsilon = epsilon } } : options,
        ["decoration:wobble:velocity_epsilon"] = (options, value) => TryParseDoubleRange(value, 0, 1000, out var epsilon) ? options with { WobbleSpring = options.WobbleSpring with { VelocityEpsilon = epsilon } } : options,
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

    private static bool TryParseRange(string text, float min, float max, out float value)
    {
        value = 0;
        if (!float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !float.IsFinite(parsed))
            return false;

        value = Math.Clamp(parsed, min, max);
        return true;
    }

    private static bool TryParseDoubleRange(string text, double min, double max, out double value)
    {
        value = 0;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))
            return false;

        value = Math.Clamp(parsed, min, max);
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
