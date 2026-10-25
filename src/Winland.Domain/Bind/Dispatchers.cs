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
            "execr",
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
            "moveworkspacetomonitor",
            "swapactiveworkspaces",
            "changeworkspaceid",
            "setprop",
            "cyclenext",
            "togglespecialworkspace",
            "layoutmsg",
            "layout",
            "cyclelayout",
            "movetoroot",
            "submap",
            "focuswindow",
            "closewindow",
            "closeall",
            "killwindow",
            "setfloating",
            "settiled",
            "pin",
            "swapwindow",
            "exit",
            "no_op",
            "event",
            "forcerendererreload",
            "dpms",
            "sendshortcut",
            "focusurgentorlast",
            "focuscurrentorlast",
            "fullscreen",
            "maximize",
            "movewindowpixel",
            "resizewindowpixel",
            "centerwindow",
            "bringactivetotop",
            "alterzorder",
            "movecursortocorner",
            "movecursor",
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
