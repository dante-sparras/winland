namespace Winland.Domain;

public sealed record CursorOptions
{
    public bool NoWarps { get; init; }

    public double InactiveTimeoutSeconds { get; init; }

    public bool HideOnKeyPress { get; init; }

    public static CursorOptions Documented { get; } = new();

    public CursorBehavior Behavior() => new(HideOnKeyPress, InactiveTimeoutSeconds);

    internal static OptionSet<CursorOptions> Options { get; } = new(
    [
        Of("cursor:no_warps", OptionValue.Bool, o => o.NoWarps, (o, v) => o with { NoWarps = v }),
        Of("cursor:inactive_timeout", OptionValue.Number(0, 20), o => o.InactiveTimeoutSeconds, (o, v) => o with { InactiveTimeoutSeconds = v }),
        Of("cursor:hide_on_key_press", OptionValue.Bool, o => o.HideOnKeyPress, (o, v) => o with { HideOnKeyPress = v }),
    ]);

    public static CursorOptions From(ConfigDocument document) => Options.From(Documented, document);

    private static Option<CursorOptions> Of<T>(string name, OptionValue<T> value, Func<CursorOptions, T> get, Func<CursorOptions, T, CursorOptions> set) =>
        Option<CursorOptions>.Of(name, value, get, set);
}
