namespace Winland;

public sealed record EnvironmentAssignment(string Name, string Value)
{
    public static IReadOnlyList<EnvironmentAssignment> None { get; } = [];
}

public sealed record Launch(string CommandLine, string? WorkingDirectory, bool Elevated)
{
    public static IReadOnlyList<WindowRule> NoRules { get; } = [];

    public bool Raw { get; init; }

    public IReadOnlyList<EnvironmentAssignment> Environment { get; init; } = EnvironmentAssignment.None;

    public IReadOnlyList<WindowRule> Rules { get; init; } = NoRules;
}

internal readonly record struct LaunchParse(Launch? Launch, IReadOnlyList<string> Warnings);

internal static class EnvironmentParser
{
    internal static bool TryParse(string value, out EnvironmentAssignment? assignment, out string error)
    {
        assignment = null;
        var comma = value.IndexOf(',');
        var name = (comma < 0 ? value : value[..comma]).Trim();
        var assigned = comma < 0 ? "" : value[(comma + 1)..].Trim();
        if (name.Length == 0)
        {
            error = "Environment variable name is empty.";
            return false;
        }

        if (name.Contains('=') || name.Contains('\0') || assigned.Contains('\0'))
        {
            error = "Environment variable name is invalid.";
            return false;
        }

        assignment = new EnvironmentAssignment(name, assigned);
        error = "";
        return true;
    }
}

internal static class LaunchParser
{
    private const string ElevatedPrefix = "[elevated]";

    internal static LaunchParse Parse(string arguments, bool allowsElevation, bool raw)
    {
        var text = arguments.Trim();
        var elevated = TryTakeElevated(ref text);
        var warnings = new List<string>();
        IReadOnlyList<WindowRule> rules = Launch.NoRules;
        if (text.StartsWith('['))
        {
            var end = text.IndexOf(']');
            if (end < 0)
                return new LaunchParse(null, ["Unclosed rule block."]);

            if (!TryParseRules(text[1..end], ref elevated, out rules, warnings))
            {
                text = text[(end + 1)..].TrimStart();
                return new LaunchParse(null, warnings);
            }

            text = text[(end + 1)..].TrimStart();
        }

        if (text.Length == 0)
            return new LaunchParse(null, warnings);

        var launch = new Launch(text, null, elevated && allowsElevation)
        {
            Raw = raw,
            Rules = rules.Count == 0 ? Launch.NoRules : rules,
        };
        return new LaunchParse(launch, warnings);
    }

    internal static IReadOnlyList<Launch> FromConfig(ConfigDocument config, LaunchMoment moment)
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

            var parsed = Parse(setting.Value, allowsElevation: config.ElevationTrusted, raw);
            if (parsed.Launch is not { } launch)
                continue;

            if (config.Environment.Count > 0)
                launch = launch with { Environment = config.Environment };
            launches.Add(launch);
        }

        return launches;
    }

    internal static bool IsExec(string dispatcher) =>
        dispatcher.Equals("exec", StringComparison.OrdinalIgnoreCase)
        || dispatcher.Equals("execr", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseRules(string body, ref bool elevated, out IReadOnlyList<WindowRule> rules, List<string> warnings)
    {
        rules = Launch.NoRules;
        var parsed = new List<WindowRule>();
        var fatal = false;
        foreach (var piece in body.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (piece.Equals("elevated", StringComparison.OrdinalIgnoreCase))
            {
                elevated = true;
                continue;
            }

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

    private static bool TryTakeElevated(ref string text)
    {
        if (text.Length < ElevatedPrefix.Length || !text.StartsWith(ElevatedPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        if (text.Length > ElevatedPrefix.Length && !char.IsWhiteSpace(text[ElevatedPrefix.Length]))
            return false;

        text = text[ElevatedPrefix.Length..].TrimStart();
        return true;
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
