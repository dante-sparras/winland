using System.Globalization;

namespace Winland.Plugins.Manager;

/// <summary>One plugin a repository's manifest lists.</summary>
public sealed record ManifestPlugin(
    string Name,
    int ApiVersion,
    string Project,
    string Assembly,
    string Description,
    IReadOnlyList<string> Authors)
{
    /// <summary>The shell commands that build the plugin, run in order at the repository root. Empty when <see cref="Project"/> is published.</summary>
    public IReadOnlyList<string> Build { get; init; } = [];

    /// <summary>The file the build steps make, relative to the repository root.</summary>
    public string? Output { get; init; }
}

/// <summary>
/// The <c>winland-plugin.toml</c> manifest at the root of a plugin repository.
/// Every table except <c>[repository]</c> is one plugin, named by the table.
/// </summary>
public sealed record PluginManifest(string RepositoryName, IReadOnlyList<string> Authors, IReadOnlyList<ManifestPlugin> Plugins)
{
    public const string FileName = "winland-plugin.toml";

    private const string RepositoryTable = "repository";

    public static PluginManifest Parse(string text)
    {
        var parsed = ManifestToml.Parse(text);
        if (parsed.Error is not null)
            throw new ManifestException(parsed.Error);

        var repositoryName = "";
        IReadOnlyList<string> authors = [];
        var plugins = new List<ManifestPlugin>();
        foreach (var table in parsed.Tables)
        {
            if (table.Name == RepositoryTable)
            {
                repositoryName = Text(table, "name") ?? "";
                authors = AuthorsOf(table);
                continue;
            }

            plugins.Add(Plugin(table));
        }

        if (plugins.Count == 0)
            throw new ManifestException("The manifest lists no plugins.");

        return new PluginManifest(repositoryName, authors, plugins);
    }

    /// <summary>
    /// The refusals for plugins built against another API version. Empty means every plugin matches.
    /// </summary>
    public IReadOnlyList<string> Incompatible(int current) =>
        Plugins
            .Where(plugin => plugin.ApiVersion != current)
            .Select(plugin =>
                $"Plugin '{plugin.Name}' declares API version {plugin.ApiVersion.ToString(CultureInfo.InvariantCulture)}; Winland accepts {current.ToString(CultureInfo.InvariantCulture)}.")
            .ToArray();

    private static ManifestPlugin Plugin(ManifestTable table)
    {
        var name = table.Name;
        if (!table.Values.TryGetValue("api_version", out var version))
            throw new ManifestException($"Plugin '{name}' has no api_version.");

        if (version is not long number || number is < int.MinValue or > int.MaxValue)
            throw new ManifestException($"Plugin '{name}' has an api_version that is not an integer.");

        // As in the pinned manifest, build is a list of shell commands run at the repository root and output the file they make.
        if (table.Values.TryGetValue("build", out var steps))
        {
            if (steps is not List<string> build || build.Count == 0 || build.Any(string.IsNullOrWhiteSpace))
                throw new ManifestException($"Plugin '{name}' has a build that is not a list of commands.");

            var output = Text(table, "output")
                ?? throw new ManifestException($"Plugin '{name}' has build steps and no output.");
            if (!IsInsideRepository(output) || !output.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new ManifestException($"Plugin '{name}' has output '{output}'. It must be a relative path to a .dll inside the repository.");

            var file = output.Replace('\\', '/').Split('/')[^1];
            return new ManifestPlugin(name, (int)number, "", file, Text(table, "description") ?? "", AuthorsOf(table))
            {
                Build = build,
                Output = output,
            };
        }

        var project = Text(table, "project")
            ?? throw new ManifestException($"Plugin '{name}' has no project and no build.");
        if (!IsInsideRepository(project) || !project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            throw new ManifestException($"Plugin '{name}' has project '{project}'. It must be a relative path to a .csproj inside the repository.");

        var assembly = Text(table, "assembly") ?? Path.GetFileNameWithoutExtension(project.Replace('\\', '/').Split('/')[^1]) + ".dll";
        if (assembly.IndexOfAny(['/', '\\', ':']) >= 0
            || assembly.StartsWith('.')
            || !assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || assembly.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ManifestException($"Plugin '{name}' has assembly '{assembly}'. It must be a .dll file name.");

        return new ManifestPlugin(name, (int)number, project, assembly, Text(table, "description") ?? "", AuthorsOf(table));
    }

    private static IReadOnlyList<string> AuthorsOf(ManifestTable table)
    {
        if (table.Values.TryGetValue("authors", out var many) && many is List<string> list)
            return list;

        var one = Text(table, "author");
        return string.IsNullOrWhiteSpace(one) ? [] : [one];
    }

    private static string? Text(ManifestTable table, string key)
    {
        if (!table.Values.TryGetValue(key, out var value))
            return null;

        if (value is not string text)
            throw new ManifestException($"Key '{key}' in [{table.Name}] must be a string.");

        return text;
    }

    private static bool IsInsideRepository(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':', StringComparison.Ordinal))
            return false;

        foreach (var segment in path.Replace('\\', '/').Split('/'))
        {
            if (segment == "..")
                return false;
        }

        return true;
    }
}

public sealed class ManifestException : Exception
{
    public ManifestException()
    {
    }

    public ManifestException(string message)
        : base(message)
    {
    }

    public ManifestException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
