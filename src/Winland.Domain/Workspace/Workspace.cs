namespace Winland.Domain;

public sealed record Workspace(WorkspaceId Id, MonitorId Monitor)
{
    public WindowId? LastWindow { get; init; }

    public bool Persistent { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>The name of the Layout that tiles this Workspace, as <c>general:layout</c> spells it.</summary>
    public string TiledLayout { get; init; } = "dwindle";
}
