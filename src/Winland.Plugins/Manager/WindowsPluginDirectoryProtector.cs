using System.Runtime.Versioning;
using System.Security.AccessControl;

namespace Winland.Plugins.Manager;

/// <summary>
/// Replaces the owner and the inherited access list of the plugin directory: Administrators owns
/// it, SYSTEM and Administrators get full control and Users may read. A folder under ProgramData
/// otherwise lets users create files, and a folder a user created is owned by that user.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPluginDirectoryProtector : IPluginDirectoryProtector
{
    public void Protect(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        // The folder is opened itself, not what a junction there points to, and the access list is
        // set through that handle. A junction swapped in after a check by path would otherwise have
        // another folder's access list rewritten. Without delete sharing it cannot be renamed while open.
        using var handle = WindowsFileHandles.Open(
            directory,
            WindowsFileHandles.ReadAttributes | WindowsFileHandles.ReadControl | WindowsFileHandles.WriteDac | WindowsFileHandles.WriteOwner,
            FileShare.Read | FileShare.Write,
            followReparse: false);
        var attributes = WindowsFileHandles.Attributes(handle);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException($"Refusing to protect '{directory}' because it is a junction or another reparse point.");

        if (!attributes.HasFlag(FileAttributes.Directory))
            throw new InvalidOperationException($"Refusing to protect '{directory}' because it is not a folder.");

        WindowsFileHandles.WriteSecurity(handle, Protection().GetSecurityDescriptorBinaryForm());
    }

    internal static DirectorySecurity Protection() => AdministratorFolder.Security(owner: true);
}
