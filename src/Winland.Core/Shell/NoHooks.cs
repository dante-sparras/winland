namespace Winland.Core.Shell;

/// <summary>A workspace hook that starts nothing. The window manager uses it when the shell passed none.</summary>
public sealed class NoHooks : IWorkspaceHook
{
    /// <summary>The shared instance.</summary>
    public static readonly NoHooks Instance = new();

    private NoHooks()
    {
    }

    /// <inheritdoc />
    public void Fire(string name, string? argument)
    {
    }
}
