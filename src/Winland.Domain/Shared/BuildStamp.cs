using System.Reflection;

namespace Winland.Domain;

public static class BuildStamp
{
    static BuildStamp()
    {
        var assembly = typeof(BuildStamp).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var commit = "";
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            if (plus >= 0)
            {
                version = informational[..plus];
                commit = informational[(plus + 1)..];
            }
            else
            {
                version = informational;
            }
        }

        const string dirty = ".dirty";
        if (commit.EndsWith(dirty, StringComparison.Ordinal))
        {
            Dirty = true;
            commit = commit[..^dirty.Length];
        }

        Version = version;
        Commit = commit;
    }

    public static string Version { get; }

    public static string Commit { get; }

    public static bool Dirty { get; }
}
