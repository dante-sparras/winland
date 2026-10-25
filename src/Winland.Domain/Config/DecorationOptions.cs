namespace Winland.Domain;

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
        Of("decoration:blur:enabled", OptionValue.Bool, o => o.Blur, (o, v) => o with { Blur = v }),
        Of("decoration:active_opacity", OptionValue.Single(0f, 1f), o => o.ActiveOpacity, (o, v) => o with { ActiveOpacity = v }),
        Of("decoration:inactive_opacity", OptionValue.Single(0f, 1f), o => o.InactiveOpacity, (o, v) => o with { InactiveOpacity = v }),
        Of("decoration:fullscreen_opacity", OptionValue.Single(0f, 1f), o => o.FullscreenOpacity, (o, v) => o with { FullscreenOpacity = v }),
    ]);

    public static DecorationOptions From(ConfigDocument document) => Options.From(Documented, document);

    private static Option<DecorationOptions> Of<T>(string name, OptionValue<T> value, Func<DecorationOptions, T> get, Func<DecorationOptions, T, DecorationOptions> set) =>
        Option<DecorationOptions>.Of(name, value, get, set);
}
