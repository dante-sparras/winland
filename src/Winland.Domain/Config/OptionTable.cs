namespace Winland.Domain;

/// <summary>Every Option Winland reads. A line that names anything else warns.</summary>
internal static class OptionTable
{
    private static readonly Dictionary<string, IOption> ByName = Build();

    internal static IEnumerable<string> Names => ByName.Keys;

    internal static IOption? Find(string name) => ByName.GetValueOrDefault(name);

    /// <summary>The <c>getoption</c> reply for an Option whose value <paramref name="value"/> wrote.</summary>
    internal static string Reply(string name, string value, bool set) =>
        "{\"option\":" + OptionValue.Quote(name) + "," + value + ",\"set\":" + (set ? "true" : "false") + "}";

    private static Dictionary<string, IOption> Build()
    {
        var table = new Dictionary<string, IOption>(StringComparer.Ordinal);
        IEnumerable<IOption>[] sets =
        [
            LayoutOptions.Options.All,
            ScrollingOptions.Options.All,
            MasterOptions.Options.All,
            DecorationOptions.Options.All,
            GroupOptions.Options.All,
            SessionOptions.Options.All,
            BindOptions.Options.All,
            CursorOptions.Options.All,
            InputOptions.Options.All,
            GestureOptions.Options.All,
            AnimationOptions.Options.All,
            DebugOptions.Options.All,
        ];
        foreach (var set in sets)
        {
            foreach (var option in set)
                table.Add(option.Name, option);
        }

        return table;
    }
}
