using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Winland.Plugins;

/// <summary>Reads the owner and the allow entries of a file or directory for <see cref="AdministratorOnly"/>.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsAccessReader
{
    /// <summary>Reads the access list of a path. Returns false and an error when it cannot be read.</summary>
    public static bool TryRead(string path, out AccessList access, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            FileSystemSecurity security;
            if (File.Exists(path))
                security = new FileInfo(path).GetAccessControl();
            else
                security = new DirectoryInfo(path).GetAccessControl();

            access = From(security);
            error = null;
            return true;
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            access = default;
            error = $"Could not verify that '{path}' is protected from non-administrators.";
            return false;
        }
    }

    /// <summary>
    /// Reads the access list of an open file. Checking the handle that is read from means a path
    /// swapped between the check and the read cannot pass off another file.
    /// </summary>
    public static bool TryRead(FileStream stream, out AccessList access)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            access = From(stream.GetAccessControl());
            return true;
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            access = default;
            return false;
        }
    }

    internal static bool CanWrite(FileSystemRights rights)
    {
        // Only the bits that write. Modify and FullControl also hold the read bits,
        // so a mask that included them would count a read-only grant as a write.
        const FileSystemRights write =
            FileSystemRights.WriteData
            | FileSystemRights.AppendData
            | FileSystemRights.WriteExtendedAttributes
            | FileSystemRights.WriteAttributes
            | FileSystemRights.Delete
            | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.ChangePermissions
            | FileSystemRights.TakeOwnership;

        // GENERIC_ALL and GENERIC_WRITE, which an inherit-only entry can hold unmapped.
        const int generic = 0x10000000 | 0x40000000;

        return (rights & write) != 0 || ((int)rights & generic) != 0;
    }

    private static AccessList From(FileSystemSecurity security)
    {
        var owner = security.GetOwner(typeof(SecurityIdentifier))?.Value;
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
        var grants = new List<PrincipalAccess>(rules.Count);
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow)
                continue;

            var applies = (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0;
            var sid = rule.IdentityReference.Value;
            grants.Add(new PrincipalAccess(sid, CanWrite(rule.FileSystemRights), applies));
        }

        return new AccessList(owner, grants);
    }

    private static bool IsReadFailure(Exception exception) =>
        exception is UnauthorizedAccessException
            or IOException
            or PlatformNotSupportedException
            or PrivilegeNotHeldException
            or IdentityNotMappedException;
}
