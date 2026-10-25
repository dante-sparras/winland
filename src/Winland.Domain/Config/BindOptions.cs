namespace Winland.Domain;

/// <summary>The <c>binds:</c> options that steer Workspace switches and pointer binds.</summary>
public sealed record BindOptions
{
    public static BindOptions Documented { get; } = new();

    /// <summary><c>binds:workspace_back_and_forth</c>: 0 off, 1 the previous Workspace on any Monitor, 2 the previous one on this Monitor.</summary>
    public int BackAndForth { get; init; }

    /// <summary><c>binds:drag_threshold</c>: how far the pointer moves, in pixels, before a press becomes a drag. 0 drags at once.</summary>
    public int DragThreshold { get; init; }

    private static readonly Dictionary<string, int> BackAndForthNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disabled"] = 0,
        ["all_monitors"] = 1,
        ["per_monitor"] = 2,
    };

    // 0, 1 or 2, a name for one, or a boolean as older configs write it.
    private static readonly OptionValue<int> BackAndForthMode = OptionValue.Of(
        "int",
        (string text, out int mode) =>
        {
            if (OptionValue.Int(0, 2, BackAndForthNames).Read(text, out mode))
                return true;

            if (!ConfigValueSyntax.TryParseBool(text.Trim(), out var on))
                return false;

            mode = on ? 1 : 0;
            return true;
        },
        OptionValue.Int().Json);

    internal static OptionSet<BindOptions> Options { get; } = new(
    [
        Of("binds:workspace_back_and_forth", BackAndForthMode, o => o.BackAndForth, (o, v) => o with { BackAndForth = v }),
        Of("binds:drag_threshold", OptionValue.Int(0), o => o.DragThreshold, (o, v) => o with { DragThreshold = v }),
    ]);

    public static BindOptions From(ConfigDocument document) => Options.From(Documented, document);

    private static Option<BindOptions> Of<T>(string name, OptionValue<T> value, Func<BindOptions, T> get, Func<BindOptions, T, BindOptions> set) =>
        Option<BindOptions>.Of(name, value, get, set);
}
