namespace Winland.Core.Shell;

/// <summary>Chords that leave the tiler: launch, lock, bar, clipboard, capture, panels, and plugin commands.</summary>
public interface IShellActions
{
    /// <summary>Runs a command the Host runs. Every <see cref="HostAction"/> has an arm.</summary>
    /// <param name="action">The action.</param>
    void Run(HostAction action);

    /// <summary>Opens a private window of <paramref name="browser"/>. A browser with no flag opens plainly.</summary>
    /// <param name="browser">The browser's program, or empty for the browser Windows opens a link with.</param>
    /// <param name="privateFlag">The flag that makes the window private, or empty when Winoma knows none for that browser.</param>
    void OpenBrowser(string browser, string privateFlag);

    /// <summary>The window that has the keyboard now, for the chord that opens File Explorer where a terminal is.</summary>
    /// <returns>The window's class and a way to read the terminal's directory.</returns>
    FrontWindow Front();

    /// <summary>Opens File Explorer on <paramref name="path"/>, which is a folder that was there when it was chosen.</summary>
    /// <param name="path">A folder.</param>
    void OpenFolder(string path);

    /// <summary>Starts the program the Host names for <paramref name="role"/>. A role with no program starts nothing.</summary>
    /// <param name="role">A role name in <see cref="AppRoles"/>, such as <c>music</c>.</param>
    void RunApp(string role);

    /// <summary>Sends <paramref name="command"/> to the plugin that declares it. A plugin that is not running ignores it.</summary>
    /// <param name="command">The plugin, the command name, and the phase.</param>
    void RunPlugin(ShellCommand.PluginCommand command);

    /// <summary>Opens the panel for <paramref name="id"/>, or closes it when that panel is already open.</summary>
    /// <param name="id">Plugin id.</param>
    void OpenPanel(string id);

    /// <summary>Opens the numbered panel widget on the right of the bar. The tray is not counted.</summary>
    /// <param name="number">1-based index among the visible panel widgets on the right.</param>
    void OpenRightPanel(int number);
}
