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
    public void TheLogIsCopiedToAFileWhenLogsAreEnabled()
    {
        var file = new StringWriter();
        using var writer = new RollingLogWriter(new StringWriter());

        writer.Copy(new DebugOptions { DisableLogs = false }, () => file);
        writer.Write("one ");
        writer.WriteLine("line");
        writer.Write("two\nthree\n");

        Assert.Equal("one line" + Environment.NewLine + "two\nthree\n", file.ToString());

        writer.Copy(DebugOptions.Documented, () => throw new InvalidOperationException("not wanted"));
        writer.WriteLine("quiet");
        Assert.DoesNotContain("quiet", file.ToString(), StringComparison.Ordinal);
    }
}
