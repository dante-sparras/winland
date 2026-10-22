namespace Winland.Daemon;

sealed class FileConfigSource(string directory) : IConfigSource
{
    public string? Read(string path)
    {
        var full = Path.IsPathRooted(path) ? path : Path.Combine(directory, path);
        try
        {
            // An editor may hold the file, so the read shares it.
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string? Variable(string name) => Environment.GetEnvironmentVariable(name);

    public IReadOnlyList<string> Match(string pattern)
    {
        var full = Path.IsPathRooted(pattern) ? pattern : Path.Combine(directory, pattern);
        var (root, depth) = Plan(full);
        if (!Directory.Exists(root))
            return [];

        // A wildcard never matches a separator, so a match lies exactly as deep as the pattern
        // has folders after the root. A folder that cannot be listed is skipped, not fatal.
        var options = new EnumerationOptions
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = true,
            RecurseSubdirectories = depth > 0,
            MaxRecursionDepth = depth,
        };
        try
        {
            return Directory.EnumerateFiles(root, "*", options)
                .Where(file => SourcePath.Matches(full, file))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The folder to search, the last one before the first wildcard, and how many folders
    /// below it a match lies. A drive root keeps its separator: "C:" alone is the current
    /// folder on drive C.
    /// </summary>
    internal static (string Root, int Depth) Plan(string full)
    {
        var separator = Path.DirectorySeparatorChar;
        var normalized = full.Replace(Path.AltDirectorySeparatorChar, separator);
        var wildcard = normalized.IndexOfAny(['*', '?', '[']);
        var prefix = wildcard < 0 ? normalized : normalized[..wildcard];
        var slash = prefix.LastIndexOf(separator);
        if (slash < 0)
            return ("", 0);

        var root = prefix[..slash];
        var driveRoot = Path.GetPathRoot(normalized);
        if (!string.IsNullOrEmpty(driveRoot) && root.Length < driveRoot.Length)
            root = driveRoot;

        var depth = normalized[(slash + 1)..].Count(ch => ch == separator);
        return (root, depth);
    }
}
