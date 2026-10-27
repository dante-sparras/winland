using System.Text;

namespace Winland.Application.Tests;

public class PipeFrameTests
{
    [Theory]
    [InlineData("ok")]
    [InlineData("")]
    [InlineData("first\nsecond\n\nfourth")]
    [InlineData("#42")]
    public void AFramedReplyReadsBackAsItWas(string reply)
    {
        using var stream = new MemoryStream(PipeFrame.Frame(reply));

        Assert.Equal(reply, PipeFrame.ReadReply(stream));
    }

    [Fact]
    public void AReplyWithANewlineIsSentWithItsLength()
    {
        var framed = Encoding.UTF8.GetString(PipeFrame.Frame("a\nb"));

        Assert.Equal("#3\na\nb", framed);
    }

    [Fact]
    public async Task ALineDropsItsCarriageReturn()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("clients\r\nnext\n"));

        Assert.Equal("clients", await PipeFrame.ReadLineAsync(stream, PipeFrame.RequestLimit));
        Assert.Equal("next", await PipeFrame.ReadLineAsync(stream, PipeFrame.RequestLimit));
        Assert.Null(await PipeFrame.ReadLineAsync(stream, PipeFrame.RequestLimit));
    }

    [Fact]
    public async Task ALineOverTheLimitIsRefused()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 11) + "\n"));

        await Assert.ThrowsAsync<InvalidDataException>(() => PipeFrame.ReadLineAsync(stream, 10));
    }
}
