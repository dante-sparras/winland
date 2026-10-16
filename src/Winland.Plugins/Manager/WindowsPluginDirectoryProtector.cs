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
        new DirectoryInfo(directory).SetAccessControl(Protection());
    }

    internal static DirectorySecurity Protection() => AdministratorFolder.Security(owner: true);
}
