namespace Winland;

public sealed partial class Core
{
    public IPluginRuntime? Plugins { get; set; }

    private CommandReply PluginCommand(PluginRequest request)
    {
        if (Plugins is null)
            return new CommandReply("error: plugins are not available", View([]));

        var text = request switch
        {
            PluginListRequest => Plugins.ListJson(),
            PluginLoadRequest load => Plugins.Load(load.AssemblyPath),
            PluginUnloadRequest unload => Plugins.Unload(unload.Name),
            _ => "error: unknown request",
        };
        return new CommandReply(text, View([]));
    }
}
