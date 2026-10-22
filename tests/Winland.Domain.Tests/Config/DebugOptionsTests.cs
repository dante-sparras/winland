namespace Winland.Domain.Tests;

public class DebugOptionsTests
{
    [Fact]
    public void TheDefaultsAreThePinnedReleases()
    {
        var options = DebugOptions.From(ConfigParser.Parse(""));

        Assert.True(options.DisableLogs);
        Assert.True(options.DisableTime);
        Assert.False(options.EnableStdoutLogs);
        Assert.True(options.ColoredStdoutLogs);
    }

    [Fact]
    public void TheLogOptionsAreReadWithoutAWarningAndTheOldBarOptionsWarn()
    {
        var document = ConfigParser.Parse("""
            debug:disable_logs = false
            debug:disable_time = false
            debug:enable_stdout_logs = true
            debug:colored_stdout_logs = false
            debug:suppress_errors = true
            debug:error_limit = 40
            debug:error_position = bottom
            debug:overlay = true
            """);
        var options = DebugOptions.From(document);

        Assert.False(options.DisableLogs);
        Assert.False(options.DisableTime);
        Assert.True(options.EnableStdoutLogs);
        Assert.False(options.ColoredStdoutLogs);
        Assert.Contains(document.Warnings, warning => warning.Message == "Unsupported keyword 'debug:suppress_errors'.");
        Assert.Contains(document.Warnings, warning => warning.Message == "Unsupported keyword 'debug:error_limit'.");
        Assert.Contains(document.Warnings, warning => warning.Message == "Unsupported keyword 'debug:error_position'.");
        Assert.Contains(document.Warnings, warning => warning.Message == "Unsupported keyword 'debug:overlay'.");
    }
}
