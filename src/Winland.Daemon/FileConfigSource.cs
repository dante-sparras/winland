namespace Winland;

sealed class FileConfigSource(string directory) : IConfigSource
{
    public string? Read(string path)
    {
        var full = Path.IsPathRooted(path) ? path : Path.Combine(directory, path);
        try
        {
            return File.ReadAllText(full);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IReadOnlyList<string> Match(string pattern)
    {
        var full = Path.IsPathRooted(pattern) ? pattern : Path.Combine(directory, pattern);
        var separator = Path.DirectorySeparatorChar;
        var normalized = full.Replace('/', separator).Replace('\\', separator);
        var wildcard = normalized.IndexOfAny(['*', '?', '[']);
        var prefix = wildcard < 0 ? normalized : normalized[..wildcard];
        var slash = prefix.LastIndexOf(separator);
        var root = slash < 0 ? directory : prefix[..slash];
        if (root.Length == 0 || !Directory.Exists(root))
            return [];

        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(file => SourcePath.Matches(full, file))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
