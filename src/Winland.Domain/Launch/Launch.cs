namespace Winland.Domain;

public sealed record Launch(string CommandLine, string? WorkingDirectory)
{
    public static IReadOnlyList<WindowRule> NoRules { get; } = [];

    public bool Raw { get; init; }

    public IReadOnlyList<WindowRule> Rules { get; init; } = NoRules;

    /// <summary>
    /// The Workspace this program was started on, which its window opens on.
    /// Null for <c>exec-once</c> and <c>exec-shutdown</c>, which Winland starts without a token.
    /// </summary>
    public WorkspaceId? InitialWorkspace { get; init; }
}
