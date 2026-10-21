using System.Text.Json;
using Winland;

namespace Winland.Tests;

public class RecoveryTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void RecoveryRecordsEachClientsWorkspaceMonitorAndWhetherItIsHidden()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Class = "Notepad", Title = "Untitled" },
        });
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { Class = "CabinetWClass", Title = "Home" },
        });

        var snapshot = core.Recovery();

        var hidden = Assert.Single(snapshot.Clients, client => client.Window == new WindowId(1));
        Assert.Equal(new WorkspaceId("1"), hidden.Workspace);
        Assert.Equal(MonitorId.Primary, hidden.Monitor);
        Assert.True(hidden.Hidden);
        Assert.Equal("Notepad", hidden.Class);
        Assert.Equal("Untitled", hidden.Title);
        Assert.Equal(0, hidden.ProcessId);

        var shown = Assert.Single(snapshot.Clients, client => client.Window == new WindowId(2));
        Assert.Equal(new WorkspaceId("2"), shown.Workspace);
        Assert.Equal(MonitorId.Primary, shown.Monitor);
        Assert.False(shown.Hidden);
        Assert.Equal("CabinetWClass", shown.Class);
        Assert.Equal("Home", shown.Title);
    }

    [Fact]
    public void RecoveryKeepsTheMonitorThatOwnsTheWorkspace()
    {
        var core = new Core();
        var left = Display(@"\\.\DISPLAY1", 0, 1920, 1080);
        var right = Display(@"\\.\DISPLAY2", 1920, 1280, 1024);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var snapshot = core.Recovery();

        var first = Assert.Single(snapshot.Clients, client => client.Window == new WindowId(1));
        var second = Assert.Single(snapshot.Clients, client => client.Window == new WindowId(2));
        Assert.Equal(new WorkspaceId("1"), first.Workspace);
        Assert.Equal(new MonitorId(0), first.Monitor);
        Assert.False(first.Hidden);
        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), second.Workspace);
        Assert.Equal(new MonitorId(1), second.Monitor);
        Assert.False(second.Hidden);
    }

    [Fact]
    public void MinimizedClientOnAnInactiveWorkspaceIsRecordedAsHidden()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowMinimized(new WindowId(1)));
        core.Handle(new Dispatch("workspace", "2"));

        var client = Assert.Single(core.Recovery().Clients);
        Assert.Equal(new WorkspaceId("1"), client.Workspace);
        Assert.True(client.Hidden);
    }

    [Fact]
    public void SnapshotRoundTripsThroughJson()
    {
        var snapshot = new RecoverySnapshot(
        [
            new RecoveredClient(new WindowId(42), new WorkspaceId("notes"), MonitorId.Primary, true, "Notepad", "Untitled", 4242)
            {
                MonitorIdentity = "Dell P2415Q A",
            },
            new RecoveredClient(new WindowId(7), WorkspaceId.Initial, MonitorId.Primary, false, "CabinetWClass", "Home", 9)
            {
                MonitorIdentity = @"\\.\DISPLAY1",
            },
        ]);

        var json = snapshot.ToJson();
        using var document = JsonDocument.Parse(json);
        var clients = document.RootElement.GetProperty("clients").EnumerateArray().ToArray();
        Assert.Equal(2, clients.Length);
        Assert.Equal(42, clients[0].GetProperty("window").GetInt64());
        Assert.Equal("notes", clients[0].GetProperty("workspace").GetString());
        Assert.Equal("Dell P2415Q A", clients[0].GetProperty("monitor").GetString());
        Assert.True(clients[0].GetProperty("hidden").GetBoolean());
        Assert.Equal("Notepad", clients[0].GetProperty("class").GetString());
        Assert.Equal("Untitled", clients[0].GetProperty("title").GetString());
        Assert.Equal(4242, clients[0].GetProperty("process").GetInt64());

        Assert.True(RecoverySnapshot.TryParse(json, out var parsed));
        Assert.Equal(snapshot.Clients.Count, parsed.Clients.Count);
        Assert.Equal(snapshot.Clients[0], parsed.Clients[0]);
        Assert.Equal(snapshot.Clients[1], parsed.Clients[1]);
    }

    [Fact]
    public void TheFrameAndRegionAWindowWasLeftWithRoundTrip()
    {
        var changed = new RecoveredClient(new WindowId(42), WorkspaceId.Initial, MonitorId.Primary, false, "App", "App", 1)
        {
            FrameStyle = 0x14CF0000,
            Region = true,
        };
        var plain = new RecoveredClient(new WindowId(7), WorkspaceId.Initial, MonitorId.Primary, false, "App", "App", 1);

        var json = new RecoverySnapshot([changed, plain]).ToJson();

        Assert.True(RecoverySnapshot.TryParse(json, out var parsed));
        Assert.Equal(changed, parsed.Clients[0]);
        Assert.Equal(plain, parsed.Clients[1]);
        Assert.DoesNotContain("frameStyle\":null", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"clients\":null}")]
    public void UnreadableSnapshotIsRejected(string json)
    {
        Assert.False(RecoverySnapshot.TryParse(json, out _));
    }

    [Fact]
    public void RestoreRejoinsTheSameWindowAndIgnoresOnesThatNeverAppear()
    {
        var prior = new Core();
        prior.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Class = "Notepad", Title = "Untitled" },
        });
        prior.Handle(new WindowAppeared(new WindowId(2), Area));
        prior.Handle(new Dispatch("workspace", "2"));
        var snapshot = prior.Recovery();

        var core = new Core();
        core.Restore(snapshot);
        var rejoined = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Class = "Notepad", Title = "Untitled" },
        });

        var client = Assert.Single(rejoined.State.Clients);
        Assert.Equal(new WindowId(1), client.Window);
        Assert.Equal(new WorkspaceId("1"), client.Workspace);
        Assert.Equal(MonitorId.Primary, Assert.Single(rejoined.State.Workspaces, workspace => workspace.Id == new WorkspaceId("1")).Monitor);
        Assert.DoesNotContain(rejoined.State.Hidden, hidden => hidden.Window == new WindowId(1));
        Assert.DoesNotContain(rejoined.State.Clients, candidate => candidate.Window == new WindowId(2));
        Assert.Empty(rejoined.Hide);
    }

    [Fact]
    public void RestorePutsAClientBackOnAnInactiveWorkspaceWithoutSwitching()
    {
        var snapshot = new RecoverySnapshot(
        [
            new RecoveredClient(new WindowId(5), new WorkspaceId("4"), MonitorId.Primary, true, "Notepad", "Notes", 10),
        ]);
        var core = new Core(ConfigParser.Parse("windowrulev2 = workspace 9, class:^(Notepad)$\n"));
        core.Restore(snapshot);

        var outcome = core.Handle(new WindowAppeared(new WindowId(5), Area)
        {
            Traits = WindowTraits.Normal with { Class = "Notepad", Title = "Notes" },
        });

        var client = Assert.Single(outcome.State.Clients);
        Assert.Equal(new WorkspaceId("4"), client.Workspace);
        Assert.Equal(WorkspaceId.Initial, outcome.State.ActiveWorkspace);
        Assert.Contains(outcome.State.Hidden, hidden => hidden.Window == new WindowId(5));
        Assert.Equal([new WindowId(5)], outcome.Hide);
    }

    [Fact]
    public void RestoreKeepsTheClientUntilALaterAppearanceCanAdoptIt()
    {
        var snapshot = new RecoverySnapshot(
        [
            new RecoveredClient(new WindowId(5), new WorkspaceId("3"), new MonitorId(1), true, "Notepad", "Notes", 10),
        ]);
        var core = new Core();
        var right = Display(@"\\.\DISPLAY2", 1920, 1280, 1024);
        core.Handle(new MonitorsChanged(
        [
            Display(@"\\.\DISPLAY1", 0, 1920, 1080),
            right,
        ]));
        core.Restore(snapshot);
        var blocked = core.Handle(new WindowAppeared(new WindowId(5), right.WorkArea)
        {
            Traits = WindowTraits.Normal with { Visible = false, Class = "Notepad", Title = "Notes" },
        });
        Assert.Empty(blocked.State.Clients);

        var outcome = core.Handle(new WindowAppeared(new WindowId(5), right.WorkArea)
        {
            Traits = WindowTraits.Normal with { Class = "Notepad", Title = "Notes" },
        });

        var client = Assert.Single(outcome.State.Clients);
        var workspace = WorkspaceId.FromNumber(3, new MonitorId(1));
        Assert.Equal(workspace, client.Workspace);
        Assert.Equal(new MonitorId(1), Assert.Single(outcome.State.Workspaces, candidate => candidate.Id == workspace).Monitor);
    }

    [Fact]
    public void RestoreDropsARememberedWindowWhenItClosesBeforeAdoption()
    {
        var core = new Core();
        core.Restore(new RecoverySnapshot(
        [
            new RecoveredClient(new WindowId(5), new WorkspaceId("4"), MonitorId.Primary, true, "Notepad", "Notes", 10),
        ]));
        core.Handle(new WindowClosed(new WindowId(5)));

        var outcome = core.Handle(new WindowAppeared(new WindowId(5), Area));

        Assert.Equal(WorkspaceId.Initial, Assert.Single(outcome.State.Clients).Workspace);
    }

    [Fact]
    public void MissingMonitorFallsBackWhenTheWindowIsSeenAgain()
    {
        var core = new Core();
        var left = Display(@"\\.\DISPLAY1", 0, 1920, 1080);
        core.Handle(new MonitorsChanged([left]));
        core.Restore(new RecoverySnapshot(
        [
            new RecoveredClient(new WindowId(5), new WorkspaceId("4"), new MonitorId(99), true, "Notepad", "Notes", 10),
        ]));

        var outcome = core.Handle(new WindowAppeared(new WindowId(5), left.WorkArea)
        {
            Traits = WindowTraits.Normal with { Class = "Notepad", Title = "Notes" },
        });

        var client = Assert.Single(outcome.State.Clients);
        Assert.Equal(new WorkspaceId("4"), client.Workspace);
        Assert.Equal(new MonitorId(0), Assert.Single(outcome.State.Workspaces, workspace => workspace.Id == client.Workspace).Monitor);
    }

    [Fact]
    public void AFloatingPinnedClientComesBackFloatingPinnedAndWhereItWas()
    {
        var prior = new Core(ConfigParser.Parse("windowrulev2 = float, class:^(stick)$\n"));
        prior.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Bounds = new Rectangle(0, 0, 400, 300),
            Traits = WindowTraits.Normal with { Class = "stick" },
        });
        prior.Handle(new Dispatch("pin"));
        var placed = Assert.Single(prior.Handle(new Dispatch("workspace", "2")).State.Clients).Bounds;
        Assert.True(RecoverySnapshot.TryParse(prior.Recovery().ToJson(), out var snapshot));

        var recorded = Assert.Single(snapshot.Clients);
        Assert.True(recorded.Floating);
        Assert.True(recorded.Pinned);
        Assert.Equal(placed, recorded.Bounds);

        var core = new Core();
        core.Restore(snapshot);
        var back = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Bounds = new Rectangle(5, 5, 50, 50),
            Traits = WindowTraits.Normal with { Class = "stick" },
        });

        var client = Assert.Single(back.State.Clients);
        Assert.True(client.Floating);
        Assert.True(client.Pinned);
        Assert.Equal(placed, client.Bounds);
        Assert.Equal(new WorkspaceId("2"), client.Workspace);
    }

    [Fact]
    public void AFullscreenClientComesBackFullscreen()
    {
        var prior = new Core();
        prior.Handle(new WindowAppeared(new WindowId(1), Area));
        prior.Handle(new WindowAppeared(new WindowId(2), Area));
        prior.Handle(new Dispatch("fullscreen", "1"));
        Assert.True(RecoverySnapshot.TryParse(prior.Recovery().ToJson(), out var snapshot));

        var core = new Core();
        core.Restore(snapshot);
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        var back = core.Handle(new WindowAppeared(new WindowId(2), Area));

        var client = Assert.Single(back.State.Clients, candidate => candidate.Window == new WindowId(2));
        Assert.Equal(FullscreenMode.Maximized, client.Fullscreen);
        Assert.Equal(FullscreenMode.Maximized, client.FullscreenClient);
        Assert.False(client.Floating);
        Assert.Equal(FullscreenMode.None, Assert.Single(back.State.Clients, candidate => candidate.Window == new WindowId(1)).Fullscreen);
    }

    [Fact]
    public void ASnapshotWithoutTheNewerFieldsStillParsesAndLeavesThemToTheRules()
    {
        const string json = """{"clients":[{"window":5,"workspace":"1","monitor":"\\\\.\\DISPLAY1","hidden":false,"class":"stick","title":"","process":10}]}""";

        Assert.True(RecoverySnapshot.TryParse(json, out var snapshot));
        var recorded = Assert.Single(snapshot.Clients);
        Assert.Null(recorded.Floating);
        Assert.Null(recorded.Bounds);
        Assert.Null(recorded.Pinned);
        Assert.Null(recorded.Fullscreen);

        var core = new Core(ConfigParser.Parse("windowrulev2 = float, class:^(stick)$\nwindowrulev2 = pin, class:^(stick)$\n"));
        core.Restore(snapshot);
        var back = core.Handle(new WindowAppeared(new WindowId(5), Area)
        {
            Bounds = new Rectangle(10, 20, 300, 200),
            Traits = WindowTraits.Normal with { Class = "stick" },
        });

        var client = Assert.Single(back.State.Clients);
        Assert.True(client.Floating);
        Assert.True(client.Pinned);
        Assert.Equal(new Rectangle(10, 20, 300, 200), client.Bounds);
    }

    private static Monitor Display(string name, int x, int width, int height) =>
        new(name, name, x, 0, width, height, 1, true, new Rectangle(x, 0, width, height));
}
