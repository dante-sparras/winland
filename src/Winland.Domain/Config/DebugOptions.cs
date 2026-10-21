namespace Winland.Domain;

/// <summary>
/// The <c>debug</c> options Windows has a meaning for: where Winland's log goes. Any other <c>debug</c> keyword
/// warns and is skipped.
/// </summary>
public sealed record DebugOptions
{
    public static DebugOptions Documented { get; } = new();

    /// <summary><c>debug:disable_logs</c>: no log file and no log on standard output. Winland's error stream is still written.</summary>
    public bool DisableLogs { get; init; } = true;

    /// <summary><c>debug:disable_time</c>: log lines carry no time.</summary>
    public bool DisableTime { get; init; } = true;

    /// <summary><c>debug:enable_stdout_logs</c>: the log is also written to standard output.</summary>
    public bool EnableStdoutLogs { get; init; }

    /// <summary><c>debug:colored_stdout_logs</c>: the log on standard output is colored.</summary>
    public bool ColoredStdoutLogs { get; init; } = true;

    internal static OptionSet<DebugOptions> Options { get; } = new(
    [
        Of("debug:disable_logs", OptionValue.Bool, o => o.DisableLogs, (o, v) => o with { DisableLogs = v }),
        Of("debug:disable_time", OptionValue.Bool, o => o.DisableTime, (o, v) => o with { DisableTime = v }),
        Of("debug:enable_stdout_logs", OptionValue.Bool, o => o.EnableStdoutLogs, (o, v) => o with { EnableStdoutLogs = v }),
        Of("debug:colored_stdout_logs", OptionValue.Bool, o => o.ColoredStdoutLogs, (o, v) => o with { ColoredStdoutLogs = v }),
    ]);

    public static DebugOptions From(ConfigDocument document) => Options.From(Documented, document);

    private static Option<DebugOptions> Of<T>(string name, OptionValue<T> value, Func<DebugOptions, T> get, Func<DebugOptions, T, DebugOptions> set) =>
        Option<DebugOptions>.Of(name, value, get, set);
}
