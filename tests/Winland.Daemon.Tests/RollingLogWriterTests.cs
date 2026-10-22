using System.Text;

namespace Winland.Daemon.Tests;

public class RollingLogWriterTests
{
    [Fact]
    public void TheWriterPassesEverythingOn()
    {
        var inner = new StringWriter();
        using var writer = new RollingLogWriter(inner);

        writer.WriteLine("first");
        writer.Write('x');
        writer.Write("yy");

        Assert.Equal("first" + Environment.NewLine + "xyy", inner.ToString());
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
    }

    [Fact]
    public void AColoredStandardOutputDimsTheTime()
    {
        var standardOutput = new StringWriter();
        using var writer = new RollingLogWriter(new StringWriter(new StringBuilder()), () => new DateTime(2026, 1, 1, 1, 2, 3));

        writer.Copy(new DebugOptions { DisableLogs = false, DisableTime = false, EnableStdoutLogs = true }, () => null, standardOutput);
        writer.Write("hi\n");

        Assert.Equal("\u001b[90m[01:02:03.000] \u001b[0mhi\n", standardOutput.ToString());
    }
}
