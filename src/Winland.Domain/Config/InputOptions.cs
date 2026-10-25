namespace Winland.Domain;

/// <summary>Input options Winland reads for focus.</summary>
public sealed record InputOptions
{
    public int FollowMouse { get; init; } = 1;

    public static InputOptions Documented { get; } = new();

    private static readonly Dictionary<string, int> FollowMouseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disabled"] = 0,
        ["follow"] = 1,
        ["detached"] = 2,
        ["separate"] = 3,
    };

    internal static OptionSet<InputOptions> Options { get; } = new(
    [
        Of("input:follow_mouse", OptionValue.Int(0, 3, FollowMouseNames), o => o.FollowMouse, (o, v) => o with { FollowMouse = v }),
    ]);

    public static InputOptions From(ConfigDocument document) => Options.From(Documented, document);

    private static Option<InputOptions> Of<T>(string name, OptionValue<T> value, Func<InputOptions, T> get, Func<InputOptions, T, InputOptions> set) =>
        Option<InputOptions>.Of(name, value, get, set);
}
