namespace Winland.Plugins;

/// <summary>
/// Decides whether a plugin directory or approvals file is writable by a non-administrator, and
/// whether a folder above the directory would let one swap it for another.
/// The Windows access-list reader is separate so tests can pass a fake list.
/// </summary>
public static class PluginLocationPolicy
{
    public static string? Deny(
        string directoryPath,
        AccessList directory,
        string approvalsPath,
        AccessList? approvals,
        IEnumerable<(string Path, AccessList? Access)> ancestors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalsPath);
        ArgumentNullException.ThrowIfNull(ancestors);

        var writer = AdministratorOnly.NonAdministrator(directory);
        if (writer is not null)
            return Message(directoryPath, writer);

        // A non-administrator who can rename the directory, or a folder above it, can move the
        // protected one away and put a folder of their own, with approvals of their own, in its place.
        if (AdministratorOnly.FirstNonAdministratorAbove(ancestors) is var (above, who))
            return $"Refusing to load plugins because a non-administrator ({who}) can rename or replace what is in '{above}'.";

        return DenyApprovals(approvalsPath, approvals);
    }

    /// <summary>Refuses an approvals file a non-administrator can write. A null list is a file that does not exist.</summary>
    public static string? DenyApprovals(string approvalsPath, AccessList? approvals)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalsPath);
        if (approvals is not AccessList file)
            return null;

        var writer = AdministratorOnly.NonAdministrator(file);
        return writer is null ? null : Message(approvalsPath, writer);
    }

    private static string Message(string path, string who) =>
        $"Refusing to load plugins because a non-administrator ({who}) can write '{path}'.";
}
