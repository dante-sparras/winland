namespace Winland.Application.Tests;

public class ScrollingLayoutTests
{
    private static readonly double[] DefaultColumnWidths = [0.333, 0.5, 0.667, 1.0];
    private static readonly double[] ParsedColumnWidths = [0.2, 0.4];
    private static readonly Rectangle Area = new(0, 0, 1000, 800);

    [Fact]
    public void TapeWiderThanTheMonitorShowsTheFittedColumnsAndOmitsTheRest()
    {
        var layout = Open();
        layout.Add(Window(1), Area, null, false, default, WorkspaceId.Initial, monitor: Area);
        layout.Add(Window(2), Area, null, false, default, WorkspaceId.Initial, monitor: Area);
        var placed = layout.Add(Window(3), Area, null, false, default, WorkspaceId.Initial, monitor: Area);

        Assert.DoesNotContain(placed, item => item.Client == new ClientId(1));
        Assert.Equal(new Rectangle(0, 0, 500, 800), Box(placed, 2));
        Assert.Equal(new Rectangle(500, 0, 500, 800), Box(placed, 3));
    }

    [Fact]
    public void OneColumnSpansTheMonitor()
    {
        var layout = Open();

        var placed = layout.Add(Window(1), Area, null, false, default, WorkspaceId.Initial, monitor: Area);

        Assert.Equal(Area, Box(placed, 1));
    }

    [Fact]
    public void ANarrowColumnIsCenteredWhenItDoesNotCoverTheMonitor()
    {
        var layout = Open();
        layout.Configure(ScrollingOptions.Defaults with { FullscreenOnOneColumn = false });

        var placed = layout.Add(Window(1), Area, null, false, default, WorkspaceId.Initial, monitor: Area);

        Assert.Equal(new Rectangle(250, 0, 500, 800), Box(placed, 1));
    }

    [Fact]
    public void LeftDirectionPlacesTheNewerColumnOnTheLeft()
    {
        var layout = Open();
        layout.Configure(ScrollingOptions.Defaults with { Direction = ScrollingDirection.Left });
        layout.Add(Window(1), Area, null, false, default, WorkspaceId.Initial, monitor: Area);

        var placed = layout.Add(Window(2), Area, null, false, default, WorkspaceId.Initial, monitor: Area);

        Assert.Equal(new Rectangle(500, 0, 500, 800), Box(placed, 1));
        Assert.Equal(new Rectangle(0, 0, 500, 800), Box(placed, 2));
    }

    [Fact]
    public void DownDirectionStacksColumnsAlongTheHeight()
    {
        var tall = new Rectangle(0, 0, 800, 1000);
        var layout = Open(tall);
        layout.Configure(ScrollingOptions.Defaults with { Direction = ScrollingDirection.Down });
        layout.Add(Window(1), tall, null, false, default, WorkspaceId.Initial, monitor: tall);

        var placed = layout.Add(Window(2), tall, null, false, default, WorkspaceId.Initial, monitor: tall);

        Assert.Equal(new Rectangle(0, 0, 800, 500), Box(placed, 1));
        Assert.Equal(new Rectangle(0, 500, 800, 500), Box(placed, 2));
    }

    [Fact]
    public void InnerGapsOpenBetweenColumns()
    {
        var layout = Open();
        layout.Apply(LayoutOptions.Unconfigured with { GapsIn = EdgeInsets.Uniform(10) });
        layout.Add(Window(1), Area, null, false, default, WorkspaceId.Initial, monitor: Area);

        var placed = layout.Add(Window(2), Area, null, false, default, WorkspaceId.Initial, monitor: Area);

        Assert.Equal(new Rectangle(0, 0, 490, 800), Box(placed, 1));
        Assert.Equal(new Rectangle(510, 0, 490, 800), Box(placed, 2));
    }

    [Fact]
    public void APartlyVisibleColumnKeepsItsTapePosition()
    {
        var layout = Tape();

        var placed = layout.LayoutMessage(Window(3), "move +100");

        Assert.Equal(new Rectangle(-400, 0, 500, 800), Box(placed, 1));
        Assert.Equal(new Rectangle(100, 0, 500, 800), Box(placed, 2));
        Assert.Equal(new Rectangle(600, 0, 500, 800), Box(placed, 3));
    }

    [Fact]
    public void ColumnResizeAbsoluteAndConfiguredWidths()
    {
        var layout = Tape();

        var absolute = layout.LayoutMessage(Window(3), "colresize 1");
        Assert.Equal(new Rectangle(0, 0, 1000, 800), Box(absolute, 3));
        Assert.DoesNotContain(absolute, item => item.Client == new ClientId(1));

        var fresh = Tape();
        var configured = fresh.LayoutMessage(Window(3), "colresize +conf");
        Assert.Equal(new Rectangle(333, 0, 667, 800), Box(configured, 3));

        var back = fresh.LayoutMessage(Window(3), "colresize -conf");
        Assert.Equal(500, Box(back, 3).Width);
    }

    [Fact]
    public void ColumnResizeAllAndRelative()
    {
        var layout = Tape();

        var all = layout.LayoutMessage(Window(3), "colresize all 0.25");
        Assert.Equal(250, Box(all, 1).Width);
        Assert.Equal(250, Box(all, 2).Width);
        Assert.Equal(250, Box(all, 3).Width);

        var grown = layout.LayoutMessage(Window(1), "colresize +0.25");
        Assert.Equal(500, Box(grown, 1).Width);
    }

    [Fact]
    public void FitAllSharesTheMonitor()
    {
        var layout = Tape();

        var placed = layout.LayoutMessage(Window(3), "fit all");

        Assert.Equal(new Rectangle(0, 0, 333, 800), Box(placed, 1));
        Assert.Equal(new Rectangle(333, 0, 334, 800), Box(placed, 2));
        Assert.Equal(new Rectangle(667, 0, 333, 800), Box(placed, 3));
    }

    [Fact]
    public void FocusWrapsToTheFirstColumnAndBringsItIntoView()
    {
        var layout = Tape();

        var placed = layout.LayoutMessage(Window(3), "focus l");

        Assert.Equal(Window(2), layout.LayoutMessageFocus);
        Assert.Equal(new Rectangle(0, 0, 500, 800), Box(placed, 2));
        Assert.DoesNotContain(placed, item => item.Client == new ClientId(1));

        placed = layout.LayoutMessage(Window(2), "focus l");

        Assert.Equal(Window(1), layout.LayoutMessageFocus);
        Assert.Equal(new Rectangle(0, 0, 500, 800), Box(placed, 1));
        Assert.Equal(new Rectangle(500, 0, 500, 800), Box(placed, 2));
        Assert.DoesNotContain(placed, item => item.Client == new ClientId(3));
    }

    [Fact]
    public void FocusAtTheEndWrapsWhenWrapFocusIsOn()
    {
        var layout = Tape();
        layout.LayoutMessage(Window(3), "focus l");
        layout.LayoutMessage(Window(2), "focus l");

        var placed = layout.LayoutMessage(Window(1), "focus l");

        Assert.Equal(Window(3), layout.LayoutMessageFocus);
        Assert.Equal(new Rectangle(500, 0, 500, 800), Box(placed, 3));
    }

    [Fact]
    public void InhibitedScrollKeepsTheViewport()
    {
        var layout = Tape();
        var before = layout.Visible();

        var held = layout.LayoutMessage(Window(3), "inhibit_scroll");
        var centered = layout.LayoutMessage(Window(3), "center");

        Assert.Null(layout.LayoutMessageError);
        Assert.DoesNotContain(before, item => item.Client == new ClientId(1));
        Assert.DoesNotContain(held, item => item.Client == new ClientId(1));
        Assert.Equal(Box(before, 2), Box(held, 2));
        Assert.Equal(Box(before, 3), Box(centered, 3));

        layout.LayoutMessage(Window(3), "inhibit_scroll 0");
        var moved = layout.LayoutMessage(Window(3), "center");
        Assert.NotEqual(Box(before, 3), Box(moved, 3));
    }

    [Fact]
    public void InhibitedScrollKeepsTheViewportOnAMove()
    {
        var layout = Tape();
        var before = layout.Visible();

        layout.LayoutMessage(Window(3), "inhibit_scroll");
        var moved = layout.LayoutMessage(Window(3), "move +100");

        Assert.DoesNotContain(moved, item => item.Client == new ClientId(1));
        Assert.Equal(Box(before, 2), Box(moved, 2));
    }

    [Fact]
    public void AWorkspaceFittedToAnotherMonitorShowsItsColumnsThere()
    {
        var layout = Open();
        layout.Add(Window(1), Area, null, false, default, WorkspaceId.Initial, monitor: Area);

        layout.Fit(new Rectangle(1920, 0, 1000, 800));
        var shown = layout.Visible();

        Assert.Equal(new Rectangle(1920, 0, 1000, 800), Box(shown, 1));
    }

    [Fact]
    public void ConsumeStacksTheNextColumnAndExpelSplitsItAgain()
    {
        var layout = Open();
        layout.Add(Window(1), Area, null, false, default, WorkspaceId.Initial, monitor: Area);
        layout.Add(Window(2), Area, null, false, default, WorkspaceId.Initial, monitor: Area);

        var stacked = layout.LayoutMessage(Window(1), "consume");

        Assert.Equal(new Rectangle(0, 0, 1000, 400), Box(stacked, 1));
        Assert.Equal(new Rectangle(0, 400, 1000, 400), Box(stacked, 2));

        var split = layout.LayoutMessage(Window(1), "expel");
        Assert.Equal(new Rectangle(0, 0, 500, 800), Box(split, 1));
        Assert.Equal(new Rectangle(500, 0, 500, 800), Box(split, 2));
    }

    [Fact]
    public void PromoteMovesTheFocusedClientIntoItsOwnColumn()
    {
        var layout = Open();
        layout.Add(Window(1), Area, null, false, default, WorkspaceId.Initial, monitor: Area);
        layout.Add(Window(2), Area, null, false, default, WorkspaceId.Initial, monitor: Area);
        layout.LayoutMessage(Window(1), "consume");

        var promoted = layout.LayoutMessage(Window(2), "promote");

        Assert.Equal(500, Box(promoted, 1).Width);
        Assert.Equal(500, Box(promoted, 2).Width);
        Assert.True(Box(promoted, 2).X >= Box(promoted, 1).X);
    }

    [Fact]
    public void SwapColExchangesNeighbors()
    {
        var layout = Tape();

        var placed = layout.LayoutMessage(Window(2), "swapcol r");

        Assert.Null(layout.LayoutMessageError);
        Assert.Equal(500, Box(placed, 2).Width);
        Assert.Equal(0, Box(placed, 3).X);
        Assert.Equal(500, Box(placed, 2).X);
    }

    [Fact]
    public void UnknownMessageIsRejected()
    {
        var layout = Tape();

        var placed = layout.LayoutMessage(Window(3), "nosuch");

        Assert.Empty(placed);
        Assert.Equal("error: no such layoutmsg for scrolling", layout.LayoutMessageError);
    }

    [Fact]
    public void FloatingClientKeepsItsBounds()
    {
        var layout = Open();
        layout.Add(Window(1), Area, null, false, default, WorkspaceId.Initial, monitor: Area);
        var bounds = new Rectangle(40, 50, 200, 120);

        var placed = layout.Add(Window(2), Area, null, true, bounds, WorkspaceId.Initial, monitor: Area);

        Assert.Equal(bounds, Box(placed, 2));
        Assert.Equal(Area, Assert.Single(layout.Placed()).Bounds);
    }

    [Fact]
    public void MoveColumnStepsTheViewportAndFitActiveFillsIt()
    {
        var layout = Tape();

        var next = layout.LayoutMessage(Window(2), "move +col");
        Assert.Equal(Window(3), layout.LayoutMessageFocus);
        Assert.Equal(new Rectangle(500, 0, 500, 800), Box(next, 3));

        var previous = layout.LayoutMessage(Window(3), "move -col");
        Assert.Equal(Window(2), layout.LayoutMessageFocus);
        Assert.Equal(new Rectangle(0, 0, 500, 800), Box(previous, 2));

        var filled = layout.LayoutMessage(Window(1), "fit active");
        Assert.Equal(new Rectangle(0, 0, 1000, 800), Box(filled, 1));
        Assert.DoesNotContain(filled, item => item.Client == new ClientId(2));
    }

    [Fact]
    public void ConsumeOrExpelJoinsALoneColumnWithItsNeighbor()
    {
        var layout = Open();
        layout.Add(Window(1), Area, null, false, default, WorkspaceId.Initial, monitor: Area);
        layout.Add(Window(2), Area, null, false, default, WorkspaceId.Initial, monitor: Area);

        var joined = layout.LayoutMessage(Window(1), "consume_or_expel next");

        Assert.Null(layout.LayoutMessageError);
        Assert.Equal(new Rectangle(0, 0, 1000, 400), Box(joined, 2));
        Assert.Equal(new Rectangle(0, 400, 1000, 400), Box(joined, 1));
    }

    [Fact]
    public void FollowFocusFitsAColumnThatIsAlreadyPartlyInView()
    {
        var layout = Tape();
        layout.LayoutMessage(Window(3), "move +450");
        layout.NoteFocus(Window(1));

        Assert.Equal(new Rectangle(0, 0, 500, 800), Box(layout.Visible(), 1));

        var steady = Tape();
        steady.Configure(ScrollingOptions.Defaults with { FollowFocus = false });
        steady.LayoutMessage(Window(3), "move +450");
        steady.NoteFocus(Window(1));
        Assert.Equal(-50, Box(steady.Visible(), 1).X);
    }

    [Fact]
    public void ScrollingOptionsUseTheSourceDefaults()
    {
        var options = ScrollingOptions.From(ConfigParser.Parse(""));

        Assert.True(options.FullscreenOnOneColumn);
        Assert.Equal(0.5, options.ColumnWidth);
        Assert.Equal(ScrollingFit.Fit, options.FocusFitMethod);
        Assert.True(options.FollowFocus);
        Assert.Equal(0.4, options.FollowMinVisible);
        Assert.Equal(DefaultColumnWidths, options.ExplicitColumnWidths);
        Assert.Equal(ScrollingDirection.Right, options.Direction);
        Assert.True(options.WrapFocus);
        Assert.True(options.WrapSwapCol);
    }

    [Fact]
    public void ScrollingOptionsParseAndClamp()
    {
        var options = ScrollingOptions.From(ConfigParser.Parse("""
            scrolling:fullscreen_on_one_column = false
            scrolling:column_width = 2
            scrolling:focus_fit_method = center
            scrolling:follow_focus = 0
            scrolling:follow_min_visible = 0.2
            scrolling:explicit_column_widths = 0.2, 0.4
            scrolling:direction = up
            scrolling:wrap_focus = false
            scrolling:wrap_swapcol = no
            """));

        Assert.False(options.FullscreenOnOneColumn);
        Assert.Equal(1, options.ColumnWidth);
        Assert.Equal(ScrollingFit.Center, options.FocusFitMethod);
        Assert.False(options.FollowFocus);
        Assert.Equal(0.2, options.FollowMinVisible);
        Assert.Equal(ParsedColumnWidths, options.ExplicitColumnWidths);
        Assert.Equal(ScrollingDirection.Up, options.Direction);
        Assert.False(options.WrapFocus);
        Assert.False(options.WrapSwapCol);

        var narrow = ScrollingOptions.From(ConfigParser.Parse("scrolling:column_width = 0.01\n"));
        Assert.Equal(0.1, narrow.ColumnWidth);
    }

    [Fact]
    public void CoreSelectsScrollingAndConcealsTheHiddenColumn()
    {
        var core = Configured("""
            general:layout = scrolling
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 0
            """);
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        var outcome = core.Handle(Appeared(3));

        Assert.Equal([Window(1)], outcome.Hide);
        Assert.DoesNotContain(outcome.Placements, placement => placement.Client == Client(outcome, 1).Id);
        Assert.Equal(new Rectangle(0, 0, 500, 800), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(500, 0, 500, 800), BoundsOf(outcome, 3));

        var focused = core.Handle(new Dispatch("layoutmsg", "focus l"));
        Assert.Equal(Window(2), focused.Focus);

        var rejected = core.Handle(new Dispatch("layoutmsg", "nosuch"));
        Assert.Equal("error: no such layoutmsg for scrolling", rejected.Error);
    }

    [Fact]
    public void ConcealedColumnIsHiddenAndReturnsWhenScrolledBack()
    {
        var core = Flat();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        var third = core.Handle(Appeared(3));

        Assert.Equal([Window(1)], third.Hide);
        Assert.Empty(third.Show);
        Assert.DoesNotContain(third.Placements, placement => placement.Client == new ClientId(1));

        var nearer = core.Handle(new Dispatch("layoutmsg", "focus l"));
        Assert.Equal(Window(2), nearer.Focus);
        Assert.Empty(nearer.Hide);
        Assert.Empty(nearer.Show);

        var back = core.Handle(new Dispatch("layoutmsg", "focus l"));

        Assert.Equal(Window(1), back.Focus);
        Assert.Equal([Window(1)], back.Show);
        Assert.Equal([Window(3)], back.Hide);
        Assert.Equal(new Rectangle(0, 0, 500, 800), BoundsOf(back, 1));
        Assert.Equal(new Rectangle(500, 0, 500, 800), BoundsOf(back, 2));
        Assert.DoesNotContain(back.Placements, placement => placement.Client == new ClientId(3));
    }

    [Fact]
    public void APartlyVisibleColumnStaysShownOnItsTapeRectangle()
    {
        var core = Flat();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));

        var moved = core.Handle(new Dispatch("layoutmsg", "move +100"));

        Assert.Equal(new Rectangle(-400, 0, 500, 800), BoundsOf(moved, 1));
        Assert.Equal(new Rectangle(100, 0, 500, 800), BoundsOf(moved, 2));
        Assert.Equal(new Rectangle(600, 0, 500, 800), BoundsOf(moved, 3));
        Assert.Equal([Window(1)], moved.Show);
        Assert.DoesNotContain(moved.Hide, window => window == Window(1));
    }

    [Fact]
    public void ReturningToTheWorkspaceShowsOnlyOverlappingColumns()
    {
        var core = Flat();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        var third = core.Handle(Appeared(3));
        Assert.Equal([Window(1)], third.Hide);
        core.Handle(new Dispatch("workspace", "2"));

        var back = core.Handle(new Dispatch("workspace", "1"));

        Assert.Equal(new WorkspaceId("1"), back.State.ActiveWorkspace);
        Assert.Equal([Window(2), Window(3)], back.Show);
        Assert.DoesNotContain(back.Show, window => window == Window(1));
        Assert.Equal(new Rectangle(0, 0, 500, 800), BoundsOf(back, 2));
        Assert.Equal(new Rectangle(500, 0, 500, 800), BoundsOf(back, 3));
        Assert.DoesNotContain(back.Placements, placement => placement.Client == Client(back, 1).Id);
    }

    [Fact]
    public void ScrollingWidthRuleSizesTheNewColumn()
    {
        var core = Configured("""
            general:layout = scrolling
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 0
            scrolling:fullscreen_on_one_column = false
            windowrulev2 = scrolling_width 0.25, class:^(app)$
            """);

        var outcome = core.Handle(new WindowAppeared(Window(1), Area)
        {
            Traits = WindowTraits.Normal with { Class = "app" },
        });

        Assert.Equal(new Rectangle(375, 0, 250, 800), BoundsOf(outcome, 1));
    }

    private static ScrollingLayout Open() => Open(Area);

    private static ScrollingLayout Open(Rectangle area)
    {
        var layout = new ScrollingLayout();
        layout.Apply(LayoutOptions.Unconfigured);
        layout.Fit(area);
        return layout;
    }

    private static ScrollingLayout Tape()
    {
        var layout = Open();
        layout.Add(Window(1), Area, null, false, default, WorkspaceId.Initial, monitor: Area);
        layout.Add(Window(2), Area, null, false, default, WorkspaceId.Initial, monitor: Area);
        layout.Add(Window(3), Area, null, false, default, WorkspaceId.Initial, monitor: Area);
        return layout;
    }

    private static WindowId Window(long id) => new(id);

    private static Rectangle Box(IReadOnlyList<Placement> placements, long window)
    {
        var placement = Assert.Single(placements, item => item.Client == new ClientId(window));
        return placement.Bounds;
    }

    [Fact]
    public void FocusThatScrollsTheStripPlacesTheWindows()
    {
        var core = Flat();
        core.Handle(Appeared(1));
        core.Execute("dispatch layoutmsg colresize 0.8");
        var opened = core.Handle(Appeared(2));
        Assert.True(BoundsOf(opened, 1).X < 0);

        var focused = core.Execute("dispatch movefocus l").Effect;

        Assert.Equal(0, BoundsOf(focused, 1).X);
        Assert.True(BoundsOf(focused, 2).X >= 800);
    }

    private static WindowManager Configured(string text)
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(text)));
        return core;
    }

    private static WindowManager Flat() => Configured("""
        general:layout = scrolling
        general:gaps_in = 0
        general:gaps_out = 0
        general:border_size = 0
        """);

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == Window(window));

    private static WindowAppeared Appeared(long window) => new(Window(window), Area);

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == Window(window));
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }
}
