using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

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

        if (!TryRead(pluginDirectory, out var directory, out var directoryError))
            return directoryError;

        IReadOnlyList<PrincipalAccess>? approvals = null;
        if (File.Exists(approvalsFile))
        {
            if (!TryRead(approvalsFile, out var fileGrants, out var fileError))
                return fileError;

            approvals = fileGrants;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        return PluginLocationPolicy.Deny(
            pluginDirectory,
            directory,
            approvalsFile,
            approvals,
            identity.User?.Value,
            isAdmin);
    }

    private static bool TryRead(string path, out IReadOnlyList<PrincipalAccess> grants, out string? error)
    {
        try
        {
            FileSystemSecurity security;
            if (File.Exists(path))
                security = new FileInfo(path).GetAccessControl();
            else
                security = new DirectoryInfo(path).GetAccessControl();

            grants = Grants(security);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or PlatformNotSupportedException
            or PrivilegeNotHeldException
            or IdentityNotMappedException)
        {
            grants = [];
            error = $"Could not verify that '{path}' is protected from non-administrators.";
            return false;
        }
    }

    private static List<PrincipalAccess> Grants(FileSystemSecurity security)
    {
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

        return grants;
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
}
