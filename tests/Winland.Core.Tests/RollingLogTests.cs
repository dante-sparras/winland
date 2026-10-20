using System.Text;
using Winland;

namespace Winland.Tests;

// The log is one for the process. Only this class writes it, and its tests run one at a time.
public class RollingLogTests
{
    [Fact]
    public void TheLogKeepsItsTailAndTheWriterPassesEverythingOn()
    {
        RollingLog.Clear();
        var inner = new StringWriter();
        using var writer = new RollingLogWriter(inner);

        writer.WriteLine("first");
        writer.Write('x');
        writer.Write(new string('y', RollingLog.Size));

        Assert.StartsWith("first", inner.ToString(), StringComparison.Ordinal);
        Assert.Equal(RollingLog.Size, RollingLog.Read().Length);
        Assert.Equal(new string('y', RollingLog.Size), RollingLog.Read());
        RollingLog.Clear();
    }

    [Fact]
    public void TheRequestAnswersWithTheLogPlainOrInThePinnedReleasesJsonForm()
    {
        Assert.Equal("a \"b\"\n", ExtraCommands.FormatRollingLog("a \"b\"\n", json: false));
        Assert.Equal("[\n\"log\":\"a \\u0022b\\u0022\\n\"]", ExtraCommands.FormatRollingLog("a \"b\"\n", json: true));
    }

    [Fact]
    public void RollinglogAnswersWithTheLog()
    {
        RollingLog.Clear();
        RollingLog.Append("first\n");

        Assert.Equal("first\n", new Core().Execute("rollinglog").Text);
        RollingLog.Clear();
    }

    [Fact]
    public void TheWriterUsesItsInnerEncoding()
    {
        using var writer = new RollingLogWriter(new StreamWriter(new MemoryStream(), Encoding.UTF8));

        Assert.Equal(Encoding.UTF8.WebName, writer.Encoding.WebName);
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
}
