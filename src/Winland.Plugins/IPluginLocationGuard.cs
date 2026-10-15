namespace Winland.Plugins;

/// <summary>
/// Refuses a load when the plugin directory or the approvals file is writable by a non-administrator.
/// A null return means the location is protected. Tests pass a fake.
/// </summary>
public interface IPluginLocationGuard
{
    string? Deny(string pluginDirectory, string approvalsFile);
}

public interface IPluginLog
{
    void Write(string message);
}
