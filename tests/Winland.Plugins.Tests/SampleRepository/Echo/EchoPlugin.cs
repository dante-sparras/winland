using Winland.PluginApi;

namespace Echo;

public sealed class EchoPlugin : IPlugin
{
    public string Name => "echo";

    public int ApiVersion => PluginApiVersion.Current;

    public void Initialize(IPluginHost host) =>
        host.RegisterDispatcher("echo", static arguments => PluginResult.Success(arguments));

    public void Shutdown()
    {
    }
}
