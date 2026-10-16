namespace Winland.Plugins;

/// <summary>One allow entry from a directory or file access list.</summary>
public readonly record struct PrincipalAccess(string Sid, bool CanWrite, bool AppliesToTarget);

/// <summary>The owner and the allow entries of a file or directory. A null owner could not be read.</summary>
public readonly record struct AccessList(string? Owner, IReadOnlyList<PrincipalAccess> Grants);

/// <summary>
/// Decides whether a file or directory is protected from non-administrators: its owner and every
/// principal that may write it are SYSTEM, Administrators or TrustedInstaller. The current user does
/// not count even when it is an administrator, because under UAC every program it runs without
/// elevation has the same SID. The Windows reader is separate so tests can pass a fake list.
/// </summary>
public static class AdministratorOnly
{
    public const string SystemSid = "S-1-5-18";

    public const string AdministratorsSid = "S-1-5-32-544";

    public const string TrustedInstallerSid = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    public static bool IsAllowed(string sid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        return sid.Equals(SystemSid, StringComparison.OrdinalIgnoreCase)
            || sid.Equals(AdministratorsSid, StringComparison.OrdinalIgnoreCase)
            || sid.Equals(TrustedInstallerSid, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Names the first principal other than those three that owns or can write the target, or
    /// returns null when there is none. An owner may always rewrite the access list.
    /// </summary>
    public static string? NonAdministrator(AccessList access)
    {
        ArgumentNullException.ThrowIfNull(access.Grants);
        if (string.IsNullOrWhiteSpace(access.Owner))
            return "unknown owner";

        if (!IsAllowed(access.Owner))
            return "owner " + access.Owner;

        foreach (var grant in access.Grants)
        {
            if (!grant.AppliesToTarget || !grant.CanWrite || string.IsNullOrWhiteSpace(grant.Sid))
                continue;

            if (!IsAllowed(grant.Sid))
                return grant.Sid;
        }

        return null;
    }

    /// <summary>
    /// Finds the first entry that a non-administrator owns or can write, and who that is.
    /// A null access list could not be read, and counts as unprotected.
    /// </summary>
    public static (string Path, string Who)? FirstNonAdministrator(IEnumerable<(string Path, AccessList? Access)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var (path, access) in entries)
        {
            if (access is not AccessList list)
                return (path, "unreadable access list");

            var who = NonAdministrator(list);
            if (who is not null)
                return (path, who);
        }

        return null;
    }
}
