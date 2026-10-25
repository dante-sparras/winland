using System.Globalization;
using System.Text.Json;

namespace Winland.Application.Tests;

public class CommandTests
{
    [Fact]
    public void PipeDispatchParsesTheNameAndArguments()
    {
        var parsed = Assert.IsType<CommandParsed>(CommandProtocol.Parse("dispatch exec notepad --title hi"));
        var dispatch = Assert.IsType<DispatchRequest>(parsed.Request);

        Assert.Equal("exec", dispatch.Name);
        Assert.Equal("notepad --title hi", dispatch.Arguments);
    }

    [Fact]
    public void PipeDispatchKeepsInternalSpacing()
    {
        var parsed = Assert.IsType<CommandParsed>(CommandProtocol.Parse("dispatch   exec   foo  bar"));
        var dispatch = Assert.IsType<DispatchRequest>(parsed.Request);

        Assert.Equal("exec", dispatch.Name);
        Assert.Equal("foo  bar", dispatch.Arguments);
    }

    [Fact]
    public void ExecOverThePipeRunsTheCommand()
    {
        var core = new WindowManager();

        var reply = core.Execute("dispatch exec elevated notepad");

        Assert.Equal("ok", reply.Text);
        Assert.Equal("exec", reply.Effect.Dispatcher);
        Assert.Null(reply.Effect.Close);
        Assert.Empty(reply.Effect.Placements);
    }

    [Fact]
    public void BoundExecRuns()
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = SUPER, E, exec, notepad")));
        core.Handle(new KeyPressed("Super_L"));

        var outcome = core.Handle(new KeyPressed("E"));

        Assert.Equal("exec", outcome.Dispatcher);
        Assert.Null(outcome.Close);
    }

    [Fact]
    public void DispatchKillactiveAsksToCloseTheActiveClientAndLeavesItInPlace()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area));

        var reply = core.Execute("dispatch killactive");

        Assert.Equal("ok", reply.Text);
        Assert.Equal(new WindowId(2), reply.Effect.Close);
        Assert.Equal(2, reply.Effect.State.Clients.Count);
        Assert.Contains(reply.Effect.State.Clients, client => client.Window == new WindowId(2));
    }

    [Fact]
    public void DispatchWithoutANameIsRejected()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 100, 100)));

        var reply = core.Execute("dispatch");

        Assert.Equal("error: missing dispatcher", reply.Text);
        Assert.Null(reply.Effect.Close);
        Assert.Null(reply.Effect.Dispatcher);
    }

    [Fact]
    public void UnknownRequestIsPlainText()
    {
        var reply = new WindowManager().Execute("not-a-command");

        Assert.Equal("error: unknown request", reply.Text);
    }

    [Theory]
    [InlineData("clients")]
    [InlineData("workspaces")]
    [InlineData("monitors")]
    [InlineData("activewindow")]
    [InlineData("activeworkspace")]
    [InlineData("events")]
    public void ReadCommandsRejectTrailingArguments(string command)
    {
        var reply = new WindowManager().Execute(command + " extra");

        Assert.Equal("error: unknown request", reply.Text);
    }

    [Fact]
    public void ClientsReplyIsEmptyUntilAWindowIsAdopted()
    {
        var core = new WindowManager();
        var clients = Json(core.Execute("j/clients").Text);

        Assert.Equal(JsonValueKind.Array, clients.ValueKind);
        Assert.Equal(0, clients.GetArrayLength());
        Assert.Equal("no open windows", core.Execute("clients").Text);
    }

    [Fact]
    public void ClientsReplyUsesTheAddressFloatingAndWorkspaceShape()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 800, 600);
        core.Handle(new WindowAppeared(new WindowId(42), area));
        core.Handle(new WindowAppeared(new WindowId(255), area)
        {
            Traits = WindowTraits.Normal with { Dialog = true },
            Bounds = new Rectangle(4, 5, 6, 7),
        });

        var clients = Json(core.Execute("j/Clients").Text);

        Assert.Equal(2, clients.GetArrayLength());
        AssertClient(clients[0], "0x2a", floating: false, hidden: false, workspaceId: 1, workspaceName: "1");
        AssertClient(clients[1], "0xff", floating: true, hidden: false, workspaceId: 1, workspaceName: "1");
    }

    [Fact]
    public void MinimizedClientIsReportedHidden()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(7), new Rectangle(0, 0, 100, 100)));
        core.Handle(new WindowMinimized(new WindowId(7)));

        var client = Assert.Single(Json(core.Execute("ja/clients").Text).EnumerateArray());

        Assert.True(client.GetProperty("hidden").GetBoolean());
        Assert.False(client.GetProperty("mapped").GetBoolean());
    }

    [Fact]
    public void ClientWorkspaceFollowsTheStoredWorkspace()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = workspace 2 silent, class:^(kitty)$\n"));
        core.Handle(new WindowAppeared(new WindowId(9), new Rectangle(0, 0, 100, 100))
        {
            Traits = WindowTraits.Normal with { Class = "kitty" },
        });

        var client = Assert.Single(Json(core.Execute("j/clients").Text).EnumerateArray());
        var workspace = client.GetProperty("workspace");

        Assert.Equal(2, workspace.GetProperty("id").GetInt32());
        Assert.Equal("2", workspace.GetProperty("name").GetString());
    }

    [Fact]
    public void WorkspacesReplyStartsWithTheDefaultWorkspace()
    {
        var workspace = Assert.Single(Json(new WindowManager().Execute("j/workspaces").Text).EnumerateArray());

        Assert.Equal(1, workspace.GetProperty("id").GetInt32());
        Assert.Equal("1", workspace.GetProperty("name").GetString());
        Assert.Equal(0, workspace.GetProperty("windows").GetInt32());
    }

    [Fact]
    public void WorkspacesReplyCountsClientsAndKeepsTheDefault()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = workspace 2, class:^(kitty)$\n"));
        var area = new Rectangle(0, 0, 100, 100);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area)
        {
            Traits = WindowTraits.Normal with { Class = "kitty" },
        });

        var workspaces = Json(core.Execute("j/workspaces").Text);
        var byName = workspaces.EnumerateArray().ToDictionary(item => item.GetProperty("name").GetString()!);

        Assert.Equal(1, byName["1"].GetProperty("id").GetInt32());
        Assert.Equal(1, byName["1"].GetProperty("windows").GetInt32());
        Assert.Equal(2, byName["2"].GetProperty("id").GetInt32());
        Assert.Equal(1, byName["2"].GetProperty("windows").GetInt32());
    }

    [Fact]
    public void MonitorsReplyIsTheDefaultMonitorOnTheActiveWorkspace()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = workspace 4, class:^(kitty)$\n"));
        core.Handle(new WindowAppeared(new WindowId(3), new Rectangle(0, 0, 100, 100))
        {
            Traits = WindowTraits.Normal with { Class = "kitty" },
        });

        var monitor = Assert.Single(Json(core.Execute("j/monitors").Text).EnumerateArray());
        var active = monitor.GetProperty("activeWorkspace");

        Assert.Equal(0, monitor.GetProperty("id").GetInt32());
        Assert.Equal("", monitor.GetProperty("name").GetString());
        Assert.True(monitor.GetProperty("focused").GetBoolean());
        Assert.Equal(4, active.GetProperty("id").GetInt32());
        Assert.Equal("4", active.GetProperty("name").GetString());
    }

    [Fact]
    public void ActiveWindowIsEmptyUntilAClientExists()
    {
        var core = new WindowManager();
        var active = Json(core.Execute("j/activewindow").Text);

        Assert.Equal(JsonValueKind.Object, active.ValueKind);
        Assert.Empty(active.EnumerateObject());
        Assert.Equal("Invalid", core.Execute("activewindow").Text);
    }

    [Fact]
    public void ActiveWindowFollowsTheLatestAdoptedClient()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 640, 480);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area));

        var active = Json(core.Execute("j/activewindow").Text);

        Assert.Equal("0x2", active.GetProperty("address").GetString());

        core.Handle(new WindowClosed(new WindowId(2)));

        active = Json(core.Execute("j/activewindow").Text);
        Assert.Equal("0x1", active.GetProperty("address").GetString());
    }

    [Fact]
    public void DispatchWorkspaceOverThePipeSwitches()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 800, 600);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area));

        CommandReply reply = default;
        var thread = new Thread(() => reply = core.Execute("dispatch workspace 2"));
        thread.Start();
        thread.Join();

        Assert.Equal("ok", reply.Text);
        Assert.Equal("workspace", reply.Effect.Dispatcher);
        Assert.Equal(new WorkspaceId("2"), reply.Effect.State.ActiveWorkspace);
        Assert.Equal([new WindowId(1), new WindowId(2)], reply.Effect.Hide);
        Assert.Empty(reply.Effect.Show);
    }

    [Fact]
    public void WorkspacesReplyAfterASwitchReportsEachDesk()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 800, 600);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area));
        core.Execute("dispatch workspace 2");
        core.Handle(new WindowAppeared(new WindowId(3), area));

        var byName = Json(core.Execute("j/workspaces").Text)
            .EnumerateArray()
            .ToDictionary(item => item.GetProperty("name").GetString()!);

        Assert.Equal(1, byName["1"].GetProperty("id").GetInt32());
        Assert.Equal(2, byName["1"].GetProperty("windows").GetInt32());
        Assert.Equal(2, byName["2"].GetProperty("id").GetInt32());
        Assert.Equal(1, byName["2"].GetProperty("windows").GetInt32());

        var clients = Json(core.Execute("j/clients").Text).EnumerateArray().ToArray();
        AssertClient(clients[0], "0x1", floating: false, hidden: true, workspaceId: 1, workspaceName: "1");
        AssertClient(clients[1], "0x2", floating: false, hidden: true, workspaceId: 1, workspaceName: "1");
        AssertClient(clients[2], "0x3", floating: false, hidden: false, workspaceId: 2, workspaceName: "2");

        var monitor = Assert.Single(Json(core.Execute("j/monitors").Text).EnumerateArray());
        var activeWorkspace = monitor.GetProperty("activeWorkspace");
        Assert.Equal(2, activeWorkspace.GetProperty("id").GetInt32());
        Assert.Equal("2", activeWorkspace.GetProperty("name").GetString());

        var active = Json(core.Execute("j/activewindow").Text);
        Assert.Equal("0x3", active.GetProperty("address").GetString());
        Assert.False(active.GetProperty("hidden").GetBoolean());
        Assert.Equal(2, active.GetProperty("workspace").GetProperty("id").GetInt32());
    }

    [Fact]
    public void PipeRunsFocusFloatAndMoveOnTheActiveWorkspace()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 1920, 1080);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area));

        var focus = core.Execute("dispatch movefocus l");

        Assert.Equal("ok", focus.Text);
        Assert.Equal("movefocus", focus.Effect.Dispatcher);
        Assert.Equal(new WindowId(1), focus.Effect.Focus);

        var floating = core.Execute("dispatch togglefloating");

        Assert.Equal("togglefloating", floating.Effect.Dispatcher);
        Assert.True(Client(floating.Effect, 1).Floating);
        Assert.False(Client(floating.Effect, 2).Floating);

        var moved = core.Execute("dispatch movetoworkspace 3");

        Assert.Equal("movetoworkspace", moved.Effect.Dispatcher);
        Assert.Equal(new WorkspaceId("3"), moved.Effect.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("3"), Client(moved.Effect, 1).Workspace);
        Assert.Equal(new WorkspaceId("1"), Client(moved.Effect, 2).Workspace);
        Assert.Contains(moved.Effect.State.Hidden, client => client.Window == new WindowId(2));
    }

    [Fact]
    public void BoundWorkspaceSwitches()
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = SUPER, 2, workspace, 2")));
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 100, 100)));
        core.Handle(new KeyPressed("Super_L"));

        var outcome = core.Handle(new KeyPressed("2"));

        Assert.Equal("workspace", outcome.Dispatcher);
        Assert.True(outcome.Consumed);
        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal([new WindowId(1)], outcome.Hide);
    }

    [Fact]
    public void ConcurrentPipeRequestsLeaveTheDesksConsistent()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));
        var failures = 0;
        var threads = new Thread[4];
        for (var index = 0; index < threads.Length; index++)
        {
            var step = index;
            threads[index] = new Thread(() =>
            {
                for (var n = 0; n < 30; n++)
                {
                    try
                    {
                        var workspace = ((step + n) % 3) + 1;
                        if (n % 2 == 0)
                            core.Execute("dispatch workspace " + workspace.ToString(CultureInfo.InvariantCulture));
                        else
                            core.Handle(new Dispatch("workspace", workspace.ToString(CultureInfo.InvariantCulture)));

                        _ = core.Execute("j/workspaces");
                        _ = core.Execute("j/clients");
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        Interlocked.Increment(ref failures);
                    }
                }
            });
        }

        foreach (var thread in threads)
            thread.Start();
        foreach (var thread in threads)
            thread.Join();

        Assert.Equal(0, failures);
        var active = Assert.Single(Json(core.Execute("j/monitors").Text).EnumerateArray())
            .GetProperty("activeWorkspace")
            .GetProperty("id")
            .GetInt32();
        Assert.InRange(active, 1, 3);
        Assert.Contains(
            Json(core.Execute("j/workspaces").Text).EnumerateArray(),
            workspace => workspace.GetProperty("id").GetInt32() == active);
    }

    private static void AssertClient(
        JsonElement client,
        string address,
        bool floating,
        bool hidden,
        int workspaceId,
        string workspaceName)
    {
        Assert.Equal(address, client.GetProperty("address").GetString());
        Assert.Equal(floating, client.GetProperty("floating").GetBoolean());
        Assert.Equal(hidden, client.GetProperty("hidden").GetBoolean());
        Assert.Equal(!hidden, client.GetProperty("mapped").GetBoolean());
        Assert.Equal(workspaceId, client.GetProperty("workspace").GetProperty("id").GetInt32());
        Assert.Equal(workspaceName, client.GetProperty("workspace").GetProperty("name").GetString());
    }
}
