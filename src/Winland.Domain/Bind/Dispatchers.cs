namespace Winland.Domain;

/// <summary>
/// Names the core will run. A bind or a pipe command for any other name is an error.
/// Built-in names are registered here.
/// </summary>
public static class Dispatchers
{
    private static readonly Lock Gate = new();
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase);

    static Dispatchers()
    {
        Register(
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
            "tagwindow",
            "cleartags",
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
            "fullscreenstate",
            "maximize",
            "movewindowpixel",
            "resizewindowpixel",
            "centerwindow",
            "bringactivetotop",
            "alterzorder",
            "movecursortocorner",
            "movecursor");
        Register([.. DwindleMessages.Names]);
        Register(
            "togglegroup",
            "changegroupactive",
            "movegroupwindow",
            "lockgroups",
            "lockactivegroup",
            "denywindowfromgroup",
            "moveintogroup",
            "moveintoorcreategroup",
            "moveoutofgroup",
            "movewindoworgroup",
            "setignoregrouplock");
    }

    public static void Register(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        lock (Gate)
        {
            foreach (var name in names)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
                Names.Add(name);
            }
        }
    }

    public static bool IsKnown(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        lock (Gate)
            return Names.Contains(name);
    }
}
