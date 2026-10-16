using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

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

    /// <summary>Reads the access list of what an open handle refers to, a folder included.</summary>
    public static bool TryRead(SafeFileHandle handle, out AccessList access)
    {
        ArgumentNullException.ThrowIfNull(handle);
        try
        {
            var security = new DirectorySecurity();
            security.SetSecurityDescriptorBinaryForm(
                WindowsFileHandles.ReadSecurity(handle),
                AccessControlSections.Owner | AccessControlSections.Access);
            access = From(security);
            return true;
        }
        catch (Exception exception) when (IsReadFailure(exception) || exception is ArgumentException)
        {
            access = default;
            return false;
        }
    }

    /// <summary>
    /// Finds the first entry in a directory tree, the directory first, that a non-administrator
    /// owns or can write. Null means the whole tree is protected.
    /// </summary>
    public static (string Path, string Who)? FirstNonAdministrator(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return AdministratorOnly.FirstNonAdministrator(Entries(Path.GetFullPath(directory)));
    }

    /// <summary>
    /// Finds the first folder above <paramref name="path"/>, up to the root, through which a
    /// non-administrator could swap it (see <see cref="AdministratorOnly.NonAdministratorAbove"/>).
    /// </summary>
    public static (string Path, string Who)? FirstNonAdministratorAbove(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return AdministratorOnly.FirstNonAdministratorAbove(Ancestors(path));
    }

    /// <summary>The access list of each folder above <paramref name="path"/>, nearest first. Null could not be read.</summary>
    public static IReadOnlyList<(string Path, AccessList? Access)> Ancestors(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var ancestors = new List<(string Path, AccessList? Access)>();
        for (var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))); parent is not null; parent = parent.Parent)
            ancestors.Add((parent.FullName, Read(parent.FullName)));

        return ancestors;
    }

    private static List<(string Path, AccessList? Access)> Entries(string directory)
    {
        var entries = new List<(string Path, AccessList? Access)> { (directory, Read(directory)) };
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
        };
        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", options))
                entries.Add((path, Read(path)));
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            // A folder that cannot be listed cannot be shown to be protected.
            entries.Add((directory, null));
        }

        return entries;
    }

    private static AccessList? Read(string path) =>
        TryRead(path, out var access, out _) ? access : null;

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

    internal static bool CanRemove(FileSystemRights rights)
    {
        // Deleting or renaming the entry or what a folder holds, or taking over its access list.
        const FileSystemRights remove =
            FileSystemRights.Delete
            | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.ChangePermissions
            | FileSystemRights.TakeOwnership;

        // GENERIC_ALL. GENERIC_WRITE maps to rights that only add and change data.
        const int generic = 0x10000000;

        return (rights & remove) != 0 || ((int)rights & generic) != 0;
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
            grants.Add(new PrincipalAccess(sid, CanWrite(rule.FileSystemRights), applies, CanRemove(rule.FileSystemRights)));
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
