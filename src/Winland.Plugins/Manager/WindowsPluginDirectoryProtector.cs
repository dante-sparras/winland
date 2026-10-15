using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Winland.Plugins.Manager;

/// <summary>
/// Replaces the inherited access list of a new plugin directory: SYSTEM and Administrators
/// get full control and Users may read. A folder under ProgramData otherwise lets users create files.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPluginDirectoryProtector : IPluginDirectoryProtector
{
    public void Protect(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            inherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
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
        new DirectoryInfo(directory).SetAccessControl(security);
    }
}
