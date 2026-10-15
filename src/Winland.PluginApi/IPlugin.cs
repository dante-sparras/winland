namespace Winland.PluginApi;

/// <summary>The type a plugin assembly exposes. The host picks it by <see cref="Name"/>.</summary>
public interface IPlugin
{
    string Name { get; }

    /// <summary>Must equal <see cref="PluginApiVersion.Current"/> or the host refuses to load.</summary>
    int ApiVersion { get; }

    void Initialize(IPluginHost host);

    void Shutdown();
}
