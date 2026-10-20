using System.Text;
using Winland;

namespace Winland.Tests;

public class DebugOptionsTests
{
    private static readonly Rectangle Work = new(0, 0, 1920, 1080);

    [Fact]
    public void TheDefaultsAreThePinnedReleases()
    {
        var options = DebugOptions.From(ConfigParser.Parse(""));

        Assert.True(options.DisableLogs);
        Assert.True(options.DisableTime);
        Assert.False(options.EnableStdoutLogs);
        Assert.True(options.ColoredStdoutLogs);
        Assert.False(options.SuppressErrors);
        Assert.Equal(5, options.ErrorLimit);
        Assert.Equal(0, options.ErrorPosition);
    }

    [Fact]
    public void TheSevenOptionsAreReadWithoutAWarningAndTheRestOfDebugStaysAGap()
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
        Assert.True(options.SuppressErrors);
        Assert.Equal(20, options.ErrorLimit);
        Assert.Equal(1, options.ErrorPosition);
        Assert.Equal("Unsupported keyword 'debug:overlay'.", Assert.Single(document.Warnings).Message);
    }

    [Fact]
    public void AConfigWithErrorsShowsThemInTheBarButNotItsGapNotices()
    {
        var core = Configured("general:nonsense = 1\ninput:kb_rules = evdev\nmisc:bell_sound = ding\n");

        var bar = core.Execute("dispatch no_op").Effect.ErrorBar;

        Assert.NotNull(bar);
        Assert.Equal("Your config has errors:\nline 4: Unsupported keyword 'general:nonsense'.", bar.Text);
        Assert.Equal(new BorderColor(0xFF, 0xFF, 0x66, 0x66), bar.Color);
    }

    [Fact]
    public void OnlyGapNoticesOrSuppressErrorsShowNoBar()
    {
        Assert.Null(Configured("input:kb_rules = evdev\n").Execute("dispatch no_op").Effect.ErrorBar);
        Assert.Null(Configured("debug:suppress_errors = true\ngeneral:nonsense = 1\n").Execute("dispatch no_op").Effect.ErrorBar);
    }

    [Fact]
    public void AReloadWithoutErrorsTakesTheBarAwayASeterrorOneToo()
    {
        var core = Configured("");
        core.Execute("seterror rgb(ff0000) broke");

        core.Handle(new Configure(ConfigParser.Parse("general:gaps_in = 3\n")));

        Assert.Null(core.Execute("dispatch no_op").Effect.ErrorBar);
    }

    [Fact]
    public void TheLimitCutsTheLinesAndTheBarCanSitAtTheBottom()
    {
        var core = Configured("debug:error_limit = 2\ndebug:error_position = 1\ngeneral:one = 1\ngeneral:two = 2\n");
        core.Handle(new WindowAppeared(new WindowId(1), Work));

        var outcome = core.Execute("dispatch no_op").Effect;
        var bar = outcome.ErrorBar!;
        var tile = Assert.Single(outcome.State.Clients).Bounds;

        Assert.Equal("Your config has errors:\nline 6: Unsupported keyword 'general:one'.\n(1 more...)", bar.Text);
        Assert.True(bar.Bounds.Y + bar.Bounds.Height <= Work.Y + Work.Height);
        Assert.Equal(0, tile.Y);
        Assert.True(tile.Y + tile.Height <= bar.Bounds.Y);
    }

    [Fact]
    public void TheLogIsCopiedWithTheTimeToAFileAndStandardOutput()
    {
        RollingLog.Clear();
        var file = new StringWriter();
        var standardOutput = new StringWriter();
        using var writer = new RollingLogWriter(new StringWriter(), () => new DateTime(2026, 1, 1, 13, 4, 5, 6));

        writer.Copy(new DebugOptions { DisableLogs = false, DisableTime = false, EnableStdoutLogs = true, ColoredStdoutLogs = false }, () => file, standardOutput);
        writer.Write("one ");
        writer.WriteLine("line");
        writer.Write("two\nthree\n");

        var expected = "[13:04:05.006] one line" + Environment.NewLine + "[13:04:05.006] two\n[13:04:05.006] three\n";
        Assert.Equal(expected, file.ToString());
        Assert.Equal(expected, standardOutput.ToString());

        writer.Copy(DebugOptions.Documented, () => throw new InvalidOperationException("not wanted"), standardOutput);
        writer.WriteLine("quiet");
        Assert.DoesNotContain("quiet", standardOutput.ToString(), StringComparison.Ordinal);
        RollingLog.Clear();
    }

    [Fact]
    public void AColoredStandardOutputDimsTheTime()
    {
        RollingLog.Clear();
        var standardOutput = new StringWriter();
        using var writer = new RollingLogWriter(new StringWriter(new StringBuilder()), () => new DateTime(2026, 1, 1, 1, 2, 3));

        writer.Copy(new DebugOptions { DisableLogs = false, DisableTime = false, EnableStdoutLogs = true }, () => null, standardOutput);
        writer.Write("hi\n");

        Assert.Equal("\u001b[90m[01:02:03.000] \u001b[0mhi\n", standardOutput.ToString());
        RollingLog.Clear();
    }

    private static Core Configured(string config)
    {
        var core = new Core();
        core.Handle(new MonitorsChanged([new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, Work)]));
        core.Handle(new Configure(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + config)));
        return core;
    }
}
