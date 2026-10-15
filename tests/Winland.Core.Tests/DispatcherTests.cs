using Winland;

namespace Winland.Tests;

public class DispatcherTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);
    private static readonly Rectangle Tall = new(0, 0, 1080, 1920);

    [Fact]
    public void MoveFocusReachesTheNeighborInEachDirection()
    {
        var core = TwoWindows();

        var left = core.Handle(new Dispatch("movefocus", "l"));

        Assert.Equal(new WindowId(1), left.Focus);
        Assert.Equal("movefocus", left.Dispatcher);
        Assert.Empty(left.Placements);

        var right = core.Handle(new Dispatch("movefocus", "right"));

        Assert.Equal(new WindowId(2), right.Focus);

        var stacked = ThreeWindows();
        Assert.Equal(new WindowId(2), FocusToward(stacked, "u"));
        Assert.Null(FocusToward(stacked, "r"));
        Assert.Equal(new WindowId(3), FocusToward(stacked, "d"));
        Assert.Equal(new WindowId(1), FocusToward(stacked, "l"));
        Assert.Null(FocusToward(stacked, "l"));
    }

    [Fact]
    public void MoveFocusFromTheLeftHalfPrefersTheUpperNeighbor()
    {
        var core = ThreeWindows();
        core.Handle(new Dispatch("movefocus", "l"));

        Assert.Equal(new WindowId(2), core.Handle(new Dispatch("movefocus", "r")).Focus);
    }

    [Fact]
    public void MoveFocusStopsWhenNothingLiesThatWay()
    {
        var core = TwoWindows();
        core.Handle(new Dispatch("movefocus", "l"));

        var outcome = core.Handle(new Dispatch("movefocus", "l"));

        Assert.Null(outcome.Focus);
        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("killactive")).Close);
    }

    [Fact]
    public void BoundMoveFocusRequestsFocusAndLaterDispatchersUseIt()
    {
        var core = Load("""
            bind = SUPER, H, movefocus, l
            bind = SUPER, Q, killactive
            """);
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");

        var focused = Press(core, "H");

        Assert.True(focused.Consumed);
        Assert.Equal("movefocus", focused.Dispatcher);
        Assert.Equal(new WindowId(1), focused.Focus);
        Assert.Equal(new WindowId(1), Press(core, "Q").Close);
    }

    [Fact]
    public void MoveFocusBecomesTheWindowNewClientsSplit()
    {
        var core = TwoWindows();
        core.Handle(new Dispatch("movefocus", "l"));

        var outcome = Appear(core, 3);

        Assert.Equal(new Rectangle(0, 0, 960, 540), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 540, 960, 540), BoundsOf(outcome, 3));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void MoveWindowSwapsTheActiveClientWithTheNeighbor()
    {
        var core = TwoWindows();

        var outcome = core.Handle(new Dispatch("movewindow", "l"));

        Assert.Equal("movewindow", outcome.Dispatcher);
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void MoveWindowUpSwapsAVerticalPair()
    {
        var core = ThreeWindows();

        var outcome = core.Handle(new Dispatch("movewindow", "up"));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(960, 540, 960, 540), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(960, 0, 960, 540), BoundsOf(outcome, 3));
    }

    [Fact]
    public void MoveWindowRightKeepsTheClientOnTheRightWhenTheSplitIsForcedLeft()
    {
        var core = Flat("");
        Appear(core, 1);
        Appear(core, 2);
        core.Handle(new Configure(ConfigParser.Parse("""
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 0
            dwindle:force_split = 1
            """)));

        var outcome = core.Handle(new Dispatch("movewindow", "r"));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void BoundMoveWindowRearrangesTheTree()
    {
        var core = Load("bind = SUPER, L, movewindow, l");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");

        var outcome = Press(core, "L");

        Assert.True(outcome.Consumed);
        Assert.Equal("movewindow", outcome.Dispatcher);
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void MoveWindowWithoutADirectionLeavesTheTreeAlone()
    {
        var core = TwoWindows();

        var outcome = core.Handle(new Dispatch("movewindow"));

        Assert.Empty(outcome.Placements);
        Assert.Equal(new WindowId(2), core.Handle(new Dispatch("killactive")).Close);
    }

    [Fact]
    public void MoveWindowSnapsAFloatingClientToTheWorkAreaEdge()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Wide)
        {
            Traits = WindowTraits.Normal with { Dialog = true },
            Bounds = new Rectangle(100, 100, 200, 150),
        });

        var right = core.Handle(new Dispatch("movewindow", "r"));
        Assert.Equal(new Rectangle(1720, 100, 200, 150), BoundsOf(right, 1));

        var down = core.Handle(new Dispatch("movewindow", "bottom"));
        Assert.Equal(new Rectangle(1720, 930, 200, 150), BoundsOf(down, 1));
    }

    [Fact]
    public void ResizeActiveChangesTheSplitRatioOnTheFreeAxis()
    {
        var sideBySide = TwoWindows();

        var wider = sideBySide.Handle(new Dispatch("resizeactive", "480 0"));

        Assert.Equal("resizeactive", wider.Dispatcher);
        Assert.Equal(new Rectangle(0, 0, 1440, 1080), BoundsOf(wider, 1));
        Assert.Equal(new Rectangle(1440, 0, 480, 1080), BoundsOf(wider, 2));

        var stacked = new Core();
        Appear(stacked, 1, Tall);
        Appear(stacked, 2, Tall);

        var taller = stacked.Handle(new Dispatch("resizeactive", "0 480"));

        Assert.Equal(new Rectangle(0, 0, 1080, 1440), BoundsOf(taller, 1));
        Assert.Equal(new Rectangle(0, 1440, 1080, 480), BoundsOf(taller, 2));
        Assert.Equal(new Rectangle(0, 0, 1080, 1440), BoundsOf(stacked.Handle(new Dispatch("resizeactive", "20 0")), 1));
    }

    [Fact]
    public void ResizeActiveOnAFloatingClientGrowsItFromTheCenter()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Wide)
        {
            Traits = WindowTraits.Normal with { Dialog = true },
            Bounds = new Rectangle(100, 100, 200, 150),
        });

        var outcome = core.Handle(new Dispatch("resizeactive", "20 10"));

        Assert.Equal(new Rectangle(90, 95, 220, 160), BoundsOf(outcome, 1));
    }

    [Fact]
    public void BoundResizeActiveChangesTheSplit()
    {
        var core = Load("bind = SUPER, H, resizeactive, 480 0");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");

        var outcome = Press(core, "H");

        Assert.True(outcome.Consumed);
        Assert.Equal(new Rectangle(0, 0, 1440, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(1440, 0, 480, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void ToggleFloatingLiftsTheActiveClientOutAndBack()
    {
        var core = TwoWindows();

        var floating = core.Handle(new Dispatch("togglefloating"));

        Assert.Equal("togglefloating", floating.Dispatcher);
        Assert.True(Client(floating, 2).Floating);
        Assert.False(Client(floating, 1).Floating);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), BoundsOf(floating, 1));
        Assert.Equal(new Rectangle(1120, 340, 640, 400), BoundsOf(floating, 2));

        var tiled = core.Handle(new Dispatch("togglefloating"));

        Assert.False(Client(tiled, 2).Floating);
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(tiled, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(tiled, 2));
    }

    [Fact]
    public void ToggleFloatingRecentersATiledClientAtItsLastFloatingSize()
    {
        var core = Flat("");
        core.Handle(new WindowAppeared(new WindowId(1), Wide)
        {
            Bounds = new Rectangle(100, 80, 800, 600),
        });

        var floated = core.Handle(new Dispatch("togglefloating"));

        Assert.True(Client(floated, 1).Floating);
        Assert.Equal(new Rectangle(560, 240, 800, 600), BoundsOf(floated, 1));

        var same = Flat("");
        same.Handle(new WindowAppeared(new WindowId(1), Wide)
        {
            Bounds = Wide,
        });

        var grown = same.Handle(new Dispatch("togglefloating"));

        Assert.Equal(new Rectangle(-10, -10, 1930, 1090), BoundsOf(grown, 1));
    }

    [Fact]
    public void BoundToggleFloatingSwitchesTheActiveClient()
    {
        var core = Load("bind = SUPER, Space, togglefloating");
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");

        var outcome = Press(core, "Space");

        Assert.True(outcome.Consumed);
        Assert.True(Client(outcome, 2).Floating);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), BoundsOf(outcome, 1));
    }

    [Fact]
    public void ToggleSplitDoesNothingUnlessTheSplitIsPreserved()
    {
        var core = Flat("");
        Appear(core, 1);
        Appear(core, 2);

        var outcome = core.Handle(new Dispatch("layoutmsg", "togglesplit"));

        Assert.Equal("layoutmsg", outcome.Dispatcher);
        Assert.Empty(outcome.Placements);
    }

    [Fact]
    public void ToggleSplitFlipsTheCurrentSplitWhenItIsPreserved()
    {
        var core = Flat("dwindle:preserve_split = true");
        Appear(core, 1);
        Appear(core, 2);

        var outcome = core.Handle(new Dispatch("layoutmsg", "togglesplit"));

        Assert.Equal("layoutmsg", outcome.Dispatcher);
        Assert.Equal(new Rectangle(0, 0, 1920, 540), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 540, 1920, 540), BoundsOf(outcome, 2));
    }

    [Fact]
    public void SwapSplitExchangesTheTwoHalves()
    {
        var core = TwoWindows();

        var outcome = core.Handle(new Dispatch("swapsplit"));

        Assert.Equal("swapsplit", outcome.Dispatcher);
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void RotateSplitTurnsTheCurrentSplit()
    {
        var core = Flat("dwindle:preserve_split = true");
        Appear(core, 1);
        Appear(core, 2);

        var clockwise = core.Handle(new Dispatch("rotatesplit", "90"));

        Assert.Equal(new Rectangle(0, 0, 1920, 540), BoundsOf(clockwise, 1));
        Assert.Equal(new Rectangle(0, 540, 1920, 540), BoundsOf(clockwise, 2));

        var back = core.Handle(new Dispatch("layout", "rotatesplit -90"));

        Assert.Equal("layout", back.Dispatcher);
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(back, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(back, 2));
    }

    [Fact]
    public void MoveToRootPromotesTheActiveClientAndKeepsItsSide()
    {
        var core = ThreeWindows();

        var stable = core.Handle(new Dispatch("movetoroot"));

        Assert.Equal(new Rectangle(0, 0, 960, 540), BoundsOf(stable, 2));
        Assert.Equal(new Rectangle(0, 540, 960, 540), BoundsOf(stable, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(stable, 3));
    }

    [Fact]
    public void MoveToRootReadsAWindowSelectorBeforeUnstable()
    {
        var stable = ThreeWindows();

        var kept = stable.Handle(new Dispatch("layoutmsg", "movetoroot unstable"));

        Assert.Equal(new Rectangle(0, 0, 960, 540), BoundsOf(kept, 2));
        Assert.Equal(new Rectangle(0, 540, 960, 540), BoundsOf(kept, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(kept, 3));

        var selected = Flat("");
        Appear(selected, 1);
        selected.Handle(new WindowAppeared(new WindowId(2), Wide)
        {
            Traits = WindowTraits.Normal with { Class = "Top" },
        });
        Appear(selected, 3);

        var moved = selected.Handle(new Dispatch("layoutmsg", "movetoroot class:Top unstable"));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(moved, 2));
        Assert.Equal(new Rectangle(960, 0, 960, 540), BoundsOf(moved, 1));
        Assert.Equal(new Rectangle(960, 540, 960, 540), BoundsOf(moved, 3));
    }

    [Fact]
    public void MoveToRootUnstableSwapsTheRootSides()
    {
        var core = ThreeWindows();

        var outcome = core.Handle(new Dispatch("layoutmsg", "movetoroot active unstable"));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 3));
        Assert.Equal(new Rectangle(960, 0, 960, 540), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(960, 540, 960, 540), BoundsOf(outcome, 1));
    }

    [Fact]
    public void SplitRatioAddsADeltaOrSetsAnExactValue()
    {
        var core = TwoWindows();

        var delta = core.Handle(new Dispatch("splitratio", "0.2"));

        Assert.Equal(new Rectangle(0, 0, 1152, 1080), BoundsOf(delta, 1));
        Assert.Equal(new Rectangle(1152, 0, 768, 1080), BoundsOf(delta, 2));

        var exact = core.Handle(new Dispatch("layoutmsg", "splitratio 1 exact"));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(exact, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(exact, 2));
    }

    [Fact]
    public void SplitRatioClampsToTheAllowedRange()
    {
        var core = TwoWindows();

        var outcome = core.Handle(new Dispatch("splitratio", "5 exact"));

        Assert.Equal(new Rectangle(0, 0, 1824, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(1824, 0, 96, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void BoundLayoutMessageRuns()
    {
        var core = new Core();
        var document = ConfigParser.Parse("""
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 0
            dwindle:preserve_split = true
            bind = SUPER, T, layoutmsg, togglesplit
            """);
        core.Handle(new Configure(document));
        core.Handle(new ConfigLoaded(document));
        Appear(core, 1);
        Appear(core, 2);
        Press(core, "Super_L");

        var outcome = Press(core, "T");

        Assert.True(outcome.Consumed);
        Assert.Equal("layoutmsg", outcome.Dispatcher);
        Assert.Equal(new Rectangle(0, 0, 1920, 540), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 540, 1920, 540), BoundsOf(outcome, 2));
    }

    [Fact]
    public void PreselectAndPseudoRunFromABindAndThePipe()
    {
        var core = new Core();
        var document = ConfigParser.Parse("""
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 0
            dwindle:preserve_split = true
            bind = SUPER, P, pseudo
            bind = SUPER, D, layoutmsg, preselect l
            """);
        core.Handle(new Configure(document));
        core.Handle(new ConfigLoaded(document));
        core.Handle(new WindowAppeared(new WindowId(1), Tall)
        {
            Bounds = new Rectangle(0, 0, 800, 600),
        });
        Press(core, "Super_L");

        var fromPipe = core.Execute("dispatch pseudo");

        Assert.Equal("ok", fromPipe.Text);
        Assert.Equal("pseudo", fromPipe.Effect.Dispatcher);
        Assert.Equal(new Rectangle(140, 660, 800, 600), BoundsOf(fromPipe.Effect, 1));

        var fromBind = Press(core, "P");

        Assert.True(fromBind.Consumed);
        Assert.Equal("pseudo", fromBind.Dispatcher);
        Assert.Equal(Tall, BoundsOf(fromBind, 1));

        var directed = core.Execute("dispatch layoutmsg preselect l");
        Assert.Equal("ok", directed.Text);
        var opened = Appear(core, 2, Tall);

        Assert.Equal(new Rectangle(540, 0, 540, 1920), BoundsOf(opened, 1));
        Assert.Equal(new Rectangle(0, 0, 540, 1920), BoundsOf(opened, 2));

        Press(core, "D");
        var again = Appear(core, 3, Tall);

        Assert.Equal(new Rectangle(0, 0, 270, 1920), BoundsOf(again, 3));
        Assert.Equal(new Rectangle(270, 0, 270, 1920), BoundsOf(again, 2));
        Assert.Equal(new Rectangle(540, 0, 540, 1920), BoundsOf(again, 1));
    }

    [Fact]
    public void DispatchRunsKillActiveOnTheSamePath()
    {
        var core = TwoWindows();

        var outcome = core.Handle(new Dispatch("killactive"));

        Assert.Equal("killactive", outcome.Dispatcher);
        Assert.Equal(new WindowId(2), outcome.Close);
        Assert.Contains(outcome.State.Clients, client => client.Window == new WindowId(2));
        Assert.Null(outcome.Focus);
    }

    [Fact]
    public void UnknownDispatcherIsReportedWithoutAnEffect()
    {
        var core = TwoWindows();

        var outcome = core.Handle(new Dispatch("nosuch", "notepad"));

        Assert.Equal("nosuch", outcome.Dispatcher);
        Assert.False(outcome.Consumed);
        Assert.Null(outcome.Close);
        Assert.Null(outcome.Focus);
        Assert.Empty(outcome.Placements);
        Assert.Equal(2, outcome.State.Clients.Count);
    }

    private static WindowId? FocusToward(Core core, string direction) =>
        core.Handle(new Dispatch("movefocus", direction)).Focus;

    private static Core TwoWindows()
    {
        var core = new Core();
        Appear(core, 1);
        Appear(core, 2);
        return core;
    }

    private static Core ThreeWindows()
    {
        var core = TwoWindows();
        Appear(core, 3);
        return core;
    }

    private static Core Flat(string extra)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + extra)));
        return core;
    }

    private static Core Load(string text)
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(text)));
        return core;
    }

    private static Outcome Appear(Core core, long window, Rectangle? area = null) =>
        core.Handle(new WindowAppeared(new WindowId(window), area ?? Wide));

    private static Outcome Press(Core core, string key) =>
        core.Handle(new KeyPressed(key));

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Client(outcome, window);
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }
}
