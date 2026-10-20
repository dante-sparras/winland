using System.Text.Json;
using Winland;

namespace Winland.Tests;

public class SwallowTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);
    private const string Swallowing = "misc:enable_swallow = true\nmisc:swallow_regex = ^(kitty)$\n";

    [Fact]
    public void AChildOfTheTerminalHidesTheTerminal()
    {
        var core = WithTerminal(Swallowing);

        var opened = core.Handle(Child(2, parentProcess: 100));

        Assert.Contains(new WindowId(1), opened.Hide);
        var hidden = Assert.Single(opened.State.Hidden);
        Assert.Equal(new WindowId(1), hidden.Window);
        Assert.Equal(new WindowId(1), Client(opened, 2).Swallowing);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), Placement(opened, Client(opened, 2).Id));
        Assert.DoesNotContain(opened.Events, item => item is ClientClosed);
    }

    [Fact]
    public void ClosingTheChildShowsAndFocusesTheTerminalAgain()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(Child(2, parentProcess: 100));

        var closed = core.Handle(new WindowClosed(new WindowId(2)));

        Assert.Contains(new WindowId(1), closed.Show);
        Assert.Equal(new WindowId(1), closed.Focus);
        Assert.Empty(closed.State.Hidden);
        Assert.Null(Client(closed, 1).Swallowing);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), Placement(closed, Client(closed, 1).Id));
    }

    [Fact]
    public void ATerminalHiddenWhenTheDaemonDiesIsInTheRecoverySnapshot()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(Child(2, parentProcess: 100));

        var snapshot = core.Recovery();

        var terminal = Assert.Single(snapshot.Clients, client => client.Window == new WindowId(1));
        Assert.True(terminal.Hidden);
        Assert.False(Assert.Single(snapshot.Clients, client => client.Window == new WindowId(2)).Hidden);
        Assert.True(RecoverySnapshot.TryParse(snapshot.ToJson(), out var read));
        Assert.True(Assert.Single(read.Clients, client => client.Window == new WindowId(1)).Hidden);
    }

    [Fact]
    public void AfterACrashTheTerminalComesBackAndTheLiveChildSwallowsItAgain()
    {
        var before = WithTerminal(Swallowing);
        before.Handle(Child(2, parentProcess: 100));
        var snapshot = before.Recovery();

        var after = new Core(ConfigParser.Parse(Swallowing));
        after.Restore(snapshot);
        after.Handle(Terminal());
        var reopened = after.Handle(Child(2, parentProcess: 100));
        var closed = after.Handle(new WindowClosed(new WindowId(2)));

        Assert.Contains(new WindowId(1), reopened.Hide);
        Assert.Contains(new WindowId(1), closed.Show);
    }

    [Fact]
    public void ATerminalShownAgainByItselfIsHiddenAgain()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(Child(2, parentProcess: 100));

        var shown = core.Handle(Terminal());

        Assert.Equal([new WindowId(1)], shown.Hide);
        Assert.Single(shown.State.Hidden);
    }

    [Fact]
    public void ATerminalThatClosesWhileHiddenIsForgotten()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(Child(2, parentProcess: 100));

        core.Handle(new WindowClosed(new WindowId(1)));
        var closed = core.Handle(new WindowClosed(new WindowId(2)));

        Assert.Empty(closed.State.Clients);
        Assert.Empty(closed.Show);
    }

    [Theory]
    [InlineData("misc:swallow_regex = ^(kitty)$\n")]
    [InlineData("misc:enable_swallow = true\n")]
    [InlineData("misc:enable_swallow = true\nmisc:swallow_regex = ^(foot)$\n")]
    [InlineData("misc:enable_swallow = true\nmisc:swallow_regex = ^(kitty)$\nmisc:swallow_exception_regex = ^(vim.*)$\n")]
    public void NothingIsSwallowedWhenDisabledUnmatchedOrExcepted(string config)
    {
        var core = WithTerminal(config, title: "vim notes");

        var opened = core.Handle(Child(2, parentProcess: 100));

        Assert.Empty(opened.State.Hidden);
        Assert.DoesNotContain(new WindowId(1), opened.Hide);
    }

    [Fact]
    public void AWindowFromAnUnrelatedProcessDoesNotSwallow()
    {
        var core = WithTerminal(Swallowing);

        var opened = core.Handle(Child(2, parentProcess: 555));

        Assert.Empty(opened.State.Hidden);
    }

    [Fact]
    public void ASecondChildDoesNotSwallowATerminalAlreadySwallowed()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(Child(2, parentProcess: 100));

        var second = core.Handle(Child(3, parentProcess: 100));

        Assert.Null(Client(second, 3).Swallowing);
    }

    [Fact]
    public void ToggleSwallowShowsTheTerminalAndHidesItAgain()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(Child(2, parentProcess: 100));

        var shown = core.Handle(new Dispatch("toggleswallow"));
        var hidden = core.Handle(new Dispatch("toggleswallow"));

        Assert.Contains(new WindowId(1), shown.Show);
        Assert.Empty(shown.State.Hidden);
        Assert.Equal(new WindowId(1), Client(shown, 2).Swallowing);
        Assert.Contains(new WindowId(1), hidden.Hide);
        Assert.Single(hidden.State.Hidden);
    }

    [Fact]
    public void ToggleSwallowOnTheShownTerminalHidesItAndFocusesTheChild()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(Child(2, parentProcess: 100));
        core.Handle(new Dispatch("toggleswallow"));
        core.Handle(new Dispatch("focuswindow", "address:0x1"));

        var hidden = core.Handle(new Dispatch("toggleswallow"));

        Assert.Contains(new WindowId(1), hidden.Hide);
        Assert.Equal(new WindowId(2), hidden.Focus);
        using var active = JsonDocument.Parse(core.Execute("j/activewindow").Text);
        Assert.Equal("0x2", active.RootElement.GetProperty("address").GetString());
    }

    [Fact]
    public void TheWorkspacesQueryDoesNotCountTheSwallowedTerminal()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(Child(2, parentProcess: 100));

        using var workspaces = JsonDocument.Parse(core.Execute("j/workspaces").Text);

        Assert.Equal(1, workspaces.RootElement[0].GetProperty("windows").GetInt32());
    }

    [Fact]
    public void AWindowSelectorFindsTheSwallowedTerminal()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(Child(2, parentProcess: 100));

        var reply = core.Execute("dispatch killwindow address:0x1");

        Assert.Equal(100, reply.Effect.Kill?.ProcessId);
    }

    [Fact]
    public void AChildSwallowedInTurnGivesBackTheTerminalWhenItCloses()
    {
        var core = WithTerminal("misc:enable_swallow = true\nmisc:swallow_regex = ^(kitty|mpv)$\n");
        core.Handle(Child(2, parentProcess: 100));
        var chained = core.Handle(Child(3, parentProcess: 202));

        Assert.Equal(new WindowId(1), Client(chained, 2).Swallowing);
        Assert.Equal(new WindowId(2), Client(chained, 3).Swallowing);

        var closed = core.Handle(new WindowClosed(new WindowId(2)));

        Assert.Equal(2, closed.State.Clients.Count);
        Assert.DoesNotContain(closed.State.Hidden, client => client.Window == new WindowId(1));
        Assert.Contains(new WindowId(1), closed.Show);
    }

    [Fact]
    public void ClosingAChildAfterToggleSwallowLeavesTheTerminalWhereItIs()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(Child(2, parentProcess: 100));
        core.Handle(new Dispatch("toggleswallow"));

        var closed = core.Handle(new WindowClosed(new WindowId(2)));

        Assert.Single(closed.State.Clients);
        Assert.DoesNotContain(new WindowId(1), closed.Show);
    }

    [Fact]
    public void TheClientsQueryReportsTheSwallowedTerminal()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(Child(2, parentProcess: 100));

        var reply = core.Execute("j/clients");

        using var json = JsonDocument.Parse(reply.Text);
        var child = json.RootElement.EnumerateArray().Single(client => client.GetProperty("address").GetString() == "0x2");
        var terminal = json.RootElement.EnumerateArray().Single(client => client.GetProperty("address").GetString() == "0x1");
        Assert.Equal("0x1", child.GetProperty("swallowing").GetString());
        Assert.True(terminal.GetProperty("hidden").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingAGroupedChildBringsTheTerminalBackAsItsOwnTile(bool childShown)
    {
        var core = WithTerminal(Swallowing);
        core.Handle(Child(2, parentProcess: 100));
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(Unrelated(3));
        if (childShown)
            core.Handle(new Dispatch("changegroupactive", "b"));

        var closed = core.Handle(new WindowClosed(new WindowId(2)));

        Assert.Contains(new WindowId(1), closed.Show);
        Assert.Empty(closed.State.Hidden);
        Assert.Null(Client(closed, 1).Swallowing);
        Assert.Equal([new WindowId(3)], Assert.Single(closed.State.Groups));
        Assert.Contains(core.Recovery().Clients, client => client.Window == new WindowId(1) && !client.Hidden);
    }

    [Fact]
    public void AChildTakesTheTerminalsPlaceInItsGroupAndGivesItBackOnClose()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(Unrelated(3));
        core.Handle(new Dispatch("changegroupactive", "b"));

        var opened = core.Handle(Child(2, parentProcess: 100));

        Assert.Equal([new WindowId(2), new WindowId(3)], Assert.Single(opened.State.Groups));
        Assert.Contains(new WindowId(1), opened.Hide);
        Assert.Single(opened.State.Clients, client => client.Window == new WindowId(1));
        Assert.Contains(opened.State.Hidden, client => client.Window == new WindowId(1));
        Assert.Contains(opened.State.Hidden, client => client.Window == new WindowId(3));
        Assert.Equal(new WindowId(1), Client(opened, 2).Swallowing);

        var closed = core.Handle(new WindowClosed(new WindowId(2)));

        Assert.Equal([new WindowId(1), new WindowId(3)], Assert.Single(closed.State.Groups));
        Assert.Contains(new WindowId(1), closed.Show);
        Assert.Equal(new WindowId(1), closed.Focus);
        Assert.Single(closed.State.Hidden, client => client.Window == new WindowId(3));
        Assert.Contains(closed.State.Workspaces, workspace => workspace.Id == Client(closed, 3).Workspace);
    }

    [Fact]
    public void ToggleSwallowInAGroupSlotTradesPlacesWithTheTerminal()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(Unrelated(3));
        core.Handle(new Dispatch("changegroupactive", "b"));
        core.Handle(Child(2, parentProcess: 100));

        var revealed = core.Handle(new Dispatch("toggleswallow"));

        Assert.Equal([new WindowId(1), new WindowId(3)], Assert.Single(revealed.State.Groups));
        Assert.Contains(new WindowId(2), revealed.Hide);
        Assert.Contains(new WindowId(1), revealed.Show);
        Assert.Equal(new WindowId(1), revealed.Focus);
        Assert.Contains(revealed.State.Hidden, client => client.Window == new WindowId(2));
        Assert.Equal(new WindowId(1), Client(revealed, 2).Swallowing);

        var swallowed = core.Handle(new Dispatch("toggleswallow"));

        Assert.Equal([new WindowId(2), new WindowId(3)], Assert.Single(swallowed.State.Groups));
        Assert.Contains(new WindowId(1), swallowed.Hide);
        Assert.Equal(new WindowId(2), swallowed.Focus);
    }

    [Fact]
    public void TheTerminalClosingInTheSlotGivesItBackToTheChild()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(Unrelated(3));
        core.Handle(new Dispatch("changegroupactive", "b"));
        core.Handle(Child(2, parentProcess: 100));
        core.Handle(new Dispatch("toggleswallow"));

        var closed = core.Handle(new WindowClosed(new WindowId(1)));

        Assert.Equal([new WindowId(2), new WindowId(3)], Assert.Single(closed.State.Groups));
        Assert.Contains(new WindowId(2), closed.Show);
        Assert.Null(Client(closed, 2).Swallowing);
    }

    [Fact]
    public void AWorkspaceHoldingOnlyAChildsGroupIsKeptWhenTheChildMoves()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(Unrelated(3));
        core.Handle(new Dispatch("changegroupactive", "b"));
        core.Handle(Child(2, parentProcess: 100));

        var moved = core.Handle(new Dispatch("movetoworkspacesilent", "2"));

        Assert.Equal([new WindowId(2), new WindowId(3)], Assert.Single(moved.State.Groups));
        Assert.Equal(new WorkspaceId("2"), Client(moved, 3).Workspace);
        Assert.Equal(new WorkspaceId("2"), Client(moved, 1).Workspace);
    }

    [Fact]
    public void AFocusedChildClosingInItsGroupSlotOnAHiddenWorkspaceLeavesNoFocus()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(Unrelated(3));
        core.Handle(new Dispatch("changegroupactive", "b"));
        core.Handle(Child(2, parentProcess: 100));
        core.Handle(new Dispatch("movetoworkspacesilent", "2"));

        // No dispatcher focuses a hidden Client, but the close path must not keep one it finds focused.
        ActiveField.SetValue(core, new WindowId(2));
        core.Handle(new WindowClosed(new WindowId(2)));

        Assert.Null(ActiveField.GetValue(core));
    }

    private static readonly System.Reflection.FieldInfo ActiveField =
        typeof(Core).GetField("_active", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    [Fact]
    public void AChildTakingTheTerminalsGroupSlotKeepsItsOwnFullscreenRequest()
    {
        var core = WithTerminal(Swallowing + "windowrulev2 = fullscreen, class:^(mpv)$\n");
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(Unrelated(3));
        core.Handle(new Dispatch("changegroupactive", "b"));

        var opened = core.Handle(Child(2, parentProcess: 100));

        Assert.Equal([new WindowId(2), new WindowId(3)], Assert.Single(opened.State.Groups));
        Assert.Equal(FullscreenMode.Fullscreen, Client(opened, 2).Fullscreen);
        Assert.Equal(Area, Placement(opened, Client(opened, 2).Id));
    }

    [Fact]
    public void AChildThatSwallowsAStandaloneTerminalStillJoinsTheFocusedGroup()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(Unrelated(3));
        core.Handle(new Dispatch("togglegroup"));

        var opened = core.Handle(Child(2, parentProcess: 100));

        Assert.Equal([new WindowId(3), new WindowId(2)], Assert.Single(opened.State.Groups));
        Assert.Contains(new WindowId(1), opened.Hide);
        Assert.Equal(new WindowId(1), Client(opened, 2).Swallowing);
    }

    [Fact]
    public void AHiddenGroupMemberIsNotSwallowed()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(Unrelated(3));

        var opened = core.Handle(Child(2, parentProcess: 100));

        Assert.Null(Client(opened, 2).Swallowing);
        Assert.Contains(new WindowId(1), Assert.Single(opened.State.Groups));
        Assert.DoesNotContain(new WindowId(1), opened.Hide);
    }

    [Fact]
    public void ALockedGroupKeepsItsTerminalAndPlacesTheChildOnItsOwn()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(Unrelated(3));
        core.Handle(new Dispatch("changegroupactive", "b"));
        core.Handle(new Dispatch("lockactivegroup", "lock"));

        var opened = core.Handle(Child(2, parentProcess: 100));

        Assert.Null(Client(opened, 2).Swallowing);
        Assert.Equal([new WindowId(1), new WindowId(3)], Assert.Single(opened.State.Groups));
        Assert.DoesNotContain(new WindowId(1), opened.Hide);
        _ = Placement(opened, Client(opened, 2).Id);
    }

    [Fact]
    public void AChildJoinsAGroupedTerminalOnAnotherWorkspaceAndIsPlacedInItsTile()
    {
        var core = WithTerminal(Swallowing);
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(Unrelated(3));
        core.Handle(new Dispatch("changegroupactive", "b"));
        core.Handle(new Dispatch("movetoworkspacesilent", "2"));

        var opened = core.Handle(Child(2, parentProcess: 100));

        Assert.Equal([new WindowId(2), new WindowId(3)], Assert.Single(opened.State.Groups));
        Assert.Contains(new WindowId(1), opened.Hide);
        Assert.Equal(new WindowId(1), Client(opened, 2).Swallowing);
        Assert.Equal(new WorkspaceId("2"), Client(opened, 2).Workspace);
        Assert.Equal(new WorkspaceId("2"), Client(opened, 3).Workspace);
        Assert.Equal(new WorkspaceId("2"), opened.State.ActiveWorkspace);
        _ = Placement(opened, Client(opened, 2).Id);
        Assert.DoesNotContain(opened.Placements, placement => placement.Client == new ClientId(1));
    }

    [Fact]
    public void ASilentWorkspaceRuleDoesNotKeepAChildFromItsParentsGroup()
    {
        var core = WithTerminal(Swallowing + "windowrulev2 = workspace 2 silent, class:^(mpv)$\n");
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(Unrelated(3));
        core.Handle(new Dispatch("changegroupactive", "b"));

        var opened = core.Handle(Child(2, parentProcess: 100));

        Assert.Equal([new WindowId(2), new WindowId(3)], Assert.Single(opened.State.Groups));
        Assert.Contains(new WindowId(1), opened.Hide);
        Assert.Equal(new WindowId(1), Client(opened, 2).Swallowing);
        Assert.Equal(WorkspaceId.Initial, Client(opened, 2).Workspace);
        Assert.Equal(WorkspaceId.Initial, opened.State.ActiveWorkspace);
        Assert.DoesNotContain(opened.State.Workspaces, workspace => workspace.Id == new WorkspaceId("2"));
        _ = Placement(opened, Client(opened, 2).Id);
    }

    [Fact]
    public void AChildDoesNotSwallowATerminalInADeniedGroup()
    {
        var core = WithTerminal(Swallowing + "group:auto_group = false\n");
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(new Dispatch("denywindowfromgroup", "on"));

        var opened = core.Handle(Child(2, parentProcess: 100));

        Assert.DoesNotContain(new WindowId(1), opened.Hide);
        Assert.Null(Client(opened, 2).Swallowing);
        Assert.Empty(opened.State.Hidden);
    }

    [Fact]
    public void AnExceptionTitleEndingInANewlineDoesNotMatchAsIfTheNewlineWereNotThere()
    {
        var core = WithTerminal(Swallowing + "misc:swallow_exception_regex = vim\n", title: "vim\n");

        var opened = core.Handle(Child(2, parentProcess: 100));

        Assert.Contains(new WindowId(1), opened.Hide);
    }

    [Fact]
    public void SwallowKeywordsAreHonored()
    {
        var document = ConfigParser.Parse(Swallowing + "misc:swallow_exception_regex = ^(wev)$\n");

        Assert.Empty(document.Warnings);
    }

    private static Core WithTerminal(string config, string title = "")
    {
        var core = new Core(ConfigParser.Parse(config));
        core.Handle(Terminal(title));
        return core;
    }

    private static WindowAppeared Terminal(string title = "") =>
        new(new WindowId(1), Area)
        {
            ProcessId = 100,
            Traits = WindowTraits.Normal with { Class = "kitty", Title = title },
        };

    private static WindowAppeared Child(long window, int parentProcess) =>
        new(new WindowId(window), Area)
        {
            ProcessId = 200 + window,
            AncestorProcessIds = [150, parentProcess, 4],
            Traits = WindowTraits.Normal with { Class = "mpv" },
        };

    private static WindowAppeared Unrelated(long window) =>
        new(new WindowId(window), Area)
        {
            ProcessId = 500 + window,
            AncestorProcessIds = [4],
            Traits = WindowTraits.Normal with { Class = "foot" },
        };

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, client => client.Window == new WindowId(window));

    private static Rectangle Placement(Outcome outcome, ClientId id) =>
        Assert.Single(outcome.Placements, placement => placement.Client == id).Bounds;
}
