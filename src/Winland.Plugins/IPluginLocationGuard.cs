namespace Winland.Plugins;

/// <summary>
/// Refuses a load when the plugin directory or the approvals file is writable by a non-administrator.
/// A null return means the location is protected. Tests pass a fake.
/// </summary>
public interface IPluginLocationGuard
{
    string? Deny(string pluginDirectory, string approvalsFile);

    /// <summary>
    /// Refuses an approvals file, opened for reading, that a non-administrator can write. The check
    /// is made on the handle that is read from, so a file put in its place after <see cref="Deny"/>
    /// cannot pass.
    /// </summary>
    string? DenyApprovals(FileStream approvals);
}

public interface IPluginLog
{
    void Write(string message);
}
