namespace Winland.Core.Chords;

/// <summary>
/// The chord ids stored in <c>bindings.json</c>. A window, workspace, or layout id is parsed by <see cref="WinlandCommands"/>.
/// An App chord's is <c>app.</c> and a role in <see cref="AppRoles"/>.
/// A command with no payload that the Host runs is a row in <see cref="ChordCommandTable"/>.
/// A plugin's commands are <c>plugin:&lt;id&gt;:&lt;name&gt;</c>.
/// </summary>
public static class ChordCommands
{
    /// <summary>The id <paramref name="command"/> is stored as, or null when it has no id.</summary>
    /// <param name="command">A command.</param>
    /// <returns>Its chord id, or null.</returns>
    public static string? IdOf(ShellCommand command)
    {
        if (WinlandShell.ToWinland(command) is { } owned)
        {
            return WinlandCommands.IdOf(owned);
        }

        return command switch
        {
            ShellCommand.Host => ChordCommandTable.Of(command)?.Id,
            ShellCommand.AppRole app => AppRoles.Find(app.Role)?.Id,
            ShellCommand.OpenPanel panel => "panel:" + panel.Id,
            ShellCommand.OpenRightPanel index => "right-panel:" + index.Number,
            ShellCommand.PluginCommand plugin => ChordPluginIds.Of(plugin.Plugin, plugin.Name),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
    }

    /// <summary>
    /// The command named <paramref name="id"/>, or null when the id is unknown.
    /// A <c>plugin:&lt;id&gt;:&lt;name&gt;</c> id is read by its shape; whether that plugin declares the name is checked where the manifests are known.
    /// So is a <c>panel:&lt;id&gt;</c> id, and whether that plugin declares a panel.
    /// </summary>
    /// <param name="id">A chord id.</param>
    /// <param name="command">The command, when this returns true.</param>
    /// <returns>True when <paramref name="id"/> names a command.</returns>
    public static bool TryParse(string id, out ShellCommand? command)
    {
        command = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (WinlandCommands.TryParse(id, out var owned) && owned is not null)
        {
            command = WinlandShell.ToShell(owned);
            return true;
        }

        if (id.StartsWith(ChordPluginIds.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!ChordPluginIds.TryParse(id, out var owner, out var name))
            {
                return false;
            }

            command = new ShellCommand.PluginCommand(owner, name, BindPhase.Run);
            return true;
        }

        if (id.StartsWith(AppRoles.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            if (AppRoles.FromId(id) is not { } role)
            {
                return false;
            }

            command = new ShellCommand.AppRole(role.Name);
            return true;
        }

        if (id.StartsWith("panel:", StringComparison.OrdinalIgnoreCase))
        {
            var plugin = id["panel:".Length..];
            if (plugin.Length > 0)
            {
                command = new ShellCommand.OpenPanel(plugin);
                return true;
            }

            return false;
        }

        if (id.StartsWith("right-panel:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(id["right-panel:".Length..], out var panelNumber)
            && panelNumber is >= 1 and <= 9)
        {
            command = new ShellCommand.OpenRightPanel(panelNumber);
            return true;
        }

        command = ChordCommandTable.Find(id)?.Command;
        return command is not null;
    }
}
