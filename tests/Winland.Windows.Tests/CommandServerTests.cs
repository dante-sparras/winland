using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Winland;
using Winland.Windows;

namespace Winland.Windows.Tests;

public class CommandServerTests
{
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
        Assert.NotNull(descriptor.SystemAcl);
        var label = Assert.IsType<CustomAce>(Assert.Single(descriptor.SystemAcl.Cast<GenericAce>()));
        var opaque = label.GetOpaque();
        Assert.NotNull(opaque);
        Assert.Equal(new byte[] { 1, 0, 0, 0 }, opaque[..4]);
        Assert.Equal(new byte[] { 1, 1, 0, 0, 0, 0, 0, 0x10, 0, 0x20, 0, 0 }, opaque[^12..]);
    }

    [Fact]
    public void ClientRoundTripsClientsAndDispatch()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(42), new Rectangle(0, 0, 10, 10)));
        var name = "winland-" + Guid.NewGuid().ToString("N");
        using var server = new CommandServer(core, name);
        server.Start();

        var clients = JsonDocument.Parse(CommandClient.Transact("clients", name)).RootElement;
        var client = Assert.Single(clients.EnumerateArray());
        Assert.Equal("0x2a", client.GetProperty("address").GetString());
        Assert.Equal("ok", CommandClient.Transact("dispatch exec", name));
        Assert.Equal("error: unknown request", CommandClient.Transact("nope", name));
    }

    [Fact]
    public void SubscribersReceiveEachEventInOrder()
    {
        var core = new Core();
        var name = PipeName();
        using var server = new CommandServer(core, name);
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
            ["openwindow>>7,1,app,title", "activewindow>>app,title", "activewindowv2>>7"],
            Take(first, 3));
        Assert.Equal(
            ["openwindow>>7,1,app,title", "activewindow>>app,title", "activewindowv2>>7"],
            Take(second, 3));

        Assert.Equal("ok", CommandClient.Transact("dispatch workspace 2", name));
        Assert.Equal(
            [
                "createworkspace>>2",
                "createworkspacev2>>2,2",
                "workspace>>2",
                "workspacev2>>2,2",
                "activewindow>>,",
                "activewindowv2>>",
            ],
            Take(first, 6));
        Assert.Equal(
            [
                "createworkspace>>2",
                "createworkspacev2>>2,2",
                "workspace>>2",
                "workspacev2>>2,2",
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
        var core = new Core();
        var name = PipeName();
        using var server = new CommandServer(core, name);
        server.Start();
        using var idle = Connect(name);
        Send(idle, "events");
        Assert.Equal("[]", CommandClient.Transact("clients", name));

        var finished = Task.Run(() =>
        {
            for (var i = 0; i < 8000; i++)
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
        var core = new Core();
        var name = PipeName();
        using var server = new CommandServer(core, name);
        server.Start();
        var idle = Connect(name);
        Send(idle, "events");
        Assert.Equal("[]", CommandClient.Transact("clients", name));
        idle.Dispose();

        using var listener = new Listener(name);
        WaitUntilSubscribed(core, listener);
        Sync(core, listener);
        core.Handle(new WindowAppeared(new WindowId(3), new Rectangle(0, 0, 10, 10)));
        Assert.Equal(["openwindow>>3,1,,", "activewindow>>,", "activewindowv2>>3"], Take(listener, 3));
        Assert.Equal("ok", CommandClient.Transact("dispatch workspace 1", name));
    }

    [Fact]
    public void CursorBatchAndSystemInfoTravelThePipe()
    {
        var core = new Core();
        var name = PipeName();
        using var server = new CommandServer(core, name);
        server.Start();

        using var cursor = JsonDocument.Parse(CommandClient.Transact("cursorpos", name));
        Assert.Equal(JsonValueKind.Number, cursor.RootElement.GetProperty("x").ValueKind);
        Assert.Equal(JsonValueKind.Number, cursor.RootElement.GetProperty("y").ValueKind);

        var batch = CommandClient.Transact("[[BATCH]]layouts;locked", name);
        Assert.Equal("[\"dwindle\",\"master\",\"scrolling\",\"monocle\"]\n\n\n{\"locked\":false}", batch);

        var info = CommandClient.Transact("systeminfo", name);
        Assert.Contains("Winland ", info, StringComparison.Ordinal);
        Assert.Contains("\nOS: ", info, StringComparison.Ordinal);
        Assert.Contains("\n.NET: ", info, StringComparison.Ordinal);
        Assert.Contains("\nMonitors:", info, StringComparison.Ordinal);
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

    private static void WaitUntilSubscribed(Core core, Listener listener)
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
    private static void Sync(Core core, params Listener[] listeners)
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
            _task = Task.Run(() => CommandClient.Follow("events", line => _lines.Writer.TryWrite(line), _cancel.Token, pipe));

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
