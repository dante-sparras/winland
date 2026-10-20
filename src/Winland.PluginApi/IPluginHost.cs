namespace Winland.PluginApi;

public delegate PluginResult PluginDispatcher(string arguments);

public interface IPluginWarnings
{
    void Warn(string message);
}

public delegate void PluginKeywordHandler(string value, IPluginWarnings warnings);

public delegate void PluginEventHandler(PluginEvent pluginEvent);

/// <summary>
/// Result of <see cref="IPluginHost.RegisterLayout"/>. <see cref="Reason"/> is empty when
/// <see cref="Supported"/> is true.
/// </summary>
public readonly record struct LayoutRegistration(bool Supported, string Reason);

/// <summary>What a plugin can register while it is loaded.</summary>
public interface IPluginHost
{
    void RegisterDispatcher(string name, PluginDispatcher handler);

    void RegisterKeyword(string name, PluginKeywordHandler handler);

    void Subscribe(PluginEventHandler handler);

    /// <summary>
    /// Registers a tiled layout under <paramref name="name"/>. <paramref name="create"/> makes one
    /// <see cref="PluginLayout"/> for each workspace that selects the name, as the pinned release makes one algorithm
    /// per workspace. <c>general:layout</c> and a workspace rule select it by that name.
    /// </summary>
    LayoutRegistration RegisterLayout(string name, Func<PluginLayout> create);
}
