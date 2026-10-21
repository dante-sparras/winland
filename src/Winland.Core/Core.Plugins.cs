namespace Winland;

public sealed partial class Core
{
    public IPluginRuntime? Plugins { get; set; }

    private CommandReply PluginCommand(PluginRequest request)
    {
        if (Plugins is null)
            return new CommandReply("error: plugins are not available", View([]));

        if (request is PluginListRequest)
            return new CommandReply(Plugins.ListJson(), View([]));

        var before = PluginLayouts.Names();
        var text = request switch
        {
            PluginLoadRequest load => Plugins.Load(load.AssemblyPath),
            PluginUnloadRequest unload => Plugins.Unload(unload.Name),
            _ => "error: unknown request",
        };

        // A plugin that registers or drops a layout switches the workspaces that name it, as Winland does
        // when an algorithm is added or removed. A load that registers nothing leaves them as they are.
        if (text != "ok" || SameNames(before, PluginLayouts.Names()))
            return new CommandReply(text, View([]));

        foreach (var desk in _workspaces)
            MatchLayout(desk);

        return new CommandReply(text, View(ShownPlacements()));
    }

    private static bool SameNames(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        if (before.Count != after.Count)
            return false;

        var seen = new HashSet<string>(before, StringComparer.Ordinal);
        foreach (var name in after)
        {
            if (!seen.Remove(name))
                return false;
        }

        return seen.Count == 0;
    }
}
