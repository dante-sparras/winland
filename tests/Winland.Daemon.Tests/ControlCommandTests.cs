namespace Winland.Daemon.Tests;

[CollectionDefinition("Control output", DisableParallelization = true)]
public class ControlOutputCollection;

[Collection("Control output")]
public class ControlCommandTests
{
    [Theory]
    [InlineData("[[BATCH]]dispatch bogus;j/clients", "error: unknown dispatcher\n\n\n[]", 1)]
    [InlineData("j/[[BATCH]]clients;dispatch bogus", "[]\n\n\nerror: unknown dispatcher", 1)]
    [InlineData("[[BATCH]]clients;dispatch bogus", "Window 2a -> title:\n\ttitle: title\n\n\n\n\nerror: unknown dispatcher", 1)]
    [InlineData("[[BATCH]]events", "", 0)]
    [InlineData("[[BATCH]]events;events", "\n\n\n", 0)]
    [InlineData("[[BATCH]]j/clients", "[]", 0)]
    public void ABatchWritesEveryReplyToStandardOutput(string line, string reply, int expectedExit)
    {
        var result = WriteReply(line, reply);

        Assert.Equal(expectedExit, result.Exit);
        Assert.Equal(reply + Environment.NewLine, result.Output);
        Assert.Equal("", result.Error);
    }

    [Theory]
    [InlineData("error: no such option")]
    [InlineData("error: prop not found")]
    [InlineData("error: sendshortcut: invalid args")]
    [InlineData("")]
    public void ASingleFailureWritesToStandardError(string reply)
    {
        var result = WriteReply("getoption missing", reply);

        Assert.Equal(1, result.Exit);
        Assert.Equal("", result.Output);
        Assert.Equal((reply.Length == 0 ? "error: no reply" : reply) + Environment.NewLine, result.Error);
    }

    private static (int Exit, string Output, string Error) WriteReply(string line, string reply)
    {
        var previousOut = Console.Out;
        var previousError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var exit = ControlCommand.WriteReply(line, reply);
            return (exit, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }
}
