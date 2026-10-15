namespace Winland;

/// <summary>The text a plugin dispatcher reports. <see cref="Error"/> is null on success.</summary>
public readonly record struct DispatcherResult(string? Error, string? Notice);

/// <summary>
/// Names the core will run. A bind or a pipe command for any other name is an error.
/// Built-in names are registered here. A plugin binds a handler with <see cref="TryBind"/>.
/// </summary>
public static class Dispatchers
{
    private static readonly Lock Gate = new();
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Func<string, DispatcherResult>> Handlers = new(StringComparer.OrdinalIgnoreCase);

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
            "focusworkspaceoncurrentmonitor",
            "focusmonitor",
            "movecurrentworkspacetomonitor",
            "moveworkspacetomonitor",
            "swapactiveworkspaces",
            "togglespecialworkspace",
            "layoutmsg",
            "layout",
            "movetoroot",
            "submap",
            "focuswindow",
            "closewindow",
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
            "pass",
            "sendshortcut",
            "sendkeystate",
            "focusurgentorlast",
            "toggleswallow",
            "fullscreen",
            "fullscreenstate",
            "maximize");
        Register([.. DwindleMessages.Names]);
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

    public static bool TryBind(string name, Func<string, DispatcherResult> handler, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        lock (Gate)
        {
            if (Names.Contains(name))
            {
                error = Handlers.ContainsKey(name)
                    ? $"Dispatcher '{name}' is already registered."
                    : $"Dispatcher '{name}' is reserved.";
                return false;
            }

            Names.Add(name);
            Handlers[name] = handler;
            error = null;
            return true;
        }
    }

    public static void Unbind(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        lock (Gate)
        {
            if (!Handlers.Remove(name))
                return;

            Names.Remove(name);
        }
    }

    public static bool TryInvoke(string name, string arguments, out DispatcherResult result)
    {
        Func<string, DispatcherResult>? handler;
        lock (Gate)
        {
            if (string.IsNullOrWhiteSpace(name) || !Handlers.TryGetValue(name, out handler))
            {
                result = default;
                return false;
            }
        }

        result = handler(arguments ?? "");
        return true;
    }
}
