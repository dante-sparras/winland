using System.Runtime.Versioning;

namespace Winland.Plugins;

[SupportedOSPlatform("windows")]
public sealed class WindowsPluginLocationGuard : IPluginLocationGuard
{
    public string? Deny(string pluginDirectory, string approvalsFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalsFile);

        if (!Directory.Exists(pluginDirectory))
            return $"Plugin directory '{pluginDirectory}' does not exist, so plugins will not load.";

        if (!WindowsAccessReader.TryRead(pluginDirectory, out var directory, out var directoryError))
            return directoryError;

        AccessList? approvals = null;
        if (File.Exists(approvalsFile))
        {
            if (!WindowsAccessReader.TryRead(approvalsFile, out var file, out var fileError))
                return fileError;

            approvals = file;
        }

        return PluginLocationPolicy.Deny(pluginDirectory, directory, approvalsFile, approvals);
    }
}
