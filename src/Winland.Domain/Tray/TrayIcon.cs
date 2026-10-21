using System.Globalization;

namespace Winland.Domain;

/// <summary>One command on the notification-area icon's menu.</summary>
public enum TrayCommand
{
    Reload,
    EditConfig,
    OpenLog,
    Quit,
}

/// <summary>A menu row: the command it runs and the words the menu shows.</summary>
public readonly record struct TrayMenuItem(TrayCommand Command, string Label);

/// <summary>
/// The notification-area icon. It shows the number of the Workspace the focused Monitor is showing,
/// and its menu runs Winland.
/// </summary>
public sealed record TrayIcon(bool Shown, string Number)
{
    public const string Keyword = "general:tray_icon";

    public static IReadOnlyList<TrayMenuItem> Menu { get; } =
    [
        new(TrayCommand.Reload, "Reload"),
        new(TrayCommand.EditConfig, "Edit config"),
        new(TrayCommand.OpenLog, "Open log"),
        new(TrayCommand.Quit, "Quit"),
    ];

    /// <summary>The tooltip: the Workspace the icon is showing.</summary>
    public string Tip => "Workspace " + Number;

    /// <summary>The icon for <paramref name="shown"/> and the Workspace the focused Monitor is showing.</summary>
    public static TrayIcon For(bool shown, WorkspaceId workspace) => new(shown, Text(workspace));

    /// <summary>
    /// The text the icon draws. A numbered Workspace contributes its number, including a Guest's number on
    /// the Monitor that is showing it. A special workspace contributes the name after <c>special:</c>.
    /// </summary>
    public static string Text(WorkspaceId workspace)
    {
        if (workspace.TryGetNumber(out var number))
            return number.ToString(CultureInfo.InvariantCulture);

        const string Prefix = "special:";
        var name = workspace.Name;
        if (name.StartsWith(Prefix, StringComparison.Ordinal))
            name = name[Prefix.Length..];

        return name.Length == 0 ? "special" : name;
    }
}
