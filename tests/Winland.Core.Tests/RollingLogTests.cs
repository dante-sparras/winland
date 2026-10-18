using System.Text;
using Winland;

namespace Winland.Tests;

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
    public void TheWriterUsesItsInnerEncoding()
    {
        using var writer = new RollingLogWriter(new StreamWriter(new MemoryStream(), Encoding.UTF8));

        Assert.Equal(Encoding.UTF8.WebName, writer.Encoding.WebName);
    }
}
