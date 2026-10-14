namespace Winland.Core.Shell;

/// <summary>
/// Hears that the workspace in front changed. The shell starts the person's script.
/// The window manager only names the event.
/// </summary>
public interface IWorkspaceHook
{
    /// <summary>The workspace in front changed.</summary>
    /// <param name="name">Event name. The window manager passes <c>workspace-changed</c>.</param>
    /// <param name="argument">The workspace number, as text. Null when the event has none.</param>
    void Fire(string name, string? argument);
}
