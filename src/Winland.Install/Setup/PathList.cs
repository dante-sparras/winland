namespace Winland.Install;

/// <summary>Adds and removes one directory on a <c>PATH</c> value without expanding or reordering the rest.</summary>
public static class PathList
{
    /// <summary>The value to store, or null when <paramref name="current"/> already contains <paramref name="folder"/>.</summary>
    public static string? Add(string? current, string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var entries = Split(current);
        if (entries.Any(entry => Same(entry, folder)))
            return null;

        entries.Add(folder);
        return string.Join(';', entries);
    }

    /// <summary>The value to store, or null when <paramref name="folder"/> is not an entry.</summary>
    public static string? Remove(string? current, string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var entries = Split(current);
        if (entries.RemoveAll(entry => Same(entry, folder)) == 0)
            return null;

        return string.Join(';', entries);
    }

    private static List<string> Split(string? current)
    {
        if (string.IsNullOrWhiteSpace(current))
            return [];

        return current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static bool Same(string entry, string folder) =>
        Normalize(entry).Equals(Normalize(folder), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string entry)
    {
        var value = entry.Trim().Trim('"');
        if (value.Length > 3)
            value = value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return value;
    }
}
