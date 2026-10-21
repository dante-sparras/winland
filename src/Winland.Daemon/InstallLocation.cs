namespace Winland;

/// <summary>
/// Decides whether the logon task may start Winland from a folder. The task starts it with
/// administrator rights at every logon, so a non-administrator must not be able to change what it
/// starts: not by writing the folder or a file in it, and not by renaming the folder, or one above
/// it, away and putting another in its place.
/// </summary>
static class InstallLocation
{
    /// <summary>Why the folder is refused, or null. A null access list could not be read.</summary>
    public static string? Refusal(
        IEnumerable<(string Path, AccessList? Access)> tree,
        IEnumerable<(string Path, AccessList? Access)> ancestors)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(ancestors);
        if (AdministratorOnly.FirstNonAdministrator(tree) is var (path, who))
            return $"a non-administrator ({who}) can write '{path}'";

        if (AdministratorOnly.FirstNonAdministratorAbove(ancestors) is var (above, by))
            return $"a non-administrator ({by}) can rename or replace what is in '{above}'";

        return null;
    }
}
