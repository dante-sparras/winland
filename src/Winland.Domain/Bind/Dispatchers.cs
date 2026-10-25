using System.Collections.Frozen;

namespace Winland.Domain;

/// <summary>
/// The names of the dispatchers Winland runs. A bind or a pipe command for any other name is an error. The
/// application's dispatcher table runs each of them, and a test keeps the two lists equal.
/// </summary>
public static class Dispatchers
{
    public static FrozenSet<string> Names { get; } =
        new[]
        {
            "exec",
            "killactive",
            "togglefloating",
            "movefocus",
            "movewindow",
            "resizeactive",
            "resizewindow",
            "pseudo",
            "workspace",
            "movetoworkspace",
            "movetoworkspacesilent",
            "focusmonitor",
            "movecurrentworkspacetomonitor",
            "setprop",
            "cyclenext",
            "togglespecialworkspace",
            "layoutmsg",
            "layout",
            "cyclelayout",
            "focuswindow",
            "closeall",
            "pin",
            "swapwindow",
            "exit",
            "sendshortcut",
            "focusurgentorlast",
            "fullscreen",
            "maximize",
            "centerwindow",
            "togglegroup",
            "changegroupactive",
            "movegroupwindow",
            "moveintogroup",
            "moveoutofgroup",
        }
        .Concat(DwindleMessages.Names)
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string? name) => !string.IsNullOrWhiteSpace(name) && Names.Contains(name);
}
