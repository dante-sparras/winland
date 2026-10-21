using Winland.Install;
using Winland.Core;

namespace Winland.Daemon;

/// <summary>Where the config is, and which files a change to it can come from.</summary>
static class ConfigPaths
{
    /// <summary>
    /// The config Winland reads when it is started without <c>--config</c>: <c>winland.conf</c> in
    /// <c>.winland</c> under the user profile.
    /// </summary>
    public static string Default(string userProfile) =>
        Path.Combine(InstallLocations.Config(userProfile), "winland.conf");

    /// <summary>
    /// The config and every source it names, as full paths. A relative source is relative to the
    /// config's folder, as the parser reads it. Path.Combine keeps a rooted source as it is.
    /// </summary>
    public static IReadOnlyList<string> Watched(string configPath, IEnumerable<string> sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentNullException.ThrowIfNull(sources);
        var full = Path.GetFullPath(configPath);
        var directory = Path.GetDirectoryName(full) ?? ".";
        var paths = new List<string> { full };
        foreach (var source in sources)
            paths.Add(Path.GetFullPath(Path.Combine(directory, source)));

        return paths;
    }
}
