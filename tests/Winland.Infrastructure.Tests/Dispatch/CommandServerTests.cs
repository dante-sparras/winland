using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Winland.Infrastructure.Tests;

[Collection(CommandServerCollection.Name)]
public class CommandServerTests
{
    [Fact]
    public void MissingDaemonReportsThatWinlandIsNotRunning()
    {
        var missing = "winland-missing-" + Guid.NewGuid().ToString("N");

        var error = Assert.Throws<InvalidOperationException>(() => CommandClient.Transact("clients", missing, 200));

        Assert.Equal("Winland is not running.", error.Message);
    }

    [Fact]
    public void PipeNamesOnlyTheGivenUserAndAllowsMediumIntegrity()
    {
        var user = new SecurityIdentifier("S-1-5-21-1-2-3-1001");

        var security = CommandServer.SecurityFor(user);

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        var rule = Assert.Single(rules.Cast<PipeAccessRule>());
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.Equal(user.Value, rule.IdentityReference.Value);
        Assert.True(rule.PipeAccessRights.HasFlag(PipeAccessRights.ReadData));
        Assert.True(rule.PipeAccessRights.HasFlag(PipeAccessRights.WriteData));

        var descriptor = CommandServer.DescriptorFor(user);
        var aces = descriptor.DiscretionaryAcl!.Cast<CommonAce>().ToList();
        var userAce = Assert.Single(aces, ace => ace.SecurityIdentifier == user);
        Assert.Equal(0, userAce.AccessMask & (int)PipeAccessRights.CreateNewInstance);
        var admins = Assert.Single(aces, ace => ace.SecurityIdentifier.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
        Assert.NotEqual(0, admins.AccessMask & (int)PipeAccessRights.CreateNewInstance);
        Assert.NotNull(descriptor.SystemAcl);
        var label = Assert.IsType<CustomAce>(Assert.Single(descriptor.SystemAcl.Cast<GenericAce>()));
        var opaque = label.GetOpaque();
        Assert.NotNull(opaque);
        Assert.Equal(new byte[] { 1, 0, 0, 0 }, opaque[..4]);
        Assert.Equal(new byte[] { 1, 1, 0, 0, 0, 0, 0, 0x10, 0, 0x20, 0, 0 }, opaque[^12..]);
    }

    // A process that took the name first would otherwise share it, and get some of the clients.
    [Fact]
    public void StartFailsWhenAnotherProcessHoldsThePipeName()
    {
        var name = PipeName();
        using var squatter = new NamedPipeServerStream(
            name,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        using var server = new CommandServer(new WindowManager(), _ => { }, name);

        var error = Assert.Throws<InvalidOperationException>(server.Start);

        Assert.Contains("already in use", error.InnerException?.Message ?? error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondServerCannotTakeAPipeNameInUse()
    {
        var name = PipeName();
        using var first = new CommandServer(new WindowManager(), _ => { }, name);
        first.Start();
        using var second = new CommandServer(new WindowManager(), _ => { }, name);

        Assert.Throws<InvalidOperationException>(second.Start);
        Assert.Equal("ok", CommandClient.Transact("dispatch workspace 1", name));
    }

    [Fact]
    public void ClientRoundTripsClientsAndDispatch()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(42), new Rectangle(0, 0, 10, 10)));
        var name = "winland-" + Guid.NewGuid().ToString("N");
        using var server = new CommandServer(core, _ => { }, name);
        server.Start();

        var clients = JsonDocument.Parse(CommandClient.Transact("j/clients", name)).RootElement;
        var client = Assert.Single(clients.EnumerateArray());
        Assert.Equal("0x2a", client.GetProperty("address").GetString());
        Assert.Equal("ok", CommandClient.Transact("dispatch exec", name));
        Assert.Equal("error: unknown request", CommandClient.Transact("nope", name));
    }

    [Fact]
    public void SubscribersReceiveEachEventInOrder()
    {
        var core = new WindowManager();
        var name = PipeName();
        using var server = new CommandServer(core, _ => { }, name);
        server.Start();
        using var first = new Listener(name);
        using var second = new Listener(name);
        WaitUntilSubscribed(core, first);
        WaitUntilSubscribed(core, second);
        Sync(core, first, second);

        var area = new Rectangle(0, 0, 200, 100);
        core.Handle(new WindowAppeared(new WindowId(7), area)
        {
            Traits = WindowTraits.Normal with { Class = "app", Title = "title" },
        });
        Assert.Equal(
            ["openwindow>>7,1,,app,title", "activewindow>>app,title", "activewindowv2>>7"],
            Take(first, 3));
        Assert.Equal(
            ["openwindow>>7,1,,app,title", "activewindow>>app,title", "activewindowv2>>7"],
            Take(second, 3));

        Assert.Equal("ok", CommandClient.Transact("dispatch workspace 2", name));
        Assert.Equal(
            [
                "createworkspace>>2,",
                "createworkspacev2>>2,2,",
                "workspace>>2,",
                "workspacev2>>2,2,",
                "activewindow>>,",
                "activewindowv2>>",
            ],
            Take(first, 6));
        Assert.Equal(
            [
                "createworkspace>>2,",
                "createworkspacev2>>2,2,",
                "workspace>>2,",
                "workspacev2>>2,2,",
                "activewindow>>,",
                "activewindowv2>>",
            ],
            Take(second, 6));

        core.Handle(new WindowClosed(new WindowId(7)));
        Assert.Equal(["closewindow>>7"], Take(first, 1));
    }

    [Fact]
    public async Task ASlowSubscriberDoesNotBlockTheCore()
    {
        var core = new WindowManager();
        var name = PipeName();
        using var server = new CommandServer(core, _ => { }, name);
        server.Start();
        using var idle = Connect(name);
        Send(idle, "events");
        Assert.Equal("[]", CommandClient.Transact("j/clients", name));

        var finished = Task.Run(() =>
        {
            for (var i = 0; i < 600; i++)
                core.Execute(i % 2 == 0 ? "dispatch workspace 2" : "dispatch workspace 1");
        });

        var completed = await Task.WhenAny(finished, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(finished, completed);
        await finished;
        Assert.Equal("ok", CommandClient.Transact("dispatch workspace 1", name));
    }

    [Fact]
    public void ADeadSubscriberIsDroppedAndThePipeKeepsServing()
    {
        var core = new WindowManager();
        var name = PipeName();
        using var server = new CommandServer(core, _ => { }, name);
        server.Start();
        var idle = Connect(name);
        Send(idle, "events");
        Assert.Equal("[]", CommandClient.Transact("j/clients", name));
        idle.Dispose();

        using var listener = new Listener(name);
        WaitUntilSubscribed(core, listener);
        Sync(core, listener);
        core.Handle(new WindowAppeared(new WindowId(3), new Rectangle(0, 0, 10, 10)));
        Assert.Equal(["openwindow>>3,1,,,", "activewindow>>,", "activewindowv2>>3"], Take(listener, 3));
        Assert.Equal("ok", CommandClient.Transact("dispatch workspace 1", name));
    }

    // Every instance the name allows can be taken for a while. The pipe makes the next one once an
    // instance is free again, instead of going quiet for the rest of the session.
    [Fact]
    public void ThePipeServesAgainAfterEveryInstanceWasTaken()
    {
        var core = new WindowManager();
        var name = PipeName();
        using var server = new CommandServer(core, _ => { }, name) { MaxInstances = 2, RequestTimeout = TimeSpan.FromMilliseconds(300) };
        server.Start();
        using (Connect(name))
        using (Connect(name))
        {
            Thread.Sleep(100);
        }

        Assert.Equal("[]", CommandClient.Transact("j/clients", name, timeoutMs: 5000));
    }

    [Fact]
    public void AClientThatSendsNothingDoesNotStallTheNext()
    {
        var core = new WindowManager();
        var name = PipeName();
        using var server = new CommandServer(core, _ => { }, name);
        server.Start();
        using var silent = Connect(name);

        Assert.Equal("[]", CommandClient.Transact("j/clients", name));
        Assert.Equal("ok", CommandClient.Transact("dispatch workspace 1", name));
    }

    [Fact]
    public async Task AClientThatSendsNothingIsDroppedAfterTheRequestTimeout()
    {
        var core = new WindowManager();
        var name = PipeName();
        using var server = new CommandServer(core, _ => { }, name) { RequestTimeout = TimeSpan.FromMilliseconds(200) };
        server.Start();
        using var silent = Connect(name);

        var one = new byte[1];
        var read = silent.ReadAsync(one).AsTask();
        var completed = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(read, completed);
        Assert.Equal(0, await read);
    }

    // Status bars subscribe and then sit idle. A blocked thread each would starve the pool that
    // runs the accept loop and the bind timers; the pool only keeps up by growing a thread at a time.
    [Fact]
    public void IdleSubscribersHoldNoPoolThreads()
    {
        var core = new WindowManager();
        var name = PipeName();
        using var server = new CommandServer(core, _ => { }, name);
        server.Start();
        ThreadPool.GetMinThreads(out var workers, out _);
        var count = Math.Clamp(workers * 3, 48, 200);
        var before = ThreadPool.ThreadCount;
        var subscribers = new List<NamedPipeClientStream>();
        try
        {
            for (var i = 0; i < count; i++)
            {
                var pipe = Connect(name);
                subscribers.Add(pipe);
                Send(pipe, "events");
            }

            using var listener = new Listener(name);
            WaitUntilSubscribed(core, listener);
            var grown = ThreadPool.ThreadCount - before;
            Assert.True(grown < count / 2, $"The pool grew by {grown} threads for {count} idle subscribers.");
        }
        finally
        {
            foreach (var pipe in subscribers)
                pipe.Dispose();
        }
    }

    [Fact]
    public void BatchTravelsThePipe()
    {
        var core = new WindowManager();
        var name = PipeName();
        using var server = new CommandServer(core, _ => { }, name);
        server.Start();

        var batch = CommandClient.Transact("j/[[BATCH]]binds;configerrors", name);
        Assert.Equal("[]\n\n\n[]", batch);
    }

    private static string PipeName() => "winland-" + Guid.NewGuid().ToString("N");

    private static NamedPipeClientStream Connect(string name)
    {
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        pipe.Connect(2000);
        return pipe;
    }

    private static void Send(Stream pipe, string command)
    {
        var bytes = Encoding.UTF8.GetBytes(command + "\n");
        pipe.Write(bytes);
        pipe.Flush();
    }

    private static void WaitUntilSubscribed(WindowManager core, Listener listener)
    {
        for (var i = 1; i <= 40; i++)
        {
            core.Handle(new WindowAppeared(new WindowId(100 + i), new Rectangle(0, 0, 10, 10)));
            if (listener.TryTake(TimeSpan.FromMilliseconds(500), out _))
                return;
        }

        throw new TimeoutException("The event subscriber did not connect.");
    }

    // Everything a listener is sent arrives in order. A window opened now is the last thing it hears about,
    // so reading up to that window leaves nothing queued from before. A quiet gap proves nothing on a slow machine.
    private static void Sync(WindowManager core, params Listener[] listeners)
    {
        const long marker = 0x270F;
        core.Handle(new WindowAppeared(new WindowId(marker), new Rectangle(0, 0, 10, 10)));
        foreach (var listener in listeners)
        {
            var seen = false;
            while (!seen)
            {
                Assert.True(listener.TryTake(TimeSpan.FromSeconds(10), out var line), "The marker window was never reported.");
                seen = line == "activewindowv2>>270f";
            }
        }
    }

    // Reads the lines the test expects, however long a slow machine takes to deliver them. Anything that
    // arrives right after them is returned too, so an unexpected extra line still fails the comparison.
    private static List<string> Take(Listener listener, int count)
    {
        var lines = new List<string>();
        while (lines.Count < count)
        {
            Assert.True(listener.TryTake(TimeSpan.FromSeconds(10), out var line), $"Only {lines.Count} of {count} events arrived: {string.Join(", ", lines)}");
            lines.Add(line);
        }

        while (listener.TryTake(TimeSpan.FromMilliseconds(200), out var extra))
            lines.Add(extra);

        return lines;
    }

    private sealed class Listener : IDisposable
    {
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();
        private readonly CancellationTokenSource _cancel = new();
        private readonly Task _task;

        public Listener(string pipe) =>
            _task = Task.Run(() => CommandClient.Follow("events", line => _lines.Writer.TryWrite(line), pipe, cancel: _cancel.Token));

        public bool TryTake(TimeSpan timeout, out string line)
        {
            using var cancel = new CancellationTokenSource(timeout);
            try
            {
                line = _lines.Reader.ReadAsync(cancel.Token).AsTask().GetAwaiter().GetResult();
                return true;
            }
            catch (OperationCanceledException)
            {
                line = "";
                return false;
            }
        }

        public void Dispose()
        {
            _cancel.Cancel();
            try
            {
                _task.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }

            _cancel.Dispose();
        }
    }
}

// ThreadPool.ThreadCount is process-wide, so other collections must finish before these tests run.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CommandServerCollection
{
    public const string Name = "Command server";
}
