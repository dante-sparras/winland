using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Winland.Daemon;

/// <summary>Folders that only SYSTEM and Administrators can write, for the elevated programs.</summary>
[SupportedOSPlatform("windows")]
public static class AdministratorFolder
{
    /// <summary>
    /// Creates a new folder in <paramref name="parent"/> whose access list is set as it is created:
    /// SYSTEM and Administrators get full control and Users may read. Throws
    /// <see cref="InvalidOperationException"/> when the folder that results is not protected, such as
    /// one a non-administrator made first, or one made by a process that is not elevated.
    /// </summary>
    public static string CreateNew(string parent, string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var folder = Path.Combine(Path.GetFullPath(parent), prefix + Guid.NewGuid().ToString("N"));
        new DirectoryInfo(folder).Create(Security(owner: false));
        if (WindowsAccessReader.FirstNonAdministrator(folder) is var (path, who))
        {
            throw new InvalidOperationException(
                $"Could not make a folder only administrators can write: a non-administrator ({who}) can write '{path}'.");
        }

        return folder;
    }

    /// <summary>
    /// The protected access list. With <paramref name="owner"/>, Administrators also becomes the
    /// owner, which only an elevated process may set.
    /// </summary>
    internal static DirectorySecurity Security(bool owner)
    {
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var security = new DirectorySecurity();
        if (owner)
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
