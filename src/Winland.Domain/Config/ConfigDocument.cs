namespace Winland.Domain;

public sealed record ConfigDocument(
    IReadOnlyList<Setting> Settings,
    IReadOnlyList<BindDirective> Binds,
    IReadOnlyList<ConfigWarning> Warnings)
{
    public IReadOnlyList<WindowRule> WindowRules { get; init; } = [];

    public IReadOnlyList<WorkspaceRule> WorkspaceRules { get; init; } = WorkspaceRule.None;

    public IReadOnlyList<string> Sources { get; init; } = [];

    public IReadOnlyList<TrackpadGesture> Gestures { get; init; } = [];

    /// <summary>The path the config was read from, when it was read from a file. A sourced warning names its own file.</summary>
    public string? Path { get; init; }
}
