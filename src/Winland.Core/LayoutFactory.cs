namespace Winland;

/// <summary>
/// The Layout a Workspace gets for a <c>general:layout</c> value, and the warning when that
/// value fell back to dwindle.
/// </summary>
internal readonly record struct LayoutFallback(Layout Layout, string? Warning);

/// <summary>Picks the Layout a Workspace uses from <c>general:layout</c>.</summary>
internal static class LayoutFactory
{
    internal static IReadOnlyList<string> Names { get; } = ["dwindle", "master", "scrolling", "monocle"];

    /// <summary>The keyword <c>hl.layout.register</c> becomes, which names a Layout a script arranges: <c>lua:NAME</c>.</summary>
    internal const string ScriptKeyword = LayoutRequest.Keyword;

    /// <summary>The prefix of a Layout a script arranges.</summary>
    internal const string ScriptPrefix = LayoutRequest.Prefix;

    /// <summary>
    /// Creates the Layout for a Workspace. A missing config uses dwindle.
    /// </summary>
    public static Layout Create(ConfigDocument? config) => Create(config, null, null);

    /// <summary>
    /// Creates the Layout for a Workspace whose rule may name one, as the pinned release picks it: the rule's
    /// <c>layout</c>, or else <c>general:layout</c>. A name that is no Layout uses dwindle. A name a plugin registered
    /// is a <see cref="ScriptedLayout"/> that plugin arranges. A <c>lua:NAME</c> the config registered is one that
    /// <paramref name="scripts"/> arranges.
    /// </summary>
    public static Layout Create(ConfigDocument? config, string? ruleLayout, ILayoutScripts? scripts = null)
    {
        var name = (ruleLayout ?? Name(config))?.Trim();
        if (name is not null && PluginLayouts.TryScripts(name, out var pluginScripts))
            return new ScriptedLayout(name, pluginScripts);

        if (name is not null && IsScript(name) && Registered(config, name))
            return new ScriptedLayout(name, scripts);

        return Fallback(name).Layout;
    }

    /// <summary>Whether a value names a Layout a script arranges: <c>lua:</c> and a name.</summary>
    internal static bool IsScript(string name) =>
        name.StartsWith(ScriptPrefix, StringComparison.Ordinal) && name.Length > ScriptPrefix.Length;

    // Whether the config registered a script's Layout by this name.
    private static bool Registered(ConfigDocument? config, string name)
    {
        if (config is null)
            return false;

        foreach (var setting in config.Settings)
        {
            if (setting.Keyword == ScriptKeyword && setting.Value.Trim() == name)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves a <c>general:layout</c> value. <c>dwindle</c>, <c>master</c>, <c>monocle</c>,
    /// <c>scrolling</c>, and a missing name select a Layout and return no warning. A missing name
    /// selects dwindle. A <c>lua:NAME</c> also selects dwindle without a warning here, as a name no script registered does
    /// in the pinned release, and so does a layout a plugin registered. Any other value keeps the dwindle Layout and
    /// returns a warning.
    /// </summary>
    public static LayoutFallback Fallback(string? name)
    {
        var trimmed = name?.Trim();
        if (trimmed is null || trimmed.Equals("dwindle", StringComparison.OrdinalIgnoreCase))
            return new LayoutFallback(new DwindleLayout(), null);
        if (trimmed.Equals("master", StringComparison.OrdinalIgnoreCase))
            return new LayoutFallback(new MasterLayout(), null);
        if (trimmed.Equals("scrolling", StringComparison.OrdinalIgnoreCase))
            return new LayoutFallback(new ScrollingLayout(), null);
        if (trimmed.Equals("monocle", StringComparison.OrdinalIgnoreCase))
            return new LayoutFallback(new MonocleLayout(), null);
        if (IsScript(trimmed) || PluginLayouts.Contains(trimmed))
            return new LayoutFallback(new DwindleLayout(), null);

        return new LayoutFallback(new DwindleLayout(), $"Layout '{trimmed}' is not available; using dwindle.");
    }

    /// <summary>The <c>general:layout</c> value that selects <paramref name="layout"/>.</summary>
    public static string NameOf(Layout layout) => layout switch
    {
        MasterLayout => "master",
        ScrollingLayout => "scrolling",
        MonocleLayout => "monocle",
        ScriptedLayout scripted => scripted.Name,
        _ => "dwindle",
    };

    private static string? Name(ConfigDocument? config)
    {
        if (config is null)
            return null;

        string? name = null;
        foreach (var setting in config.Settings)
        {
            if (setting.Keyword.Equals("general:layout", StringComparison.OrdinalIgnoreCase))
                name = setting.Value;
        }

        return name;
    }
}
