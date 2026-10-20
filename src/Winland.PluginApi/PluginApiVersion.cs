namespace Winland.PluginApi;

/// <summary>
/// The plugin contract version. A plugin loads only when <see cref="IPlugin.ApiVersion"/>
/// equals <see cref="Current"/>.
/// </summary>
public static class PluginApiVersion
{
    public const int Current = 2;
}
