namespace Winland.PluginApi;

public delegate PluginResult PluginDispatcher(string arguments);

public interface IPluginWarnings
{
    void Warn(string message);
}

public delegate void PluginKeywordHandler(string value, IPluginWarnings warnings);

public delegate void PluginEventHandler(PluginEvent pluginEvent);

/// <summary>Result of <see cref="IPluginHost.RegisterLayout"/>. Layouts are not supported yet.</summary>
public readonly record struct LayoutRegistration(bool Supported, string Reason);

/// <summary>What a plugin can register while it is loaded.</summary>
public interface IPluginHost
{
    void RegisterDispatcher(string name, PluginDispatcher handler);

    void RegisterKeyword(string name, PluginKeywordHandler handler);

    void Subscribe(PluginEventHandler handler);

    /// <summary>
    /// Reserved for a later change. Always returns a registration whose
    /// <see cref="LayoutRegistration.Supported"/> is false.
    /// </summary>
    LayoutRegistration RegisterLayout(string name);
}
