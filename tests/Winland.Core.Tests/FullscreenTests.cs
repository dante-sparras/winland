using System.Text.Json;
using Winland;

namespace Winland.Tests;

public class FullscreenTests
{
    private static readonly Rectangle MonitorBounds = new(0, 0, 1920, 1080);
    private static readonly Rectangle Work = new(0, 40, 1920, 1040);

    [Fact]
    public void FullscreenZeroAndTwoToggleTheMonitorAndRestoreTheSlot()
    {
        var core = Desk();
        var before = Appear(core, 2);
        var left = BoundsOf(before, 1);
        var right = BoundsOf(before, 2);

        var entered = core.Handle(new Dispatch("fullscreen", "0"));

        Assert.Equal(MonitorBounds, BoundsOf(entered, 2));
        Assert.Equal(left, BoundsOf(entered, 1));
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(entered, 2).Fullscreen);
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(entered, 2).FullscreenClient);
        Assert.Equal(FullscreenMode.None, ClientOf(entered, 1).Fullscreen);
        Assert.Contains(entered.Events, static item => item is FullscreenChanged { Entered: true });
        Assert.True(DecorationOf(entered, 2).Borderless);
        Assert.False(DecorationOf(entered, 1).Borderless);

        var again = core.Handle(new Dispatch("fullscreen", "2"));

        Assert.Equal(right, BoundsOf(again, 2));
        Assert.Equal(left, BoundsOf(again, 1));
        Assert.Equal(FullscreenMode.None, ClientOf(again, 2).Fullscreen);
        Assert.Contains(again.Events, static item => item is FullscreenChanged { Entered: false });
        Assert.False(DecorationOf(again, 2).Borderless);
    }

    [Fact]
    public void MaximizeFillsTheWorkAreaAndKeepsTheBorder()
    {
        var core = Desk();
        Appear(core, 2);
        var left = Bounds(core, 1);

        var entered = core.Handle(new Dispatch("fullscreen", "1"));

        Assert.Equal(Work, BoundsOf(entered, 2));
        Assert.Equal(left, BoundsOf(entered, 1));
        Assert.Equal(FullscreenMode.Maximized, ClientOf(entered, 2).Fullscreen);
        Assert.False(DecorationOf(entered, 2).Borderless);

        var named = core.Handle(new Dispatch("maximize"));

        Assert.Equal(left, BoundsOf(named, 1));
        Assert.Equal(FullscreenMode.None, ClientOf(named, 2).Fullscreen);
        var restored = BoundsOf(named, 2);
        core.Handle(new Dispatch("maximize", "set"));
        Assert.Equal(Work, Bounds(core, 2));
        core.Handle(new Dispatch("maximize", "unset"));
        Assert.Equal(restored, Bounds(core, 2));
        Assert.Equal(left, Bounds(core, 1));
    }

    [Fact]
    public void FullscreenStateZeroReturnsTheClientToItsSlot()
    {
        var core = Desk();
        var before = Appear(core, 2);
        var right = BoundsOf(before, 2);
        core.Handle(new Dispatch("fullscreen"));

        var back = core.Handle(new Dispatch("fullscreenstate", "0 0"));

        Assert.Equal(right, BoundsOf(back, 2));
        Assert.Equal(FullscreenMode.None, ClientOf(back, 2).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(back, 2).FullscreenClient);
    }

    [Fact]
    public void FullscreenStateKeepsAClientModeWithoutMovingTheClient()
    {
        var core = Desk();
        var before = Appear(core, 1);
        var slot = BoundsOf(before, 1);

        var outcome = core.Handle(new Dispatch("fullscreenstate", "-1 2"));

        Assert.Equal(slot, BoundsOf(outcome, 1));
        Assert.Equal(FullscreenMode.None, ClientOf(outcome, 1).Fullscreen);
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(outcome, 1).FullscreenClient);
        Assert.DoesNotContain(outcome.Events, static item => item is FullscreenChanged);
    }

    // -1 takes the current mode before the pair is compared, so naming the current
    // client mode with -1 is the same pair and turns both modes off, as in the pinned release.
    [Theory]
    [InlineData("1 0", "-1 0")]
    [InlineData("2 2", "-1 2")]
    [InlineData("1 2", "1 -1")]
    [InlineData("2 1", "-1 -1")]
    public void FullscreenStateKeepingAModeComparesTheCurrentMode(string set, string again)
    {
        var core = Desk();
        var before = Appear(core, 2);
        var slot = BoundsOf(before, 2);
        core.Handle(new Dispatch("fullscreenstate", set));

        var outcome = core.Handle(new Dispatch("fullscreenstate", again));

        Assert.Equal(FullscreenMode.None, ClientOf(outcome, 2).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(outcome, 2).FullscreenClient);
        Assert.Equal(slot, BoundsOf(outcome, 2));
    }

    [Fact]
    public void FullscreenStateKeepingTheInternalModeChangesOnlyTheClientMode()
    {
        var core = Desk();
        Appear(core, 2);
        core.Handle(new Dispatch("fullscreenstate", "1 0"));

        var outcome = core.Handle(new Dispatch("fullscreenstate", "-1 2"));

        Assert.Equal(FullscreenMode.Maximized, ClientOf(outcome, 2).Fullscreen);
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(outcome, 2).FullscreenClient);
    }

    [Fact]
    public void TheTreeStaysPutWhileFullscreenAndASplitDoesNotMoveTheNeighbor()
    {
        var core = Desk();
        Appear(core, 2);
        var left = Bounds(core, 1);
        core.Handle(new Dispatch("fullscreen"));

        var resized = core.Handle(new Dispatch("splitratio", "0.2"));

        Assert.Equal(MonitorBounds, BoundsOf(resized, 2));
        Assert.Equal(left, BoundsOf(resized, 1));
    }

    [Fact]
    public void ASecondCoveringClientReplacesTheFirst()
    {
        var core = Desk();
        Appear(core, 2);
        core.Handle(new Dispatch("fullscreen", "1 set"));
        core.Handle(new Dispatch("movefocus", "l"));

        var outcome = core.Handle(new Dispatch("fullscreen", "2 set"));

        Assert.Equal(MonitorBounds, BoundsOf(outcome, 1));
        Assert.Equal(FullscreenMode.None, ClientOf(outcome, 2).Fullscreen);
        Assert.NotEqual(MonitorBounds, BoundsOf(outcome, 2));
    }

    [Fact]
    public void RulesRequestFullscreenAndMaximize()
    {
        var core = new Core(ConfigParser.Parse("""
            windowrulev2 = fullscreen, class:^(game)$
            windowrulev2 = maximize, class:^(edit)$
            windowrulev2 = fullscreen_state 2 0, class:^(browser)$
            """));
        core.Handle(new MonitorsChanged([Screen()]));

        var game = core.Handle(new WindowAppeared(new WindowId(1), Work)
        {
            Traits = WindowTraits.Normal with { Class = "game" },
        });
        Assert.Equal(MonitorBounds, BoundsOf(game, 1));
        Assert.Contains(game.Events, static item => item is FullscreenChanged { Entered: true });

        var edit = core.Handle(new WindowAppeared(new WindowId(2), Work)
        {
            Traits = WindowTraits.Normal with { Class = "edit" },
        });
        Assert.Equal(Work, BoundsOf(edit, 2));
        Assert.Equal(FullscreenMode.Maximized, ClientOf(edit, 2).Fullscreen);

        var browser = core.Handle(new WindowAppeared(new WindowId(3), Work)
        {
            Traits = WindowTraits.Normal with { Class = "browser" },
        });
        Assert.Equal(MonitorBounds, BoundsOf(browser, 3));
        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(browser, 3).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(browser, 3).FullscreenClient);
    }

    [Fact]
    public void FullscreenOpacityAppliesOnlyToModeTwo()
    {
        var core = new Core(ConfigParser.Parse("decoration:fullscreen_opacity = 0.5\n"));
        core.Handle(new MonitorsChanged([Screen()]));
        core.Handle(new WindowAppeared(new WindowId(1), Work));

        var maximized = core.Handle(new Dispatch("fullscreen", "1"));
        Assert.Equal(255, DecorationOf(maximized, 1).Alpha);

        var fullscreen = core.Handle(new Dispatch("fullscreen", "2"));
        Assert.Equal(128, DecorationOf(fullscreen, 1).Alpha);
    }

    [Fact]
    public void ClientsReportTheModesAndTheWorkspace()
    {
        var core = Desk();
        Appear(core, 1);
        core.Handle(new Dispatch("fullscreenstate", "1 0"));

        using var clients = JsonDocument.Parse(core.Execute("clients").Text);
        var client = clients.RootElement[0];
        Assert.Equal(1, client.GetProperty("fullscreen").GetInt32());
        Assert.Equal(0, client.GetProperty("fullscreenClient").GetInt32());
        Assert.Equal("default", client.GetProperty("fullscreenHandler").GetString());

        using var workspaces = JsonDocument.Parse(core.Execute("workspaces").Text);
        Assert.True(workspaces.RootElement[0].GetProperty("hasfullscreen").GetBoolean());
    }

    [Fact]
    public void FullscreenWithoutAClientIsAnErrorAndABadActionDoesNothing()
    {
        var core = Desk();
        var missing = core.Execute("dispatch fullscreen");
        Assert.Equal("error: window not found", missing.Text);

        Appear(core, 1);
        var ignored = core.Handle(new Dispatch("fullscreen", "1 sideways"));
        Assert.Equal(FullscreenMode.None, ClientOf(ignored, 1).Fullscreen);
        Assert.DoesNotContain(ignored.Events, static item => item is FullscreenChanged);

        core.Handle(new Dispatch("fullscreen", "1 set"));
        var repeat = core.Handle(new Dispatch("fullscreen", "1 set"));
        Assert.Equal(FullscreenMode.Maximized, ClientOf(repeat, 1).Fullscreen);
        Assert.DoesNotContain(repeat.Events, static item => item is FullscreenChanged);
    }

    [Fact]
    public void AnExclusiveFullscreenWindowIsNotAdopted()
    {
        var core = Desk();
        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Work)
        {
            Bounds = MonitorBounds,
            Traits = WindowTraits.Normal with { ExclusiveFullscreen = true },
        });

        Assert.Empty(outcome.State.Clients);
    }

    [Fact]
    public void APopupThatCoversTheMonitorIsExclusive()
    {
        Assert.True(ExclusiveFullscreenWindow.Is(ExclusiveFullscreenWindow.Popup, MonitorBounds, MonitorBounds));
        Assert.False(ExclusiveFullscreenWindow.Is(ExclusiveFullscreenWindow.Popup | ExclusiveFullscreenWindow.Caption, MonitorBounds, MonitorBounds));
        Assert.False(ExclusiveFullscreenWindow.Is(ExclusiveFullscreenWindow.Popup, new Rectangle(10, 10, 100, 100), MonitorBounds));
    }

    [Fact]
    public void AFloatingClientReturnsToItsFloatAfterFullscreen()
    {
        var core = Desk();
        Appear(core, 1);
        var floated = core.Handle(new Dispatch("togglefloating"));
        var slot = BoundsOf(floated, 1);
        core.Handle(new Dispatch("fullscreen"));

        var back = core.Handle(new Dispatch("fullscreen"));

        Assert.Equal(slot, BoundsOf(back, 1));
        Assert.True(ClientOf(back, 1).Floating);
    }

    [Theory]
    [InlineData("movetoworkspace")]
    [InlineData("movetoworkspacesilent")]
    public void AFullscreenClientStaysFullscreenOnTheWorkspaceItMovesTo(string dispatcher)
    {
        var core = Desk();
        Appear(core, 2);
        core.Handle(new Dispatch("fullscreen", "1"));

        var moved = core.Handle(new Dispatch(dispatcher, "2"));

        var client = ClientOf(moved, 2);
        Assert.Equal(new WorkspaceId("2"), client.Workspace);
        Assert.Equal(FullscreenMode.Maximized, client.Fullscreen);
        Assert.Equal(FullscreenMode.Maximized, client.FullscreenClient);
        Assert.Equal(Work, Bounds(core, 2));
    }

    [Fact]
    public void AFullscreenClientMovedOntoAnotherReplacesIt()
    {
        var core = Desk();
        core.Handle(new WindowAppeared(new WindowId(1), Work));
        core.Handle(new Dispatch("fullscreen"));
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(new WindowAppeared(new WindowId(2), Work));
        core.Handle(new Dispatch("fullscreen"));

        var moved = core.Handle(new Dispatch("movetoworkspace", "1"));

        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(moved, 2).Fullscreen);
        Assert.Equal(FullscreenMode.None, ClientOf(moved, 1).Fullscreen);
        Assert.Equal(MonitorBounds, BoundsOf(moved, 2));
    }

    [Fact]
    public void AFullscreenClientStaysFullscreenOnTheMonitorItMovesTo()
    {
        var core = new Core();
        var right = new Monitor(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1, true, new Rectangle(1920, 0, 1280, 984));
        core.Handle(new MonitorsChanged([Screen(), right]));
        core.Handle(new WindowAppeared(new WindowId(1), Work));
        core.Handle(new Dispatch("fullscreen"));

        var moved = core.Handle(new Dispatch("movewindow", "mon:" + right.Name));

        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(moved, 1).Fullscreen);
        Assert.Equal(new Rectangle(1920, 0, 1280, 1024), Bounds(core, 1));
    }

    [Fact]
    public void ChangingTheLayoutOnReloadKeepsAFullscreenClient()
    {
        var core = Desk();
        Appear(core, 2);
        core.Handle(new Dispatch("fullscreen"));

        var reloaded = core.Handle(new Configure(ConfigParser.Parse("general:layout = master\n")));

        Assert.Equal(FullscreenMode.Fullscreen, ClientOf(reloaded, 2).Fullscreen);
        Assert.Equal(MonitorBounds, Bounds(core, 2));
    }

    private static Core Desk()
    {
        var core = new Core();
        core.Handle(new MonitorsChanged([Screen()]));
        return core;
    }

    private static Monitor Screen() =>
        new(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, Work);

    private static Outcome Appear(Core core, int count)
    {
        Outcome outcome = null!;
        for (var id = 1; id <= count; id++)
            outcome = core.Handle(new WindowAppeared(new WindowId(id), Work));
        return outcome;
    }

    private static Client ClientOf(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = ClientOf(outcome, window);
        return Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id).Bounds;
    }

    private static Rectangle Bounds(Core core, long window) =>
        ClientOf(core.Handle(new Dispatch("no_op")), window).Bounds;

    private static Decoration DecorationOf(Outcome outcome, long window)
    {
        var client = ClientOf(outcome, window);
        return Assert.Single(outcome.Decorations, candidate => candidate.Client == client.Id);
    }
}
