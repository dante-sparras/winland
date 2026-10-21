using System.Globalization;

namespace Winland;

/// <summary>
/// The <c>debug</c> options Windows has a meaning for: the config error bar and Winland's log. Any other
/// <c>debug</c> keyword warns and is skipped.
/// </summary>
public sealed record DebugOptions
{
    internal static IReadOnlySet<string> Keywords { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "debug:disable_logs",
        "debug:disable_time",
        "debug:enable_stdout_logs",
        "debug:colored_stdout_logs",
        "debug:suppress_errors",
        "debug:error_limit",
        "debug:error_position",
    };

    private static readonly Dictionary<string, int> Positions = new(StringComparer.Ordinal) { ["top"] = 0, ["bottom"] = 1 };

    public static DebugOptions Documented { get; } = new();

    /// <summary><c>debug:disable_logs</c>: no log file and no log on standard output. Winland's error stream is still written.</summary>
    public bool DisableLogs { get; init; } = true;

    /// <summary><c>debug:disable_time</c>: log lines carry no time.</summary>
    public bool DisableTime { get; init; } = true;

    /// <summary><c>debug:enable_stdout_logs</c>: the log is also written to standard output.</summary>
    public bool EnableStdoutLogs { get; init; }

    /// <summary><c>debug:colored_stdout_logs</c>: the log on standard output is colored.</summary>
    public bool ColoredStdoutLogs { get; init; } = true;

    /// <summary><c>debug:suppress_errors</c>: config errors are not shown in the error bar.</summary>
    public bool SuppressErrors { get; init; }

    /// <summary><c>debug:error_limit</c>: the lines of the error bar shown, 0 to 20.</summary>
    public int ErrorLimit { get; init; } = 5;

    /// <summary><c>debug:error_position</c>: 0 puts the error bar at the top of the Monitor, 1 at the bottom.</summary>
    public int ErrorPosition { get; init; }

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
                "debug:suppress_errors" when ConfigValueSyntax.TryParseBool(value, out var suppress) => options with { SuppressErrors = suppress },
                "debug:error_limit" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit) =>
                    options with { ErrorLimit = Math.Clamp(limit, 0, 20) },
                "debug:error_position" when ConfigValueSyntax.TryParseNamedInt(value, Positions, out var position) =>
                    options with { ErrorPosition = Math.Clamp(position, 0, 1) },
                _ => options,
            };
        }

        return options;
    }
}
