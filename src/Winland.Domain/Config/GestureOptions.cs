namespace Winland.Domain;

/// <summary>The <c>gestures</c> options that steer a workspace swipe. The distance is pixels of touchpad travel.</summary>
public sealed record GestureOptions
{
    public int Distance { get; init; } = 300;

    public bool Invert { get; init; } = true;

    public static GestureOptions Documented { get; } = new();

    internal static OptionSet<GestureOptions> Options { get; } = new(
    [
        Of("gestures:workspace_swipe_distance", OptionValue.Int(0, 2000), o => o.Distance, (o, v) => o with { Distance = v }),
        Of("gestures:workspace_swipe_invert", OptionValue.Bool, o => o.Invert, (o, v) => o with { Invert = v }),
    ]);

    public static GestureOptions From(ConfigDocument document) => Options.From(Documented, document);

    private static Option<GestureOptions> Of<T>(string name, OptionValue<T> value, Func<GestureOptions, T> get, Func<GestureOptions, T, GestureOptions> set) =>
        Option<GestureOptions>.Of(name, value, get, set);
}
