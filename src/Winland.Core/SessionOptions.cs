namespace Winland;

public sealed record SessionOptions
{
    public bool FocusOnActivate { get; init; }

    public bool CloseSpecialOnEmpty { get; init; } = true;

    public bool HideSpecialOnWorkspaceChange { get; init; }

    public bool MouseMoveFocusesMonitor { get; init; } = true;

    public static SessionOptions Documented { get; } = new();

    // Adding an entry here applies that keyword and stops the config warning.
    private static readonly Dictionary<string, Func<SessionOptions, string, SessionOptions>> Appliers = new(StringComparer.Ordinal)
    {
        ["misc:focus_on_activate"] = (options, value) => TryParseBool(value, out var focus) ? options with { FocusOnActivate = focus } : options,
        ["misc:close_special_on_empty"] = (options, value) => TryParseBool(value, out var close) ? options with { CloseSpecialOnEmpty = close } : options,
        ["binds:hide_special_on_workspace_change"] = (options, value) => TryParseBool(value, out var hide) ? options with { HideSpecialOnWorkspaceChange = hide } : options,
        ["misc:mouse_move_focuses_monitor"] = (options, value) => TryParseBool(value, out var mouse) ? options with { MouseMoveFocusesMonitor = mouse } : options,
    };

    internal static IReadOnlySet<string> Keywords { get; } = Appliers.Keys.ToHashSet(StringComparer.Ordinal);

    public static SessionOptions From(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = Documented;
        foreach (var setting in document.Settings)
            options = Apply(options, setting);
        return options;
    }

    private static SessionOptions Apply(SessionOptions options, Setting setting) =>
        Appliers.TryGetValue(setting.Keyword, out var apply) ? apply(options, setting.Value) : options;

    private static bool TryParseBool(string text, out bool value)
    {
        if (Is(text, "true") || Is(text, "yes") || Is(text, "on") || text == "1")
        {
            value = true;
            return true;
        }

        if (Is(text, "false") || Is(text, "no") || Is(text, "off") || text == "0")
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static bool Is(string text, string expected) =>
        text.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
