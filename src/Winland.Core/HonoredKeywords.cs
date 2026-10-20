namespace Winland;

// One set of honored keywords per category, taken from the type that reads them.
// Add a keyword to that type's set to stop the warning.
internal static class HonoredKeywords
{
    /// <summary>
    /// Options the pinned release defines and never reads, so they change nothing there either. They are read and
    /// change nothing here, without a warning.
    /// </summary>
    internal static IReadOnlyList<string> Inert { get; } =
    [
        "dwindle:special_scale_factor",
        "master:special_scale_factor",
        "misc:animate_mouse_windowdragging",
    ];

    private static readonly Dictionary<string, HashSet<string>> ByCategory = Build();

    internal static bool Contains(string keyword)
    {
        var colon = keyword.IndexOf(':');
        if (colon <= 0 || colon == keyword.Length - 1)
            return false;

        return ByCategory.TryGetValue(keyword[..colon], out var names)
            && names.Contains(keyword[(colon + 1)..]);
    }

    private static Dictionary<string, HashSet<string>> Build()
    {
        var table = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var category in new[] { "general", "dwindle", "decoration", "binds" })
            table.Add(category, new HashSet<string>(StringComparer.Ordinal));

        Add(table, LayoutOptions.Keywords);
        Add(table, ScrollingOptions.Keywords);
        Add(table, MasterOptions.Keywords);
        Add(table, DecorationOptions.Keywords);
        Add(table, BindMatcher.Keywords);
        Add(table, SessionOptions.Keywords);
        Add(table, CursorOptions.Keywords);
        Add(table, InputOptions.Keywords);
        Add(table, DigitizerOptions.Keywords);
        Add(table, GestureOptions.Keywords);
        Add(table, AnimationOptions.Keywords);
        Add(table, GroupOptions.Keywords);
        Add(table, DebugOptions.Keywords);
        Add(table, Inert);
        return table;
    }

    private static void Add(Dictionary<string, HashSet<string>> table, IEnumerable<string> keywords)
    {
        foreach (var keyword in keywords)
        {
            var colon = keyword.IndexOf(':');
            if (colon <= 0 || colon == keyword.Length - 1)
                continue;

            var category = keyword[..colon];
            if (!table.TryGetValue(category, out var names))
            {
                names = new HashSet<string>(StringComparer.Ordinal);
                table.Add(category, names);
            }

            names.Add(keyword[(colon + 1)..]);
        }
    }
}
