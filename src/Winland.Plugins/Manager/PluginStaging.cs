using System.Security.Cryptography;
using System.Text;
using Winland.PluginApi;

namespace Winland.Plugins.Manager;

/// <summary>Where the manager works and what it builds against.</summary>
/// <param name="Staging">The user-writable folder repositories are cloned and built in. Nothing loads from it.</param>
/// <param name="PluginDirectory">The protected folder installed assemblies are copied into.</param>
/// <param name="Approvals">The protected approvals file.</param>
/// <param name="ApiAssembly">The <c>Winland.PluginApi.dll</c> a plugin builds against.</param>
public sealed record PluginManagerPaths(string Staging, string PluginDirectory, string Approvals, string ApiAssembly)
{
    public const string StagingVariable = "WINLAND_PLUGIN_STAGING";

    /// <summary>The MSBuild property a plugin project reads to reference the API assembly.</summary>
    public const string ApiProperty = "WinlandPluginApi";
}

/// <summary>The result of staging one repository.</summary>
public abstract record StageOutcome
{
    private StageOutcome()
    {
    }

    public sealed record Built(StagedRepository Repository) : StageOutcome;

    public sealed record UpToDate(string Commit) : StageOutcome;

    public sealed record Failed(IReadOnlyList<string> Messages) : StageOutcome;
}

/// <summary>
/// Clones a repository and builds the plugins its manifest lists, as the current user,
/// in the staging folder. A plugin with another API version refuses the whole manifest before anything builds.
/// </summary>
public sealed class PluginStaging(PluginManagerPaths paths, IProcessRunner runner)
{
    public const string Git = "git";

    public const string Dotnet = "dotnet";

    public string FolderOf(string url) => Path.Combine(paths.Staging, RepositoryId(url));

    public StageOutcome Stage(string url, string? revision, string? installedCommit, bool force, TextWriter progress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(progress);
        var folder = FolderOf(url);
        var source = Path.Combine(folder, "source");
        var output = Path.Combine(folder, "out");
        try
        {
            DeleteTree(folder);
            Directory.CreateDirectory(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail($"Could not prepare the staging folder '{folder}': {exception.Message}");
        }

        progress.WriteLine($"Cloning {url}");
        var clone = runner.Run(Git, ["clone", "--recursive", "--", url, source], folder);
        if (clone.ExitCode != 0 || !Directory.Exists(Path.Combine(source, ".git")))
            return Fail($"Could not clone '{url}'.", clone.Output);

        if (!string.IsNullOrEmpty(revision))
        {
            var reset = runner.Run(Git, ["-C", source, "reset", "--hard", "--recurse-submodules", revision], folder);
            if (reset.ExitCode != 0)
                return Fail($"Could not check out revision '{revision}'.", reset.Output);

            runner.Run(Git, ["-C", source, "submodule", "update", "--init", "--recursive"], folder);
        }

        var head = runner.Run(Git, ["-C", source, "rev-parse", "HEAD"], folder);
        var commit = head.Output.Trim();
        if (head.ExitCode != 0 || commit.Length == 0)
            return Fail("Could not read the commit hash.", head.Output);

        if (!force && installedCommit is not null && commit.Equals(installedCommit, StringComparison.OrdinalIgnoreCase))
            return new StageOutcome.UpToDate(commit);

        var manifestPath = Path.Combine(source, PluginManifest.FileName);
        if (!File.Exists(manifestPath))
            return Fail($"The repository has no {PluginManifest.FileName}.");

        PluginManifest manifest;
        try
        {
            manifest = PluginManifest.Parse(File.ReadAllText(manifestPath));
        }
        catch (ManifestException exception)
        {
            return Fail($"{PluginManifest.FileName} is not valid: {exception.Message}");
        }

        var incompatible = manifest.Incompatible(PluginApiVersion.Current);
        if (incompatible.Count > 0)
            return new StageOutcome.Failed([.. incompatible, "Nothing was built."]);

        var plugins = new List<StagedPlugin>();
        foreach (var plugin in manifest.Plugins)
        {
            progress.WriteLine($"Building {plugin.Name}");
            var target = Path.Combine(output, plugin.Name);
            var project = Path.GetFullPath(Path.Combine(source, plugin.Project));
            if (!PluginNames.IsInside(source, project) || !File.Exists(project))
                return Fail($"Plugin '{plugin.Name}' names project '{plugin.Project}', which is not in the repository.");

            var build = runner.Run(
                Dotnet,
                ["publish", project, "--configuration", "Release", "--output", target, "--nologo", $"-p:{PluginManagerPaths.ApiProperty}={paths.ApiAssembly}"],
                source);
            var assembly = Path.Combine(target, plugin.Assembly);
            if (build.ExitCode != 0 || !File.Exists(assembly))
                return Fail($"Plugin '{plugin.Name}' failed to build.", build.Output);

            plugins.Add(new StagedPlugin(plugin.Name, PluginHash.OfFile(assembly), assembly));
        }

        return new StageOutcome.Built(new StagedRepository(url, commit, plugins));
    }

    public void Clean(string url)
    {
        try
        {
            DeleteTree(FolderOf(url));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _ = exception;
        }
    }

    /// <summary>A folder name for a repository: its last path segment and a short hash of the whole URL.</summary>
    public static string RepositoryId(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var trimmed = url.Trim().TrimEnd('/', '\\');
        var last = trimmed[(trimmed.LastIndexOfAny(['/', '\\', ':']) + 1)..];
        if (last.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            last = last[..^4];

        var name = new string(last.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.').ToArray()).Trim('.');
        if (name.Length == 0)
            name = "repository";

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed.ToLowerInvariant())))[..8].ToLowerInvariant();
        return name + "-" + hash;
    }

    // Git marks its object files read-only, which Directory.Delete refuses.
    private static void DeleteTree(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        Directory.Delete(folder, recursive: true);
    }

    private static StageOutcome.Failed Fail(string message, string? output = null)
    {
        if (string.IsNullOrWhiteSpace(output))
            return new StageOutcome.Failed([message]);

        return new StageOutcome.Failed([message, output.TrimEnd()]);
    }
}
