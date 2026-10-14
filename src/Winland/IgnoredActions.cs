using Winland.Core.Chords;
using Winland.Core.Diagnostics;
using Winland.Core.Shell;

namespace Winland;

/// <summary>
/// Host actions belong to the shell. A window command is never one of these, and a host action that arrives here was mis-routed.
/// </summary>
internal sealed class IgnoredActions : IShellActions
{
    /// <inheritdoc />
    public void Run(HostAction action) => Log.Warning("Winland", "a host action reached the window manager: " + action);

    /// <inheritdoc />
    public void OpenBrowser(string browser, string privateFlag) =>
        Log.Warning("Winland", "a browser launch reached the window manager");

    /// <inheritdoc />
    public FrontWindow Front() => new("", static () => null);

    /// <inheritdoc />
    public void OpenFolder(string path) => Log.Warning("Winland", "a folder launch reached the window manager");

    /// <inheritdoc />
    public void RunApp(string role) => Log.Warning("Winland", "an app role reached the window manager: " + role);

    /// <inheritdoc />
    public void RunPlugin(ShellCommand.PluginCommand command) =>
        Log.Warning("Winland", "a plugin command reached the window manager");

    /// <inheritdoc />
    public void OpenPanel(string id) => Log.Warning("Winland", "a panel reached the window manager: " + id);

    /// <inheritdoc />
    public void OpenRightPanel(int number) => Log.Warning("Winland", "a right panel reached the window manager");
}
