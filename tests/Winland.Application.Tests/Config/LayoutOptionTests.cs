namespace Winland.Application.Tests;

public class LayoutOptionTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);
    private static readonly Rectangle Tall = new(0, 0, 1080, 1920);

    [Fact]
    public void InnerGapsShrinkClientsThatDoNotTouchThatEdge()
    {
        var core = Configured("""
            general:gaps_in = 10
            general:gaps_out = 0
            general:border_size = 0
            """);

        core.Handle(Appeared(1, Wide));
        var outcome = core.Handle(Appeared(2, Wide));

        Assert.Equal(new Rectangle(0, 0, 950, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(970, 0, 950, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void GapSidesFollowTheSameCountRulesAsAMargin()
    {
        var horizontal = Configured("""
            general:gaps_in = 8 4
            general:gaps_out = 0
            general:border_size = 0
            """);
        horizontal.Handle(Appeared(1, Wide));
        var sideBySide = horizontal.Handle(Appeared(2, Wide));

        Assert.Equal(new Rectangle(0, 0, 956, 1080), BoundsOf(sideBySide, 1));
        Assert.Equal(new Rectangle(964, 0, 956, 1080), BoundsOf(sideBySide, 2));

        var vertical = Configured("""
            general:gaps_in = 10 0 30 0
            general:gaps_out = 0
            general:border_size = 0
            """);
        vertical.Handle(Appeared(1, Tall));
        var stacked = vertical.Handle(Appeared(2, Tall));

        Assert.Equal(new Rectangle(0, 0, 1080, 930), BoundsOf(stacked, 1));
        Assert.Equal(new Rectangle(0, 970, 1080, 950), BoundsOf(stacked, 2));
    }

    [Fact]
    public void OuterGapsInsetTheTilingArea()
    {
        var core = Configured("""
            general:gaps_in = 0
            general:gaps_out = 10 30 50 70
            general:border_size = 0
            """);

        var outcome = core.Handle(Appeared(1, Wide));

        Assert.Equal(new Rectangle(70, 10, 1820, 1020), BoundsOf(outcome, 1));
    }

    [Fact]
    public void BorderSizeReservesSpaceOnEverySide()
    {
        var core = Configured("""
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 2
            """);

        var outcome = core.Handle(Appeared(1, Wide));

        Assert.Equal(new Rectangle(2, 2, 1916, 1076), BoundsOf(outcome, 1));
    }

    [Fact]
    public void BorderSizeClampsToItsLimit()
    {
        var core = Configured("""
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 50
            """);

        var outcome = core.Handle(Appeared(1, Wide));

        Assert.Equal(new Rectangle(20, 20, 1880, 1040), BoundsOf(outcome, 1));
    }

    [Fact]
    public void OmittedOptionsUseTheDocumentedDefaults()
    {
        var core = Configured("");

        var outcome = core.Handle(Appeared(1, Wide));

        Assert.Equal(new Rectangle(10, 10, 1900, 1060), BoundsOf(outcome, 1));
    }

    [Fact]
    public void LatestOptionWinsAndALaterConfigStartsOver()
    {
        var core = Configured("""
            general:gaps_in = 0
            general:gaps_out = 10
            general:gaps_out = 40
            general:border_size = 0
            """);
        var first = core.Handle(Appeared(1, Wide));
        Assert.Equal(new Rectangle(40, 40, 1840, 1000), BoundsOf(first, 1));

        var replaced = core.Handle(new Configure(ConfigParser.Parse("""
            general:gaps_in = 0
            general:border_size = 0
            """)));

        Assert.Equal(new Rectangle(8, 8, 1904, 1064), BoundsOf(replaced, 1));
    }

    [Theory]
    [InlineData("nan")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-infinity")]
    public void ANumberThatIsNotFiniteWarnsAndIsSkipped(string value)
    {
        var document = ConfigParser.Parse($"""
            dwindle:split_width_multiplier = 1.5
            dwindle:split_width_multiplier = {value}
            dwindle:default_split_ratio = 1.2
            dwindle:default_split_ratio = {value}
            master:mfact = 0.6
            master:mfact = {value}
            scrolling:column_width = 0.7
            scrolling:column_width = {value}
            scrolling:follow_min_visible = 0.3
            scrolling:follow_min_visible = {value}
            scrolling:explicit_column_widths = 0.5, {value}
            cursor:inactive_timeout = 3
            cursor:inactive_timeout = {value}
            """);

        var layout = LayoutOptions.From(document);
        var master = MasterOptions.From(document);
        var scrolling = ScrollingOptions.From(document);
        var cursor = CursorOptions.From(document);

        Assert.Equal([2, 4, 6, 8, 10, 11, 13], document.Warnings.Select(warning => warning.Line));

        Assert.Equal(1.5, layout.SplitWidthMultiplier);
        Assert.Equal(1.2, layout.DefaultSplitRatio);
        Assert.Equal(0.6, master.Mfact);
        Assert.Equal(0.7, scrolling.ColumnWidth);
        Assert.Equal(0.3, scrolling.FollowMinVisible);
        Assert.Equal(ScrollingOptions.Defaults.ExplicitColumnWidths, scrolling.ExplicitColumnWidths);
        Assert.Equal(3, cursor.InactiveTimeoutSeconds);
    }

    [Fact]
    public void InvalidOptionValuesAreLeftOut()
    {
        var core = Configured("""
            general:gaps_in = 0
            general:gaps_out = 40
            general:gaps_out = no
            general:border_size = 0
            """);

        var outcome = core.Handle(Appeared(1, Wide));

        Assert.Equal(new Rectangle(40, 40, 1840, 1000), BoundsOf(outcome, 1));
    }

    [Fact]
    public void ChangingGapsMovesClientsAlreadyPlaced()
    {
        var core = new WindowManager();
        core.Handle(Appeared(1, Wide));

        var outcome = core.Handle(new Configure(ConfigParser.Parse("""
            general:gaps_in = 0
            general:gaps_out = 10
            general:border_size = 0
            """)));

        Assert.Equal(new Rectangle(10, 10, 1900, 1060), BoundsOf(outcome, 1));
    }

    [Fact]
    public void ForceSplitPutsTheNewClientOnTheNamedSide()
    {
        var left = Configured(Flat("""
            dwindle:force_split = left
            """));
        left.Handle(Appeared(1, Wide));
        var forcedLeft = left.Handle(Appeared(2, Wide, new Point(1800, 500)));

        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(forcedLeft, 1));
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(forcedLeft, 2));

        var right = Configured(Flat("""
            dwindle:force_split = 2
            """));
        right.Handle(Appeared(1, Wide));
        var forcedRight = right.Handle(Appeared(2, Wide, new Point(10, 500)));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(forcedRight, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(forcedRight, 2));
    }

    [Fact]
    public void FollowMouseKeepsThePointerSide()
    {
        var core = Configured(Flat("""
            dwindle:force_split = follow_mouse
            """));
        core.Handle(Appeared(1, Wide));

        var outcome = core.Handle(Appeared(2, Wide, new Point(10, 500)));

        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void PreserveSplitKeepsTheDirectionWhenTheContainerChangesShape()
    {
        var core = Configured(Flat("""
            dwindle:preserve_split = yes
            """));
        core.Handle(Appeared(1, Wide));
        core.Handle(Appeared(2, Wide));
        core.Handle(Appeared(3, Wide));

        var outcome = core.Handle(new WindowClosed(new WindowId(1)));

        Assert.Equal(new Rectangle(0, 0, 1920, 540), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(0, 540, 1920, 540), BoundsOf(outcome, 3));
    }

    [Fact]
    public void PreservedSplitSurvivesMinimizeAndRestore()
    {
        var core = Configured(Flat("""
            dwindle:preserve_split = true
            """));
        core.Handle(Appeared(1, Wide));
        core.Handle(Appeared(2, Wide));
        core.Handle(Appeared(3, Wide));
        core.Handle(new WindowMinimized(new WindowId(3)));

        var outcome = core.Handle(new WindowRestored(new WindowId(3)));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 540), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(960, 540, 960, 540), BoundsOf(outcome, 3));
    }

    [Fact]
    public void SmartSplitUsesThePointerTriangleAndKeepsThatDirection()
    {
        var aimed = Configured(Flat("""
            dwindle:smart_split = true
            """));
        aimed.Handle(Appeared(1, Wide));
        var split = aimed.Handle(Appeared(2, Wide, new Point(960, 10)));

        Assert.Equal(new Rectangle(0, 540, 1920, 540), BoundsOf(split, 1));
        Assert.Equal(new Rectangle(0, 0, 1920, 540), BoundsOf(split, 2));

        var kept = Configured(Flat("""
            dwindle:smart_split = on
            """));
        kept.Handle(Appeared(1, Wide));
        kept.Handle(Appeared(2, Wide));
        kept.Handle(Appeared(3, Wide));
        var closed = kept.Handle(new WindowClosed(new WindowId(1)));

        Assert.Equal(new Rectangle(0, 0, 1920, 540), BoundsOf(closed, 2));
        Assert.Equal(new Rectangle(0, 540, 1920, 540), BoundsOf(closed, 3));
    }

    [Fact]
    public void SplitWidthMultiplierChangesWhenASplitIsSideBySide()
    {
        var area = new Rectangle(0, 0, 1200, 1000);
        var core = Configured(Flat("""
            dwindle:split_width_multiplier = 2
            """));
        core.Handle(Appeared(1, area));

        var outcome = core.Handle(Appeared(2, area));

        Assert.Equal(new Rectangle(0, 0, 1200, 500), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 500, 1200, 500), BoundsOf(outcome, 2));
    }

    [Fact]
    public void DefaultSplitRatioSetsTheShareOfTheFirstSide()
    {
        var core = Configured(Flat("""
            dwindle:default_split_ratio = 1.5
            """));
        core.Handle(Appeared(1, Wide));

        var outcome = core.Handle(Appeared(2, Wide));

        Assert.Equal(new Rectangle(0, 0, 1440, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(1440, 0, 480, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void SplitBiasGivesTheRatioToTheCurrentClient()
    {
        var core = Configured(Flat("""
            dwindle:default_split_ratio = 1.5
            dwindle:split_bias = current
            dwindle:force_split = 1
            """));
        core.Handle(Appeared(1, Wide));

        var outcome = core.Handle(Appeared(2, Wide));

        Assert.Equal(new Rectangle(480, 0, 1440, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 0, 480, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void UseActiveForSplitsCanPreferTheClientUnderThePointer()
    {
        var core = Configured(Flat("""
            dwindle:use_active_for_splits = false
            """));
        core.Handle(Appeared(1, Wide));
        core.Handle(Appeared(2, Wide, new Point(1500, 500)));
        core.Handle(Appeared(3, Wide, new Point(1500, 800)));

        var outcome = core.Handle(Appeared(4, Wide, new Point(100, 100)));

        Assert.Equal(new Rectangle(0, 540, 960, 540), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 540), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(960, 540, 960, 540), BoundsOf(outcome, 3));
        Assert.Equal(new Rectangle(0, 0, 960, 540), BoundsOf(outcome, 4));
    }

    [Fact]
    public void ClosestLeafIsUsedOnlyOnAReservedAreaOfTheActiveWorkspace()
    {
        var missed = Configured(Flat("""
            dwindle:use_active_for_splits = false
            general:gaps_out = 40
            """));
        missed.Handle(Appeared(1, Wide));
        missed.Handle(Appeared(2, Wide, new Point(1500, 540)));

        var beside = missed.Handle(Appeared(3, Wide, new Point(1900, 540)));

        Assert.Equal(new Rectangle(40, 40, 920, 500), BoundsOf(beside, 1));
        Assert.Equal(new Rectangle(960, 40, 920, 1000), BoundsOf(beside, 2));
        Assert.Equal(new Rectangle(40, 540, 920, 500), BoundsOf(beside, 3));

        var reserved = Configured(Flat("""
            dwindle:use_active_for_splits = false
            """));
        var display = new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1040));
        reserved.Handle(new MonitorsChanged([display]));
        reserved.Handle(Appeared(1, display.WorkArea));
        reserved.Handle(Appeared(2, display.WorkArea, new Point(1500, 500)));

        var onBar = reserved.Handle(Appeared(3, display.WorkArea, new Point(1500, 1060)));

        Assert.Equal(new Rectangle(0, 0, 960, 1040), BoundsOf(onBar, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 520), BoundsOf(onBar, 2));
        Assert.Equal(new Rectangle(960, 520, 960, 520), BoundsOf(onBar, 3));

        var outside = Configured(Flat("""
            dwindle:use_active_for_splits = false
            """));
        outside.Handle(new MonitorsChanged([display]));
        outside.Handle(Appeared(1, display.WorkArea));
        outside.Handle(Appeared(2, display.WorkArea, new Point(1500, 500)));

        var offScreen = outside.Handle(Appeared(3, display.WorkArea, new Point(2000, 500)));

        Assert.Equal(new Rectangle(0, 520, 960, 520), BoundsOf(offScreen, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1040), BoundsOf(offScreen, 2));
        Assert.Equal(new Rectangle(0, 0, 960, 520), BoundsOf(offScreen, 3));

        var background = Configured(Flat("""
            dwindle:use_active_for_splits = false
            windowrulev2 = workspace 2 silent, class:^(extra)$
            """));
        background.Handle(Appeared(1, Wide));
        background.Handle(Appeared(2, Wide));
        background.Handle(new Dispatch("movetoworkspacesilent", "2"));
        background.Handle(new Dispatch("movetoworkspacesilent", "2"));
        background.Handle(new WindowAppeared(new WindowId(3), Wide)
        {
            Pointer = new Point(1500, 500),
            Traits = WindowTraits.Normal with { Class = "extra" },
        });

        var shown = background.Handle(new Dispatch("workspace", "2"));

        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(shown, 1));
        Assert.Equal(new Rectangle(0, 540, 960, 540), BoundsOf(shown, 2));
        Assert.Equal(new Rectangle(0, 0, 960, 540), BoundsOf(shown, 3));
    }

    [Fact]
    public void PreselectOverridesTheNextSplitDirectionWhilePreserveKeepsIt()
    {
        var core = Configured(Flat("""
            dwindle:preserve_split = true
            """));
        core.Handle(Appeared(1, Tall));
        core.Handle(new Preselect("l"));

        var outcome = core.Handle(Appeared(2, Tall));

        Assert.Equal(new Rectangle(540, 0, 540, 1920), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 0, 540, 1920), BoundsOf(outcome, 2));
    }

    [Fact]
    public void PermanentDirectionOverrideKeepsPreselectForLaterClients()
    {
        var core = Configured(Flat("""
            dwindle:permanent_direction_override = true
            """));
        core.Handle(Appeared(1, Tall));
        core.Handle(new Preselect("l"));
        core.Handle(Appeared(2, Tall));

        var outcome = core.Handle(Appeared(3, Tall));

        Assert.Equal(new Rectangle(0, 960, 1080, 960), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(540, 0, 540, 960), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(0, 0, 540, 960), BoundsOf(outcome, 3));
    }

    [Fact]
    public void ANonDirectionClearsPreselect()
    {
        var core = Configured(Flat("""
            dwindle:permanent_direction_override = true
            """));
        core.Handle(Appeared(1, Tall));
        core.Handle(new Preselect("l"));
        core.Handle(new Preselect("x"));

        var outcome = core.Handle(Appeared(2, Tall));

        Assert.Equal(new Rectangle(0, 0, 1080, 960), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 960, 1080, 960), BoundsOf(outcome, 2));
    }

    [Fact]
    public void PseudotileCentersTheClientsOwnSizeInItsTile()
    {
        var core = Configured(Flat(""));
        core.Handle(Appeared(1, Wide, bounds: new Rectangle(0, 0, 800, 600)));

        var on = core.Handle(new Pseudo(new WindowId(1)));

        Assert.Equal(new Rectangle(560, 240, 800, 600), BoundsOf(on, 1));

        var off = core.Handle(new Pseudo(new WindowId(1)));

        Assert.Equal(Wide, BoundsOf(off, 1));
    }

    [Fact]
    public void PseudotileUsesTheDefaultSizeWhenTheClientHasNone()
    {
        var core = Configured(Flat(""));
        core.Handle(Appeared(1, Wide));

        var outcome = core.Handle(new Pseudo(new WindowId(1)));

        Assert.Equal(new Rectangle(320, 180, 1280, 720), BoundsOf(outcome, 1));
    }

    [Fact]
    public void PseudotileScalesDownToFitTheTile()
    {
        var core = Configured(Flat(""));
        core.Handle(Appeared(1, Wide));
        core.Handle(Appeared(2, Wide, bounds: new Rectangle(0, 0, 1280, 720)));

        var outcome = core.Handle(new Pseudo(new WindowId(2)));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(960, 270, 960, 540), BoundsOf(outcome, 2));
    }

    [Fact]
    public void AnotherLayoutNameStillTilesWithDwindle()
    {
        var core = Configured(Flat("""
            general:layout = floating
            """));
        core.Handle(Appeared(1, Wide));

        var outcome = core.Handle(Appeared(2, Wide));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void FloatingClientOpensCenteredWhenGapsAndBorderAreSet()
    {
        var core = Configured("""
            general:gaps_in = 12
            general:gaps_out = 20
            general:border_size = 4
            """);
        var bounds = new Rectangle(400, 300, 320, 200);
        var tiled = core.Handle(Appeared(1, Wide));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Wide)
        {
            Traits = WindowTraits.Normal with { Dialog = true },
            Bounds = bounds,
        });

        Assert.Equal(new Rectangle(24, 24, 1872, 1032), BoundsOf(tiled, 1));
        Assert.Equal(NewFloat.Centered(Wide, bounds), BoundsOf(outcome, 2));
    }

    [Theory]
    [InlineData("dwindle")]
    [InlineData("master")]
    [InlineData("monocle")]
    [InlineData("scrolling")]
    public void ALoneTileIsPaddedToTheAspectRatio(string layout)
    {
        var plain = Configured(Flat($"general:layout = {layout}\n"));
        var ruled = Configured(Flat($"general:layout = {layout}\nlayout:single_window_aspect_ratio = 4 3\n"));

        var before = BoundsOf(plain.Handle(Appeared(1, Wide)), 1);
        var after = BoundsOf(ruled.Handle(Appeared(1, Wide)), 1);

        // 4:3 on 1920 by 1080 leaves 1920 - 1080 * 4 / 3 = 480 pixels, split between the two sides.
        Assert.Equal(before with { X = before.X + 240, Width = before.Width - 480 }, after);
    }

    [Fact]
    public void PaddingWithinTheToleranceIsNotAdded()
    {
        var core = Configured(Flat("layout:single_window_aspect_ratio = 4 3\nlayout:single_window_aspect_ratio_tolerance = 0.2\n"));

        Assert.Equal(new Rectangle(0, 0, 1920, 1080), BoundsOf(core.Handle(Appeared(1, Wide)), 1));
        Assert.Equal("{\"option\":\"layout:single_window_aspect_ratio_tolerance\",\"float\":0.2,\"set\":true}", core.Execute("getoption layout:single_window_aspect_ratio_tolerance").Text);
    }

    [Fact]
    public void TwoTilesAreNotPadded()
    {
        var core = Configured(Flat("layout:single_window_aspect_ratio = 4 3\n"));
        Assert.Equal(new Rectangle(240, 0, 1440, 1080), BoundsOf(core.Handle(Appeared(1, Wide)), 1));

        var split = core.Handle(Appeared(2, Wide));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(split, 1));
    }

    private static string Flat(string extra) =>
        "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + extra;

    private static WindowManager Configured(string text)
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(text)));
        return core;
    }

    private static WindowAppeared Appeared(long window, Rectangle area, Point? pointer = null, Rectangle bounds = default) =>
        new WindowAppeared(new WindowId(window), area)
        {
            Pointer = pointer,
            Bounds = bounds,
        };

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }
}
