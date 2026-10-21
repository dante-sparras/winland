namespace Winland;

/// <summary>
/// The <c>debug</c> options Windows has a meaning for: where Winland's log goes. Any other <c>debug</c> keyword
/// warns and is skipped.
/// </summary>
public sealed record DebugOptions
{
    internal static IReadOnlySet<string> Keywords { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "debug:disable_logs",
        "debug:disable_time",
        "debug:enable_stdout_logs",
        "debug:colored_stdout_logs",
    };

    public static DebugOptions Documented { get; } = new();

    /// <summary><c>debug:disable_logs</c>: no log file and no log on standard output. Winland's error stream is still written.</summary>
    public bool DisableLogs { get; init; } = true;

    /// <summary><c>debug:disable_time</c>: log lines carry no time.</summary>
    public bool DisableTime { get; init; } = true;

    /// <summary><c>debug:enable_stdout_logs</c>: the log is also written to standard output.</summary>
    public bool EnableStdoutLogs { get; init; }

    /// <summary><c>debug:colored_stdout_logs</c>: the log on standard output is colored.</summary>
    public bool ColoredStdoutLogs { get; init; } = true;

    public static DebugOptions From(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = Documented;
        foreach (var setting in document.Settings)
        {
            var value = setting.Value.Trim();
            options = setting.Keyword switch
            {
                "debug:disable_logs" when ConfigValueSyntax.TryParseBool(value, out var off) => options with { DisableLogs = off },
                "debug:disable_time" when ConfigValueSyntax.TryParseBool(value, out var untimed) => options with { DisableTime = untimed },
                "debug:enable_stdout_logs" when ConfigValueSyntax.TryParseBool(value, out var stdout) => options with { EnableStdoutLogs = stdout },
                "debug:colored_stdout_logs" when ConfigValueSyntax.TryParseBool(value, out var colored) => options with { ColoredStdoutLogs = colored },
                _ => options,
            };
        }

        return options;
    }
}
