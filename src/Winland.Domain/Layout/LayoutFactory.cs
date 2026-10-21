namespace Winland.Domain;

/// <summary>
/// The Layout a Workspace gets for a <c>general:layout</c> value, and the warning when that
/// value fell back to dwindle.
/// </summary>
internal readonly record struct LayoutFallback(Layout Layout, string? Warning);

/// <summary>Picks the Layout a Workspace uses from <c>general:layout</c>.</summary>
internal static class LayoutFactory
{
    internal static IReadOnlyList<string> Names { get; } = ["dwindle", "master", "scrolling", "monocle"];

    /// <summary>
    /// Creates the Layout for a Workspace. A missing config uses dwindle.
    /// </summary>
    public static Layout Create(ConfigDocument? config) => Create(config, null);

    /// <summary>
    /// Creates the Layout for a Workspace whose rule may name one, as Winland picks it: the rule's
    /// <c>layout</c>, or else <c>general:layout</c>. A name that is no Layout uses dwindle.
    /// </summary>
    public static Layout Create(ConfigDocument? config, string? ruleLayout) =>
        Fallback(ruleLayout ?? (config is null ? null : LayoutOptions.From(config).Layout)).Layout;

    /// <summary>
    /// Resolves a <c>general:layout</c> value. <c>dwindle</c>, <c>master</c>, <c>monocle</c>,
    /// <c>scrolling</c>, and a missing name select a Layout and return no warning. A missing name
    /// selects dwindle. Any other value keeps the dwindle Layout and returns a warning.
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

        return new LayoutFallback(new DwindleLayout(), $"Layout '{trimmed}' is not available; using dwindle.");
    }

    /// <summary>
    /// The name reported for <paramref name="layout"/>. Dwindle, master, scrolling and monocle are also
    /// <c>general:layout</c> values. Floating is reached by <c>cyclelayout</c>.
    /// </summary>
    public static string NameOf(Layout layout) => layout switch
    {
        MasterLayout => "master",
        ScrollingLayout => "scrolling",
        MonocleLayout => "monocle",
        FloatingLayout => "floating",
        _ => "dwindle",
    };
}
