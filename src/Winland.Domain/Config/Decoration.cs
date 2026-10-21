namespace Winland.Domain;

/// <summary>The corner preference DWM draws. <see cref="Round"/> and <see cref="RoundSmall"/> are circular corners of 8 and 4 pixels at 96 DPI.</summary>
public enum CornerPreference
{
    DoNotRound,
    Round,
    RoundSmall,
}

/// <summary>
/// How one Client is decorated. <see cref="Region"/> is set when DWM has no corner of that radius,
/// and the corners are then cut from the window instead. <see cref="Blur"/> asks for DWM's acrylic backdrop.
/// DWM's own shadow is left as Windows draws it.
/// </summary>
public readonly record struct Decoration(
    ClientId Client,
    BorderColor Border,
    CornerPreference Corners,
    byte Alpha = 255,
    bool Borderless = false)
{
    public CornerRegion? Region { get; init; }

    /// <summary>The window's opacity, from 0 to 255. <c>fadeSwitch</c> animates it. It is <see cref="Alpha"/>.</summary>
    public byte Opacity { get; init; } = Alpha;

    public bool Blur { get; init; }

    /// <summary>The border Winland draws outside the window, or null for none.</summary>
    public BorderRing? Ring { get; init; }
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
        // White stands in until the Windows accent is known.
        ActiveBorder = new BorderColor(0xFF, 0xFF, 0xFF, 0xFF),
        InactiveBorder = new BorderColor(0xFF, 0x40, 0x40, 0x40),
        Rounding = 8,
    };

    public BorderColor ActiveBorder { get; init; }

    public BorderColor InactiveBorder { get; init; }

    /// <summary>True once <c>general:col.active_border</c> parsed as a color. Otherwise the active border follows the Windows accent.</summary>
    public bool ActiveBorderSet { get; init; }

    /// <summary>Uses <paramref name="accent"/> for the active border when the config did not set one.</summary>
    public DecorationOptions WithAccent(BorderColor? accent) =>
        ActiveBorderSet || accent is not BorderColor color ? this : this with { ActiveBorder = color };

    /// <summary>The corner radius in logical pixels. 0 draws square corners.</summary>
    public int Rounding { get; init; }

    /// <summary>The corner curve: 1 is a straight cut, 2 is a circle, larger values are squarer. Clamped to 1 through 10.</summary>
    public float RoundingPower { get; init; } = 2;

    /// <summary><c>decoration:blur:enabled</c>: DWM's acrylic backdrop. False asks for none.</summary>
    public bool Blur { get; init; } = true;

    public float ActiveOpacity { get; init; } = 1;

    public float InactiveOpacity { get; init; } = 1;

    public float FullscreenOpacity { get; init; } = 1;

    // A negative radius does not read.
    private static readonly OptionValue<int> Radius = OptionValue.Of(
        "int",
        (string text, out int radius) => OptionValue.Int().Read(text, out radius) && radius >= 0,
        OptionValue.Int().Json);

    internal static OptionSet<DecorationOptions> Options { get; } = new(
    [
        Of("general:col.active_border", OptionValue.Color, o => o.ActiveBorder, (o, v) => o with { ActiveBorder = v, ActiveBorderSet = true }),
        Of("general:col.inactive_border", OptionValue.Color, o => o.InactiveBorder, (o, v) => o with { InactiveBorder = v }),
        Of("decoration:rounding", Radius, o => o.Rounding, (o, v) => o with { Rounding = v }),
        Of("decoration:rounding_power", OptionValue.Single(1f, 10f), o => o.RoundingPower, (o, v) => o with { RoundingPower = v }),
        Of("decoration:blur:enabled", OptionValue.Bool, o => o.Blur, (o, v) => o with { Blur = v }),
        Of("decoration:active_opacity", OptionValue.Single(0f, 1f), o => o.ActiveOpacity, (o, v) => o with { ActiveOpacity = v }),
        Of("decoration:inactive_opacity", OptionValue.Single(0f, 1f), o => o.InactiveOpacity, (o, v) => o with { InactiveOpacity = v }),
        Of("decoration:fullscreen_opacity", OptionValue.Single(0f, 1f), o => o.FullscreenOpacity, (o, v) => o with { FullscreenOpacity = v }),
    ]);

    public static DecorationOptions From(ConfigDocument document) => Options.From(Documented, document);

    private static Option<DecorationOptions> Of<T>(string name, OptionValue<T> value, Func<DecorationOptions, T> get, Func<DecorationOptions, T, DecorationOptions> set) =>
        Option<DecorationOptions>.Of(name, value, get, set);
}
