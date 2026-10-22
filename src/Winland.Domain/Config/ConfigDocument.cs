namespace Winland.Domain;

public sealed record ConfigDocument(
    IReadOnlyList<Setting> Settings,
    IReadOnlyList<BindDirective> Binds,
    IReadOnlyList<ConfigWarning> Warnings)
{
    /// <summary>A document with no lines, as when no config was read.</summary>
    public static ConfigDocument Empty { get; } = new([], [], []);

    public IReadOnlyList<WindowRule> WindowRules { get; init; } = [];

    public IReadOnlyList<WorkspaceRule> WorkspaceRules { get; init; } = WorkspaceRule.None;

    public IReadOnlyList<string> Sources { get; init; } = [];

    public IReadOnlyList<TrackpadGesture> Gestures { get; init; } = [];

    /// <summary>The path the config was read from, when it was read from a file. A sourced warning names its own file.</summary>
    public string? Path { get; init; }

    /// <summary>
    /// This document with <paramref name="live"/>'s lines applied, as <c>keyword</c> applies them. An option, a keyword
    /// with a category such as general:gaps_in, replaces its old value. A keyword that may repeat, such as monitor or
    /// windowrulev2, adds its line after the ones already read, and a rule for a Workspace that already has one merges
    /// into it. Everything else is kept, and the warnings are left behind.
    /// </summary>
    public ConfigDocument Merge(ConfigDocument live)
    {
        ArgumentNullException.ThrowIfNull(live);
        var workspaceRules = new List<WorkspaceRule>(WorkspaceRules);
        foreach (var rule in live.WorkspaceRules)
            Domain.WorkspaceRules.ReplaceOrAdd(workspaceRules, rule);

        return this with
        {
            Settings = [.. Settings.Where(setting => !Replaced(setting, live.Settings)), .. live.Settings],
            Binds = [.. Binds, .. live.Binds],
            Warnings = [],
            WindowRules = [.. WindowRules, .. live.WindowRules],
            WorkspaceRules = workspaceRules,
            Gestures = [.. Gestures, .. live.Gestures],
        };
    }

    private static bool Replaced(Setting setting, IReadOnlyList<Setting> updates) =>
        setting.Keyword.Contains(':', StringComparison.Ordinal)
        && updates.Any(update => update.Keyword == setting.Keyword);
}

public sealed record Setting(string Keyword, string Value);

public sealed record ConfigWarning(int Line, string Message, string? Source = null);

public sealed record ConfigRead(ConfigDocument? Document, ConfigWarning? Failure = null);
