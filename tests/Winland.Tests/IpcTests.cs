using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using FluentAssertions;
using Winland.Ipc;

namespace Winland.Tests;

[TestClass]
public sealed class IpcTests
{
    [TestMethod]
    public void A_line_round_trips_and_a_carriage_return_is_not_part_of_it()
    {
        using var stream = new MemoryStream();
        LineCodec.Write(stream, "{\"kind\":\"ping\"}");
        stream.Write(Encoding.UTF8.GetBytes("hi\r\n"));
        stream.Position = 0;

        LineCodec.TryRead(stream, CancellationToken.None, out var first).Should().BeTrue();
        first.Should().Be("{\"kind\":\"ping\"}");
        LineCodec.TryRead(stream, CancellationToken.None, out var second).Should().BeTrue();
        second.Should().Be("hi");
        LineCodec.TryRead(stream, CancellationToken.None, out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_state_push_round_trips_with_its_keys()
    {
        var push = new ShellPush(
            PickerOpen: true,
            Escape: true,
            SessionLocked: false,
            SurfaceHeld: true,
            Keys: [new ClaimedKey(0x25, Super: true, Ctrl: false, Alt: false, Id: 1)]);

        ShellPush.TryParse(push.ToJson(), out var read).Should().BeTrue();
        read.PickerOpen.Should().BeTrue();
        read.Escape.Should().BeTrue();
        read.SessionLocked.Should().BeFalse();
        read.SurfaceHeld.Should().BeTrue();
        read.Keys.Should().Equal(new ClaimedKey(0x25, true, false, false, 1));
        read.BarThickness.Should().Be(0);
        read.BarEdge.Should().Be("top");
        read.GroupBar.Should().Be(0);
        (read.Bars ?? []).Should().BeEmpty();
    }

    [TestMethod]
    public void A_state_push_round_trips_the_bar()
    {
        var push = new ShellPush(false, false, false, false, [], BarThickness: 40, BarEdge: "bottom", Bars: [15L, 16L], GroupBar: 28);
        ShellPush.TryParse(push.ToJson(), out var read).Should().BeTrue();
        read.BarThickness.Should().Be(40);
        read.BarEdge.Should().Be("bottom");
        read.GroupBar.Should().Be(28);
        (read.Bars ?? []).Should().Equal(15L, 16L);
    }

    [TestMethod]
    public void The_same_build_is_accepted_and_another_build_is_refused()
    {
        Handshake.Accepts("abc", Handshake.Hello("abc"), out var peer).Should().BeTrue();
        peer.Should().Be("abc");
        Handshake.Accepts("abc", Handshake.Hello("other"), out var refused).Should().BeFalse();
        refused.Should().Be("other");
        Handshake.Accepts("abc", "{\"kind\":\"ping\"}", out var none).Should().BeFalse();
        none.Should().BeEmpty();

        Handshake.TryAnswer(Handshake.Accept("abc"), out var ok, out var stamp).Should().BeTrue();
        ok.Should().BeTrue();
        stamp.Should().Be("abc");
        Handshake.TryAnswer(Handshake.Refuse("abc"), out var denied, out _).Should().BeTrue();
        denied.Should().BeFalse();
        Handshake.ServerRefusal("other", "abc").Should().Be("refused a client that speaks another protocol: client other server abc");
        Handshake.ClientRefusal("other", "abc").Should().Be("refused by Winland speaking another protocol: client other server abc");
    }

    [TestMethod]
    public void An_absent_winland_answers_at_once()
    {
        var (requests, events) = Names();
        using var client = WinlandClient.Start("stamp", _ => { }, requests, events);
        var started = Stopwatch.StartNew();

        client.Connected.Should().BeFalse();
        client.TryDispatch("workspace.jump.3", [], out var error).Should().BeFalse();
        error.Should().Be("Winland is not running");
        client.TryQuery("clients", json: true, out _).Should().BeFalse();
        client.TryPush(ShellPush.Closed).Should().BeFalse();
        started.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }

    [TestMethod]
    public void A_client_connects_when_winland_appears_and_a_push_a_query_and_a_command_arrive()
    {
        var (requests, events) = Names();
        var engine = new RecordingEngine();
        using var client = WinlandClient.Start("stamp", _ => { }, requests, events);
        client.TryQuery("workspaces", json: false, out _).Should().BeFalse();

        using var server = new WinlandServer(engine, "stamp", _ => { }, requests, events);
        server.Start();
        server.WaitListening(TimeSpan.FromSeconds(5)).Should().BeTrue();
        WaitUntil(() => client.Connected);

        var push = new ShellPush(true, false, true, false, [new ClaimedKey(0x0D, false, false, false, 5)]);
        client.TryPush(push).Should().BeTrue();
        var got = engine.Pushes.Should().ContainSingle().Subject;
        got.PickerOpen.Should().BeTrue();
        got.Escape.Should().BeFalse();
        got.SessionLocked.Should().BeTrue();
        got.SurfaceHeld.Should().BeFalse();
        got.Keys.Should().Equal(push.Keys);

        client.TryQuery("clients", json: true, out var body).Should().BeTrue();
        body.Should().Be("[]");
        engine.Queries.Should().ContainSingle().Which.Should().Be(("clients", true));

        client.TryDispatch("workspace.jump.3", ["follow"], out var error).Should().BeTrue();
        error.Should().BeEmpty();
        engine.Commands.Should().ContainSingle().Which.Should().Be(("workspace.jump.3", "follow"));

        engine.Fail = "no such command";
        client.TryDispatch("nope", [], out error).Should().BeFalse();
        error.Should().Be("no such command");
    }

    [TestMethod]
    public void A_global_bind_is_delivered_and_publishing_does_not_wait_for_the_handler()
    {
        var (requests, events) = Names();
        var engine = new RecordingEngine();
        var seen = new ConcurrentQueue<string>();
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        using var server = new WinlandServer(engine, "stamp", _ => { }, requests, events);
        server.Start();
        server.WaitListening(TimeSpan.FromSeconds(5)).Should().BeTrue();
        using var client = WinlandClient.Start("stamp", _ => { }, requests, events);
        client.Event += line =>
        {
            seen.Enqueue(line);
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        };
        WaitUntil(() => client.Connected);

        var watch = Stopwatch.StartNew();
        server.Publish(Wire.Bind("winoma:lock", down: true, hold: false));
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(200));
        started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        release.Set();
        WaitUntil(() => !seen.IsEmpty);
        seen.TryDequeue(out var line).Should().BeTrue();
        Wire.TryEvent(line, out var kind, out var root, out var document).Should().BeTrue();
        using (document)
        {
            kind.Should().Be("bind");
            JsonLine.Text(root, "name").Should().Be("winoma:lock");
            JsonLine.Flag(root, "down").Should().BeTrue();
            JsonLine.Flag(root, "hold").Should().BeFalse();
        }
    }

    [TestMethod]
    public void A_client_from_another_build_is_refused_and_both_sides_log_it()
    {
        var (requests, events) = Names();
        var serverLog = new ConcurrentQueue<string>();
        var clientLog = new ConcurrentQueue<string>();
        using var server = new WinlandServer(new RecordingEngine(), "server-build", serverLog.Enqueue, requests, events);
        server.Start();
        server.WaitListening(TimeSpan.FromSeconds(5)).Should().BeTrue();
        using var client = WinlandClient.Start("client-build", clientLog.Enqueue, requests, events);

        WaitUntil(() => serverLog.Any(line => line.Contains("client-build", StringComparison.Ordinal)));
        WaitUntil(() => clientLog.Any(line => line.Contains("server-build", StringComparison.Ordinal)));
        string.Join("\n", serverLog).Should().Contain("refused a client that speaks another protocol: client client-build server server-build");
        string.Join("\n", clientLog).Should().Contain("refused by Winland speaking another protocol: client client-build server server-build");
        client.Connected.Should().BeFalse();
    }

    [TestMethod]
    public void An_event_client_that_leaves_frees_its_pipe()
    {
        var (requests, events) = Names();
        using var server = new WinlandServer(new RecordingEngine(), "stamp", _ => { }, requests, events);
        server.Start();
        server.WaitListening(TimeSpan.FromSeconds(5)).Should().BeTrue();

        for (var i = 0; i < 10; i++)
        {
            using var pipe = new NamedPipeClientStream(".", events, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(2000);
            LineCodec.Write(pipe, Handshake.Hello("stamp"));
            LineCodec.TryRead(pipe, CancellationToken.None, out var hello).Should().BeTrue();
            Handshake.TryAnswer(hello, out var ok, out _).Should().BeTrue();
            ok.Should().BeTrue();
        }

        using var client = WinlandClient.Start("stamp", _ => { }, requests, events);
        WaitUntil(() => client.Connected);
        client.TryQuery("activewindow", json: false, out var body).Should().BeTrue();
        body.Should().Be("none");
    }

    [TestMethod]
    public void The_shell_and_a_short_query_can_both_be_connected()
    {
        var (requests, events) = Names();
        using var server = new WinlandServer(new RecordingEngine(), "stamp", _ => { }, requests, events);
        server.Start();
        server.WaitListening(TimeSpan.FromSeconds(5)).Should().BeTrue();
        using var shell = WinlandClient.Start("stamp", _ => { }, requests, events);
        using var cli = WinlandClient.Start("stamp", _ => { }, requests, events);
        WaitUntil(() => shell.Connected && cli.Connected);
        shell.TryQuery("workspaces", json: true, out var left).Should().BeTrue();
        cli.TryQuery("activewindow", json: false, out var right).Should().BeTrue();
        left.Should().Be("[]");
        right.Should().Be("none");
    }

    private static (string Requests, string Events) Names()
    {
        var id = Guid.NewGuid().ToString("N");
        return ("Winland.Test.Rpc." + id, "Winland.Test.Events." + id);
    }

    private static void WaitUntil(Func<bool> ready)
    {
        var until = Environment.TickCount64 + 8000;
        while (Environment.TickCount64 < until)
        {
            if (ready())
            {
                return;
            }

            Thread.Sleep(20);
        }

        ready().Should().BeTrue("the pipes should have settled");
    }

    private sealed class RecordingEngine : IWinlandEngine
    {
        internal List<ShellPush> Pushes { get; } = [];

        internal List<(string Name, bool Json)> Queries { get; } = [];

        internal List<(string Command, string Args)> Commands { get; } = [];

        internal string? Fail { get; set; }

        public void Apply(ShellPush state) => Pushes.Add(state);

        public string Query(string name, bool json)
        {
            Queries.Add((name, json));
            return json ? "[]" : "none";
        }

        public string? Dispatch(string command, IReadOnlyList<string> args)
        {
            Commands.Add((command, string.Join(" ", args)));
            return Fail;
        }
    }
}
