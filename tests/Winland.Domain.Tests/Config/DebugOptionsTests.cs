namespace Winland.Domain.Tests;

public class DebugOptionsTests
{
    [Fact]
    public void LogsAreOffByDefault()
    {
        Assert.True(DebugOptions.From(ConfigParser.Parse("")).DisableLogs);
    }

    [Fact]
    public void DisableLogsIsReadWithoutAWarningAndTheOtherDebugOptionsWarn()
    {
        var document = ConfigParser.Parse("""
            debug:disable_logs = false
            debug:disable_time = false
            debug:enable_stdout_logs = true
            debug:colored_stdout_logs = false
            debug:overlay = true
            """);
        var options = DebugOptions.From(document);

        Assert.False(options.DisableLogs);
        Assert.Contains(document.Warnings, warning => warning.Message == "Unsupported keyword 'debug:disable_time'.");
        Assert.Contains(document.Warnings, warning => warning.Message == "Unsupported keyword 'debug:enable_stdout_logs'.");
        Assert.Contains(document.Warnings, warning => warning.Message == "Unsupported keyword 'debug:colored_stdout_logs'.");
        Assert.Contains(document.Warnings, warning => warning.Message == "Unsupported keyword 'debug:overlay'.");
    }
}
