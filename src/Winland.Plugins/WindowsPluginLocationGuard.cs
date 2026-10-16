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

        // The access lists are read through the paths, which would follow a junction to a folder
        // whose own parents are never checked.
        var reparse = ReparsePoint(pluginDirectory) ?? ReparsePoint(approvalsFile);
        if (reparse is not null)
            return $"Refusing to load plugins because '{reparse}' is a junction or another reparse point.";

        if (!WindowsAccessReader.TryRead(pluginDirectory, out var directory, out var directoryError))
            return directoryError;

        AccessList? approvals = null;
        if (File.Exists(approvalsFile))
        {
            if (!WindowsAccessReader.TryRead(approvalsFile, out var file, out var fileError))
                return fileError;

            approvals = file;
        }

        return PluginLocationPolicy.Deny(pluginDirectory, directory, approvalsFile, approvals, WindowsAccessReader.Ancestors(pluginDirectory));
    }

    public string? DenyApprovals(FileStream approvals)
    {
        ArgumentNullException.ThrowIfNull(approvals);
        if (!WindowsAccessReader.TryRead(approvals, out var access))
            return $"Could not verify that '{approvals.Name}' is protected from non-administrators.";

        return PluginLocationPolicy.DenyApprovals(approvals.Name, access);
    }

    private static string? ReparsePoint(string path)
    {
        for (FileSystemInfo? current = File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path);
            current is not null;
            current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return current.FullName;
        }

        return null;
    }
}
