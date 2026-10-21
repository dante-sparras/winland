using Winland.Core;

namespace Winland.Core.Tests;

public class MonocleLayoutTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void FactorySelectsMonocleAndLeavesOtherNamesOnDwindle()
    {
        var selected = LayoutFactory.Fallback("monocle");
        var spaced = LayoutFactory.Fallback("  Monocle  ");
        var missing = LayoutFactory.Fallback(null);
        var unknown = LayoutFactory.Fallback("nope");

        Assert.IsType<MonocleLayout>(selected.Layout);
        Assert.Null(selected.Warning);
        Assert.IsType<MonocleLayout>(spaced.Layout);
        Assert.Null(spaced.Warning);
        Assert.NotSame(selected.Layout, spaced.Layout);
        Assert.IsType<DwindleLayout>(missing.Layout);
        Assert.Null(missing.Warning);
        Assert.IsType<DwindleLayout>(unknown.Layout);
        Assert.Equal("Layout 'nope' is not available; using dwindle.", unknown.Warning);
        Assert.IsType<MonocleLayout>(LayoutFactory.Create(ConfigParser.Parse("general:layout = monocle\n")));
    }

    [Fact]
    public void ANewTiledClientFillsTheAreaAndConcealsThePreviousOne()
    {
        var core = Open();
        core.Handle(Appeared(1));

        var second = core.Handle(Appeared(2));

        Assert.Equal(Area, BoundsOf(second, 2));
        Assert.Equal([new WindowId(1)], second.Hide);
        Assert.Empty(second.Show);
        Assert.DoesNotContain(second.Placements, placement => placement.Client == Client(second, 1).Id);
        Assert.False(Client(second, 1).Floating);
        Assert.False(Client(second, 2).Floating);
        Assert.Equal(new WindowId(2), core.Handle(new Dispatch("killactive")).Close);

        var third = core.Handle(Appeared(3));

        Assert.Equal(Area, BoundsOf(third, 3));
        Assert.Equal([new WindowId(2)], third.Hide);
        Assert.Equal(3, third.State.Clients.Count);
    }

    [Fact]
    public void GapsAndBorderInsetTheShownClient()
    {
        var core = Open("""
            general:gaps_in = 12
            general:gaps_out = 20
            general:border_size = 4
            """);
        core.Handle(Appeared(1));

        var second = core.Handle(Appeared(2));

        Assert.Equal(new Rectangle(24, 24, 1872, 1032), BoundsOf(second, 2));

        var back = core.Handle(new Dispatch("layoutmsg", "cycleprev"));

        Assert.Equal(new Rectangle(24, 24, 1872, 1032), BoundsOf(back, 1));
    }

    [Fact]
    public void CycleNextAndCyclePrevChangeTheShownClientAndWrap()
    {
        var core = Open();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));

        var previous = core.Handle(new Dispatch("layoutmsg", "cycleprev"));

        Assert.Equal(new WindowId(2), previous.Focus);
        Assert.Equal(Area, BoundsOf(previous, 2));
        Assert.Equal([new WindowId(3)], previous.Hide);
        Assert.Equal([new WindowId(2)], previous.Show);

        var next = core.Handle(new Dispatch("layoutmsg", "cyclenext"));

        Assert.Equal(new WindowId(3), next.Focus);
        Assert.Equal([new WindowId(2)], next.Hide);
        Assert.Equal([new WindowId(3)], next.Show);

        var wrapped = core.Handle(new Dispatch("layoutmsg", "cyclenext"));

        Assert.Equal(new WindowId(1), wrapped.Focus);
        Assert.Equal(Area, BoundsOf(wrapped, 1));
        Assert.Equal([new WindowId(3)], wrapped.Hide);
        Assert.Equal([new WindowId(1)], wrapped.Show);

        var around = core.Handle(new Dispatch("layoutmsg", "cycleprev"));

        Assert.Equal(new WindowId(3), around.Focus);
        Assert.Equal(new WindowId(3), core.Handle(new Dispatch("killactive")).Close);
    }

    [Fact]
    public void TrailingTiledAndFloatingWordsDoNotFilterTheCycle()
    {
        var core = Open();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        var floated = core.Handle(new WindowAppeared(new WindowId(9), Area)
        {
            Traits = WindowTraits.Normal with { Dialog = true },
            Bounds = new Rectangle(40, 50, 300, 200),
        });

        Assert.Equal(NewFloat.Centered(Area, new Rectangle(40, 50, 300, 200)), BoundsOf(floated, 9));
        Assert.True(Client(floated, 9).Floating);
        Assert.DoesNotContain(floated.Hide, window => window == new WindowId(2));

        var next = core.Handle(new Dispatch("layoutmsg", "cyclenext tiled"));

        Assert.Equal(new WindowId(1), next.Focus);
        Assert.Equal(Area, BoundsOf(next, 1));
        Assert.Equal([new WindowId(2)], next.Hide);
        Assert.DoesNotContain(next.Hide, window => window == new WindowId(9));
        Assert.True(Client(next, 9).Floating);

        var previous = core.Handle(new Dispatch("layoutmsg", "cycleprev floating"));

        Assert.Equal(new WindowId(2), previous.Focus);
        Assert.DoesNotContain(previous.Hide, window => window == new WindowId(9));
    }

    [Fact]
    public void UnknownLayoutMessageIsRejected()
    {
        var core = Open();
        core.Handle(Appeared(1));

        var unknown = core.Handle(new Dispatch("layoutmsg", "swapsplit"));
        var missing = core.Handle(new Dispatch("layoutmsg", ""));

        Assert.Equal("error: bad layoutmsg 'swapsplit'", unknown.Error);
        Assert.Empty(unknown.Placements);
        Assert.Equal("error: bad layoutmsg", missing.Error);
        Assert.Null(core.Handle(new Dispatch("layoutmsg", "cyclenext")).Error);
    }

    [Fact]
    public void MoveFocusCyclesLikeTheLayoutMessages()
    {
        var core = Open();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));

        var back = core.Handle(new Dispatch("movefocus", "l"));

        Assert.Equal(new WindowId(2), back.Focus);
        Assert.Equal(Area, BoundsOf(back, 2));
        Assert.Equal([new WindowId(3)], back.Hide);
        Assert.Equal([new WindowId(2)], back.Show);

        var forward = core.Handle(new Dispatch("movefocus", "down"));

        Assert.Equal(new WindowId(3), forward.Focus);
        Assert.Null(core.Handle(new Dispatch("movefocus", "nope")).Focus);
        Assert.Equal(new WindowId(3), core.Handle(new Dispatch("killactive")).Close);
    }

    [Fact]
    public void MoveIntoGroupWithNoGroupBesideLeavesTheShownClient()
    {
        var core = Open("group:auto_group = false\n");
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("movefocus", "l")).Focus);

        core.Handle(new Dispatch("moveintogroup", "r"));
        var next = core.Handle(new Dispatch("layoutmsg", "cyclenext"));

        Assert.Equal(new WindowId(2), next.Focus);
    }

    [Fact]
    public void MoveFocusThatCyclesGroupsFirstStepsOnceOutOfAGroup()
    {
        var core = Open("group:auto_group = false\nbinds:movefocus_cycles_groupfirst = true\n");
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(Appeared(3));
        Assert.Equal(new WindowId(2), core.Handle(new Dispatch("focuswindow", "address:0x2")).Focus);

        var left = core.Handle(new Dispatch("movefocus", "l"));

        Assert.Equal(new WindowId(1), left.Focus);
    }

    [Fact]
    public void ClosingTheShownClientShowsTheOneFocusedLast()
    {
        var core = Open();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));

        var closed = core.Handle(new WindowClosed(new WindowId(3)));

        Assert.Equal(new WindowId(2), closed.Focus);
        Assert.Equal(Area, BoundsOf(closed, 2));
        Assert.Equal([new WindowId(2)], closed.Show);
        Assert.DoesNotContain(closed.State.Clients, client => client.Window == new WindowId(3));

        var shown = core.Handle(new Dispatch("layoutmsg", "cyclenext"));
        var first = core.Handle(new WindowClosed(new WindowId(1)));

        Assert.Equal(new WindowId(1), shown.Focus);
        Assert.Equal(new WindowId(2), first.Focus);
        Assert.Equal(Area, BoundsOf(first, 2));
    }

    [Fact]
    public void MinimizingTheShownClientShowsTheNextOne()
    {
        var core = Open();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        var minimized = core.Handle(new WindowMinimized(new WindowId(2)));

        Assert.Equal(new WindowId(1), minimized.Focus);
        Assert.Equal(Area, BoundsOf(minimized, 1));
        Assert.Equal([new WindowId(1)], minimized.Show);
        Assert.True(Client(minimized, 2).Minimized);
        Assert.DoesNotContain(minimized.Placements, placement => placement.Client == Client(minimized, 2).Id);

        var restored = core.Handle(new WindowRestored(new WindowId(2)));

        Assert.Equal(Area, BoundsOf(restored, 2));
        Assert.Equal([new WindowId(1)], restored.Hide);
        Assert.False(Client(restored, 2).Minimized);
    }

    [Fact]
    public void FloatingClientsStayFloating()
    {
        var core = Open();
        core.Handle(Appeared(1));
        var bounds = new Rectangle(40, 50, 300, 200);
        var floated = core.Handle(new WindowAppeared(new WindowId(9), Area)
        {
            Traits = WindowTraits.Normal with { Dialog = true },
            Bounds = bounds,
        });

        Assert.Equal(NewFloat.Centered(Area, bounds), BoundsOf(floated, 9));
        Assert.True(Client(floated, 9).Floating);
        Assert.False(Client(floated, 1).Floating);
        Assert.DoesNotContain(floated.Hide, window => window == new WindowId(1));

        var moved = core.Handle(new Dispatch("movewindow", "l"));

        Assert.Equal(NewFloat.Centered(Area, bounds) with { X = 0 }, BoundsOf(moved, 9));
        Assert.True(Client(moved, 9).Floating);
        Assert.Empty(moved.Hide);

        var tiled = core.Handle(new Dispatch("togglefloating"));

        Assert.Equal(Area, BoundsOf(tiled, 9));
        Assert.False(Client(tiled, 9).Floating);
        Assert.Equal([new WindowId(1)], tiled.Hide);

        var again = core.Handle(new Dispatch("togglefloating"));

        Assert.Equal(new Rectangle(810, 440, 300, 200), BoundsOf(again, 9));
        Assert.True(Client(again, 9).Floating);
        Assert.Equal(Area, BoundsOf(again, 1));
        Assert.Equal([new WindowId(1)], again.Show);
    }

    [Fact]
    public void FloatingTheShownClientShowsTheTileItUncovers()
    {
        var core = Open();
        core.Handle(Appeared(1));
        core.Handle(Appeared(9));

        var reply = core.Execute("dispatch setfloating active");

        Assert.Equal("ok", reply.Text);
        Assert.Equal([new WindowId(1)], reply.Effect.Show);
    }

    [Fact]
    public void EachWorkspaceKeepsItsOwnShownClient()
    {
        var core = Open();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(Appeared(3));
        core.Handle(Appeared(4));
        var cycled = core.Handle(new Dispatch("layoutmsg", "cycleprev"));

        Assert.Equal(new WindowId(3), cycled.Focus);
        Assert.Equal(new WorkspaceId("2"), cycled.State.ActiveWorkspace);

        var back = core.Handle(new Dispatch("workspace", "1"));

        Assert.Equal(new WorkspaceId("1"), back.State.ActiveWorkspace);
        Assert.Equal(Area, BoundsOf(back, 2));
        Assert.Equal([new WindowId(2)], back.Show);
        Assert.DoesNotContain(back.Show, window => window == new WindowId(1));
        Assert.DoesNotContain(back.Placements, placement => placement.Client == Client(back, 1).Id);

        var there = core.Handle(new Dispatch("workspace", "2"));

        Assert.Equal(Area, BoundsOf(there, 3));
        Assert.Equal([new WindowId(3)], there.Show);
        Assert.DoesNotContain(there.Show, window => window == new WindowId(4));
    }

    private const string Flat =
        "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\ngeneral:layout = monocle\n";

    private static WindowManager Open(string extra = "")
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(Flat + extra)));
        return core;
    }

    private static WindowAppeared Appeared(long window) =>
        new(new WindowId(window), Area);

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Client(outcome, window);
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }
}
