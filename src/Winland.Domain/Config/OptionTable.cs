namespace Winland.Domain;

/// <summary>Every Option Winland reads, and where the config in force keeps its value. A line that names anything else warns.</summary>
internal static class OptionTable
{
    private static readonly Dictionary<string, Entry> ByName = Build(
        Section(LayoutOptions.Options, config => config.Layout),
        Section(ScrollingOptions.Options, config => config.Scrolling),
        Section(MasterOptions.Options, config => config.Master),
        Section(DecorationOptions.Options, config => config.Decoration),
        Section(GroupOptions.Options, config => config.Groups),
        Section(SessionOptions.Options, config => config.Session),
        Section(BindOptions.Options, config => config.BindOptions),
        Section(CursorOptions.Options, config => config.Cursor),
        Section(InputOptions.Options, config => config.Input),
        Section(GestureOptions.Options, config => config.Swipe),
        Section(AnimationOptions.Options, config => config.Animations),
        Section(DebugOptions.Options, config => config.Debug));

    internal static IEnumerable<string> Names => ByName.Keys;

    internal static IOption? Find(string name) => ByName.GetValueOrDefault(name)?.Option;

    /// <summary>
    /// The <c>getoption</c> reply: the value <paramref name="config"/> applied, and whether a line it read sets the
    /// Option. Null when no Option has that name.
    /// </summary>
    internal static string? Describe(string name, AppliedConfig config)
    {
        if (!ByName.TryGetValue(name, out var entry))
            return null;

        var set = config.Document?.Settings.Any(setting => setting.Keyword == name) == true;
        return "{\"option\":" + OptionValue.Quote(name) + "," + entry.Reply(config) + ",\"set\":" + (set ? "true" : "false") + "}";
    }

    private static IEnumerable<Entry> Section<TOptions>(OptionSet<TOptions> set, Func<AppliedConfig, TOptions> applied)
        where TOptions : class =>
        set.All.Select(option => new Entry(option, config => option.Reply(applied(config))));

    private static Dictionary<string, Entry> Build(params IEnumerable<Entry>[] sections)
    {
        var table = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var entry in sections.SelectMany(section => section))
            table.Add(entry.Option.Name, entry);

        return table;
    }

    private sealed record Entry(IOption Option, Func<AppliedConfig, string> Reply);
}
