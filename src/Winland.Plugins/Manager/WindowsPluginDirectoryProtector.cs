using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

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

    internal static DirectorySecurity Protection()
    {
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var security = new DirectorySecurity();
        security.SetOwner(administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            inherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            administrators,
            FileSystemRights.FullControl,
            inherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute,
            inherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }
}
