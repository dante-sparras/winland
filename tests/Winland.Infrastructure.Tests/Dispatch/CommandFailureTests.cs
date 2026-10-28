using System.IO.Pipes;

namespace Winland.Infrastructure.Tests;

[CollectionDefinition("Command failures", DisableParallelization = true)]
public class CommandFailureCollection;

[Collection("Command failures")]
public class CommandFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCommandsLogTheException(bool afterCommand)
    {
        var name = "winland-test-" + Guid.NewGuid().ToString("N");
        using var error = new StringWriter();
        var previous = Console.Error;
        Console.SetError(error);
        try
        {
            using var server = new CommandServer(new WindowManager(), _ =>
            {
                if (!afterCommand)
                    throw new InvalidOperationException("apply failed");
            }, name)
            {
                AfterCommand = () => throw new InvalidOperationException("after command failed"),
            };
            server.Start();

            var failure = Record.Exception(() => CommandClient.Transact("dispatch workspace 2", name));
            Assert.True(failure is null or IOException, failure?.ToString());
            Assert.Contains(afterCommand ? "after command failed" : "apply failed", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(previous);
        }
    }

    [Fact]
    public void AProcessIdProbeDoesNotApplyACommand()
    {
        var name = "winland-test-" + Guid.NewGuid().ToString("N");
        var applied = 0;
        var followed = 0;
        using var server = new CommandServer(new WindowManager(), _ => Interlocked.Increment(ref applied), name)
        {
            AfterCommand = () => Interlocked.Increment(ref followed),
        };
        server.Start();

        Assert.Equal(Environment.ProcessId, PipeServer.ProcessId(name));
        Assert.False(SpinWait.SpinUntil(() => Volatile.Read(ref followed) != 0, TimeSpan.FromMilliseconds(500)));
        Assert.Equal(0, Volatile.Read(ref applied));
        Assert.Equal("ok", CommandClient.Transact("dispatch workspace 2", name));
        Assert.Equal(1, Volatile.Read(ref applied));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#10\nabc")]
    public async Task AConnectedServerThatStopsAnsweringHasAReplyDeadline(string partialReply)
    {
        var name = "winland-test-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var connected = server.WaitForConnectionAsync();
        var request = Task.Run(() => CommandClient.Transact("clients", name, replyTimeoutMs: 200));
        await connected.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("clients", await PipeFrame.ReadLineAsync(server, PipeFrame.RequestLimit));
        await server.WriteAsync(System.Text.Encoding.UTF8.GetBytes(partialReply));

        var finished = await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(5)));
        if (finished != request)
            server.Dispose();

        Assert.Same(request, finished);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => request);
        Assert.Equal("Winland is not answering.", error.Message);
    }
}
