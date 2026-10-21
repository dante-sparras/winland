namespace Winland.Domain;

/// <summary>The Client record a Layout keeps for a Window it adopts.</summary>
internal static class ClientAdoption
{
    /// <summary>A Window adopted without an initial class or title starts with its current ones.</summary>
    public static Client Adopted(
        ClientId id,
        WindowId window,
        WorkspaceId workspace,
        string windowClass,
        string title,
        bool floating,
        long processId,
        string initialClass,
        string initialTitle) =>
        new(
            id,
            window,
            Floating: floating,
            Workspace: workspace,
            Class: windowClass,
            Title: title,
            ProcessId: processId,
            InitialClass: initialClass.Length == 0 ? windowClass : initialClass,
            InitialTitle: initialTitle.Length == 0 ? title : initialTitle);
}
