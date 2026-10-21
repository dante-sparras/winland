namespace Winland;

public sealed record Launch(string CommandLine, string? WorkingDirectory)
{
    public static IReadOnlyList<WindowRule> NoRules { get; } = [];

    public bool Raw { get; init; }

    public IReadOnlyList<WindowRule> Rules { get; init; } = NoRules;

    /// <summary>
    /// The Workspace this program was started on, which its window opens on under <c>misc:initial_workspace_tracking</c>.
    /// Null for <c>exec-once</c> and <c>exec-shutdown</c>, which Winland starts without a token.
    /// </summary>
    public WorkspaceId? InitialWorkspace { get; init; }
}

internal readonly record struct LaunchParse(Launch? Launch, IReadOnlyList<string> Warnings);

internal static class LaunchParser
{
    internal static LaunchParse Parse(string arguments, bool raw)
    {
        var text = arguments.Trim();
        var warnings = new List<string>();
        IReadOnlyList<WindowRule> rules = Launch.NoRules;
        if (text.StartsWith('['))
        {
            var end = text.IndexOf(']');
            if (end < 0)
                return new LaunchParse(null, ["Unclosed rule block."]);

            if (!TryParseRules(text[1..end], out rules, warnings))
            {
                text = text[(end + 1)..].TrimStart();
                return new LaunchParse(null, warnings);
            }

            text = text[(end + 1)..].TrimStart();
        }

        if (text.Length == 0)
            return new LaunchParse(null, warnings);

        var launch = new Launch(text, null)
        {
            Raw = raw,
            Rules = rules.Count == 0 ? Launch.NoRules : rules,
        };
        return new LaunchParse(launch, warnings);
    }

    internal static IReadOnlyList<Launch> FromConfig(ConfigDocument config, LaunchMoment moment, WorkspaceId? initialWorkspace = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var launches = new List<Launch>();
        foreach (var setting in config.Settings)
        {
            if (!TryClassify(setting.Keyword, out var once, out var raw, out var shutdown))
                continue;

            var include = moment switch
            {
                LaunchMoment.Startup => !shutdown,
                LaunchMoment.Reload => !once && !shutdown,
                LaunchMoment.Shutdown => shutdown,
                _ => false,
            };
            if (!include)
                continue;

            var parsed = Parse(setting.Value, raw);
            if (parsed.Launch is not { } launch)
                continue;

            if (!once && !shutdown)
                launch = launch with { InitialWorkspace = initialWorkspace };
            launches.Add(launch);
        }

        return launches;
    }

    private static bool TryParseRules(string body, out IReadOnlyList<WindowRule> rules, List<string> warnings)
    {
        rules = Launch.NoRules;
        var parsed = new List<WindowRule>();
        var fatal = false;
        foreach (var piece in body.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (WindowRuleParser.TryParseBare(piece, out var rule, out var error) && rule is not null)
            {
                parsed.Add(rule);
                continue;
            }

            warnings.Add(error);
            if (!error.StartsWith("Unsupported window rule '", StringComparison.Ordinal))
                fatal = true;
        }

        if (parsed.Count > 0)
            rules = parsed;
        return !fatal;
    }

    private static bool TryClassify(string keyword, out bool once, out bool raw, out bool shutdown)
    {
        switch (keyword)
        {
            case "exec":
                once = false;
                raw = false;
                shutdown = false;
                return true;
            case "exec-once":
                once = true;
                raw = false;
                shutdown = false;
                return true;
            case "execr":
                once = false;
                raw = true;
                shutdown = false;
                return true;
            case "execr-once":
                once = true;
                raw = true;
                shutdown = false;
                return true;
            case "exec-shutdown":
                once = false;
                raw = false;
                shutdown = true;
                return true;
            default:
                once = false;
                raw = false;
                shutdown = false;
                return false;
        }
    }

    internal enum LaunchMoment
    {
        Startup,
        Reload,
        Shutdown,
    }
}
