
namespace Winland.Daemon;

/// <summary>
/// Where the recovery file lives, and which folder an elevated Winland may write it in.
/// Winland writes, replaces and deletes that file with administrator rights, so the folder must be
/// one the user could already empty: then nothing Winland does there is beyond what any program the
/// user runs could do itself. A folder only administrators can write is exactly the wrong kind, since
/// a path the user picked into one, such as a system file, would be replaced and then deleted.
/// </summary>
static class StateLocation
{
    public const string Variable = "WINLAND_STATE";

    /// <summary>
    /// Picks the recovery file. The user's environment can be set by any program the user runs, so
    /// an elevated Winland follows <see cref="Variable"/> only to a folder <paramref name="refuseFolder"/>
    /// passes. Otherwise the default is used and <c>Ignored</c> says why.
    /// </summary>
    public static (string Path, string? Ignored) Choose(string? variable, string defaultPath, bool elevated, Func<string, string?> refuseFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPath);
        ArgumentNullException.ThrowIfNull(refuseFolder);
        if (string.IsNullOrWhiteSpace(variable))
            return (defaultPath, null);

        if (!elevated)
            return (variable, null);

        var folder = Path.GetDirectoryName(Path.GetFullPath(variable));
        var refused = folder is null ? $"'{variable}' names no folder." : refuseFolder(folder);
        return refused is null
            ? (variable, null)
            : (defaultPath, $"Ignoring {Variable}, so the state file is '{defaultPath}': {refused}");
    }

    /// <summary>
    /// Refuses a folder unless <paramref name="user"/> owns it or may delete or rename what it holds.
    /// A null access list could not be read.
    /// </summary>
    public static string? Refusal(string folder, AccessList? access, string user)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        if (access is not AccessList list)
            return $"Winland could not check who can write '{folder}'.";

        if (user.Equals(list.Owner, StringComparison.OrdinalIgnoreCase))
            return null;

        foreach (var grant in list.Grants)
        {
            if (grant.AppliesToTarget && grant.CanRemove && user.Equals(grant.Sid, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return $"'{folder}' is not a folder the user can already empty, and Winland writes the state file there as an administrator.";
    }
}
