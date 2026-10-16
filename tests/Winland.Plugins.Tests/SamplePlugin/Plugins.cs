using Winland.PluginApi;

namespace Winland.SamplePlugin;

public sealed class SamplePlugin : IPlugin
{
    public string Name => "sample";

    public int ApiVersion => PluginApiVersion.Current;

    public void Initialize(IPluginHost host)
    {
        var layout = host.RegisterLayout("sample");
        if (layout.Supported)
            throw new InvalidOperationException("Layouts are not supported.");

        host.RegisterDispatcher("sampleecho", arguments => PluginResult.Success(arguments));
        host.RegisterKeyword("samplekeyword", (value, warnings) =>
        {
            if (value == "bad")
                warnings.Warn("sample keyword rejected 'bad'.");
        });
        host.Subscribe(static _ => { });
    }

    public void Shutdown()
    {
    }
}

public sealed class FaultyPlugin : IPlugin
{
    public string Name => "faulty";

    public int ApiVersion => PluginApiVersion.Current;

    public void Initialize(IPluginHost host) =>
        host.RegisterDispatcher("boom", static _ => throw new InvalidOperationException("boom"));

    public void Shutdown()
    {
    }
}

public sealed class HangingPlugin : IPlugin
{
    public string Name => "hanger";

    public int ApiVersion => PluginApiVersion.Current;

    public void Initialize(IPluginHost host) =>
        host.RegisterDispatcher("hang", static _ =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(5));
            return PluginResult.Success();
        });

    public void Shutdown()
    {
    }
}

public sealed class BadVersionPlugin : IPlugin
{
    public string Name => "badversion";

    public int ApiVersion => throw new InvalidOperationException("no version");

    public void Initialize(IPluginHost host) =>
        throw new InvalidOperationException("badversion should not initialize");

    public void Shutdown()
    {
    }
}

// Every load constructs every plugin type in the assembly, so this one is slow only when a test asks.
public sealed class SlowStartPlugin : IPlugin
{
    public const string Variable = "WINLAND_SAMPLE_SLOW_START";

    public SlowStartPlugin()
    {
        if (Environment.GetEnvironmentVariable(Variable) == "1")
            Thread.Sleep(TimeSpan.FromSeconds(5));
    }

    public string Name => "slowstart";

    public int ApiVersion => PluginApiVersion.Current;

    public void Initialize(IPluginHost host)
    {
    }

    public void Shutdown()
    {
    }
}

public sealed class MismatchPlugin : IPlugin
{
    public string Name => "mismatch";

    public int ApiVersion => PluginApiVersion.Current + 1;

    public void Initialize(IPluginHost host) =>
        throw new InvalidOperationException("mismatch should not initialize");

    public void Shutdown()
    {
    }
}
