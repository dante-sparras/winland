namespace Winland.Plugins;

/// <summary>One allow entry from a directory or file access list.</summary>
public readonly record struct PrincipalAccess(string Sid, bool CanWrite, bool AppliesToTarget);

/// <summary>
/// Decides whether a plugin directory or approvals file is writable by a non-administrator.
/// The Windows access-list reader is separate so tests can pass a fake list.
/// </summary>
public static class PluginLocationPolicy
{
    public const string SystemSid = "S-1-5-18";

    public const string AdministratorsSid = "S-1-5-32-544";

    public static bool IsAdministrator(string sid, string? currentUserSid, bool currentUserIsAdmin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        if (sid.Equals(SystemSid, StringComparison.OrdinalIgnoreCase))
            return true;

        if (sid.Equals(AdministratorsSid, StringComparison.OrdinalIgnoreCase))
            return true;

        return currentUserIsAdmin
            && currentUserSid is not null
            && sid.Equals(currentUserSid, StringComparison.OrdinalIgnoreCase);
    }

    public static string? Deny(
        string directoryPath,
        IReadOnlyList<PrincipalAccess> directory,
        string approvalsPath,
        IReadOnlyList<PrincipalAccess>? approvals,
        string? currentUserSid,
        bool currentUserIsAdmin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalsPath);

        var writer = NonAdminWriter(directory, currentUserSid, currentUserIsAdmin);
        if (writer is not null)
            return Message(directoryPath, writer);

        if (approvals is null)
            return null;

        writer = NonAdminWriter(approvals, currentUserSid, currentUserIsAdmin);
        return writer is null ? null : Message(approvalsPath, writer);
    }

    private static string? NonAdminWriter(
        IReadOnlyList<PrincipalAccess> grants,
        string? currentUserSid,
        bool currentUserIsAdmin)
    {
        foreach (var grant in grants)
        {
            if (!grant.AppliesToTarget || !grant.CanWrite || string.IsNullOrWhiteSpace(grant.Sid))
                continue;

            if (!IsAdministrator(grant.Sid, currentUserSid, currentUserIsAdmin))
                return grant.Sid;
        }

        return null;
    }

    private static string Message(string path, string sid) =>
        $"Refusing to load plugins because a non-administrator ({sid}) can write '{path}'.";
}
