namespace Winland;

public readonly record struct PluginSync(bool RegisteredKeywords, IReadOnlyList<string> Warnings)
{
    /// <summary>True when this pass registered or dropped a plugin layout, so the config is read again.</summary>
    public bool LayoutsChanged { get; init; }
}

/// <summary>Loads and unloads plugins for the core. The daemon supplies the implementation.</summary>
public interface IPluginRuntime
{
    PluginSync Sync(ConfigDocument document, string? baseDirectory = null);

    string ListJson();

    string Load(string path);

    string Unload(string name);
}
