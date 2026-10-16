namespace Winland.Plugins;

public static class PluginNames
{
    /// <summary>A plugin name is letters, digits, <c>_</c> and <c>-</c>. It names the installed file.</summary>
    public static bool IsValid(string? name) =>
        !string.IsNullOrEmpty(name) && name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');

    /// <summary>
    /// True when two plugin names name the same installed file. File names do not differ by case on Windows,
    /// so <c>Foo</c> and <c>foo</c> are one plugin.
    /// </summary>
    public static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> is inside <paramref name="directory"/>, not the directory itself.</summary>
    public static bool IsInside(string directory, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
