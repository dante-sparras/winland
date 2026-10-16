using Winland;

namespace Winland.Tests;

public class SizeLimitTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);
    private static readonly Size SmallMax = new(400, 300);

    [Fact]
    public void SmallMaximumFloatsInsteadOfLeavingAnEmptyTile()
    {
        var core = new Core();
        var bounds = new Rectangle(40, 50, 380, 260);

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { MaxSize = SmallMax },
            Bounds = bounds,
        });

        var client = Assert.Single(outcome.State.Clients);
        Assert.True(client.Floating);
        Assert.Equal(NewFloat.Centered(Area, bounds), Assert.Single(outcome.Placements).Bounds);
    }

    [Fact]
    public void SmallMaximumFloatsAndLeavesTheExistingTileFull()
    {
        var core = new Core();
        var bounds = new Rectangle(40, 50, 380, 260);
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var floated = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { MaxSize = SmallMax },
            Bounds = bounds,
        });

        var client = Assert.Single(floated.State.Clients, candidate => candidate.Window == new WindowId(2));
        Assert.True(client.Floating);
        Assert.Equal(NewFloat.Centered(Area, bounds), Assert.Single(floated.Placements, placement => placement.Client == client.Id).Bounds);
        Assert.Contains(floated.State.Clients, candidate => candidate.Window == new WindowId(1) && !candidate.Floating);

        var split = core.Handle(new WindowAppeared(new WindowId(3), Area));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(split, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(split, 3));
        Assert.True(Assert.Single(split.State.Clients, candidate => candidate.Window == new WindowId(2)).Floating);
    }

    [Fact]
    public void MaximumBelowEitherSideOfTheTargetTileFloats()
    {
        var core = new Core();
        var bounds = new Rectangle(10, 20, 200, 150);
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var floated = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            // The target is the full tile being split, so a width of 1000 still floats on a 1920 tile.
            Traits = WindowTraits.Normal with { MaxSize = new Size(1000, 4000) },
            Bounds = bounds,
        });

        Assert.True(Assert.Single(floated.State.Clients, candidate => candidate.Window == new WindowId(2)).Floating);
        Assert.Equal(NewFloat.Centered(Area, bounds), BoundsOf(floated, 2));
    }

    [Fact]
    public void MaximumThatCoversTheTargetTileStaysTiled()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { MaxSize = new Size(1920, 1080) },
        });

        Assert.False(Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(2)).Floating);
        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void LargeMinimumIsNotSquashedAndTheNeighbourTakesTheRest()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { MinSize = new Size(1400, 100) },
        });

        Assert.Equal(new Rectangle(0, 0, 520, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(520, 0, 1400, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void LargeMinimumOnTheCrossAxisLeavesTheNeighbourOnTheMonitor()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { MinSize = new Size(100, 2000) },
        });

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 2000), BoundsOf(outcome, 2));
    }

    [Fact]
    public void NestedMinimumWidensItsColumnAndShrinksTheNeighbour()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(3), Area)
        {
            Traits = WindowTraits.Normal with { MinSize = new Size(1500, 100) },
        });

        Assert.Equal(new Rectangle(0, 0, 420, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(420, 0, 1500, 540), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(420, 540, 1500, 540), BoundsOf(outcome, 3));
    }

    [Fact]
    public void MinimumsThatExceedTheMonitorStayOnIt()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { MinSize = new Size(1200, 100) },
        });

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { MinSize = new Size(1200, 100) },
        });

        // The split stays where it would be. Each window grows to its minimum around the middle of its
        // tile and is held inside the work area, so they overlap rather than leave the Monitor.
        Assert.Equal(new Rectangle(0, 0, 1200, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(720, 0, 1200, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void AnOversizedMinimumLeavesEveryOtherTileOnTheMonitor()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { MinSize = new Size(1920, 1080) },
        });
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        core.Handle(new WindowAppeared(new WindowId(3), Area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(4), Area));

        Assert.Equal(Area, BoundsOf(outcome, 1));
        foreach (var window in new long[] { 2, 3, 4 })
        {
            var bounds = BoundsOf(outcome, window);
            Assert.True(bounds.Width > 0 && bounds.Height > 0, $"window {window} is {bounds}");
            Assert.True(
                bounds.X >= Area.X && bounds.Y >= Area.Y && bounds.X + bounds.Width <= Area.X + Area.Width && bounds.Y + bounds.Height <= Area.Y + Area.Height,
                $"window {window} is {bounds}");
        }
    }

    [Fact]
    public void SoleWindowMinimumLargerThanTheMonitorExtendsPastIt()
    {
        var core = new Core();

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { MinSize = new Size(3000, 2000) },
        });

        Assert.Equal(new Rectangle(0, 0, 3000, 2000), BoundsOf(outcome, 1));
    }

    [Fact]
    public void BorderIsOutsideTheMinimum()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("""
            general:border_size = 10
            general:gaps_in = 0
            general:gaps_out = 0
            """)));
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { MinSize = new Size(1400, 100) },
        });

        Assert.Equal(new Rectangle(10, 10, 480, 1060), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(510, 10, 1400, 1060), BoundsOf(outcome, 2));
    }

    [Fact]
    public void ReportedMaximumShrinksIntoAFloatAndTheNeighbourTakesTheTile()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var outcome = core.Handle(new WindowLimitsChanged(new WindowId(2), null, SmallMax));

        Assert.True(Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(2)).Floating);
        Assert.Equal(new Rectangle(960, 0, 400, 300), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), BoundsOf(outcome, 1));
    }

    [Fact]
    public void ReportedMinimumGrowsTheTile()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var outcome = core.Handle(new WindowLimitsChanged(new WindowId(2), new Size(1400, 100), null));

        Assert.Equal(new Rectangle(0, 0, 520, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(520, 0, 1400, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void MinSizeRuleRaisesTheTile()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = min_size 1400 100, class:^(wide)$\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { Class = "wide" },
        });

        Assert.Equal(new Rectangle(0, 0, 520, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(520, 0, 1400, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void MaxSizeRuleFloatsTheWindow()
    {
        var core = new Core(ConfigParser.Parse("windowrule = max_size 400 300, ^(small)$\n"));
        var bounds = new Rectangle(12, 16, 200, 120);
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { Class = "small" },
            Bounds = bounds,
        });

        Assert.True(Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(2)).Floating);
        Assert.Equal(NewFloat.Centered(Area, bounds), BoundsOf(outcome, 2));
    }

    [Fact]
    public void NoMaxSizeKeepsTheWindowsOwnMaximumFromFloatingIt()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = no_max_size, class:^(free)$\n"));

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Class = "free", MaxSize = SmallMax },
        });

        Assert.False(Assert.Single(outcome.State.Clients).Floating);
        Assert.Equal(Area, BoundsOf(outcome, 1));
    }

    [Fact]
    public void MaxSizeRuleWinsOverNoMaxSize()
    {
        var core = new Core(ConfigParser.Parse("""
            windowrulev2 = max_size 400 300, class:^(small)$
            windowrulev2 = no_max_size, class:^(small)$
            """));
        var bounds = new Rectangle(8, 8, 100, 80);

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Class = "small" },
            Bounds = bounds,
        });

        Assert.True(Assert.Single(outcome.State.Clients).Floating);
        Assert.Equal(NewFloat.Centered(Area, bounds), BoundsOf(outcome, 1));
    }

    [Fact]
    public void LaterMinSizeReplacesAnEarlierOne()
    {
        var core = new Core(ConfigParser.Parse("""
            windowrulev2 = min_size 1400 100, class:^(wide)$
            windowrulev2 = min_size 100 100, class:^(wide)$
            """));
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { Class = "wide", MinSize = new Size(1400, 100) },
        });

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 1080), BoundsOf(outcome, 2));
    }

    [Fact]
    public void MalformedSizeRuleWarnsAndTheConfigStillLoads()
    {
        var document = ConfigParser.Parse("windowrulev2 = min_size 400, class:^(wide)$\ngeneral:gaps_in = 0\n");

        Assert.Empty(document.WindowRules);
        Assert.Contains(document.Warnings, warning => warning.Line == 1 && warning.Message == "Malformed window rule.");
    }

    [Fact]
    public void VisibleMinimumShrinksByTheFrameInsets()
    {
        var insets = new FrameInsets(7, 0, 7, 7);

        var (min, max) = TrackLimits.Visible(new Size(200, 120), new Size(414, 314), insets);

        Assert.Equal(new Size(186, 113), min);
        Assert.Equal(new Size(400, 307), max);
    }

    [Fact]
    public void OuterMaximumBelowFiveIsNoMaximum()
    {
        var (min, max) = TrackLimits.Visible(new Size(80, 40), new Size(4, 3), FrameInsets.None);

        Assert.Equal(new Size(80, 40), min);
        Assert.Null(max);
    }

    [Fact]
    public void OneUnlimitedMaximumAxisLeavesTheOther()
    {
        var (_, max) = TrackLimits.Visible(null, new Size(4, 300), FrameInsets.None);

        Assert.Equal(new Size(int.MaxValue, 300), max);
    }

    [Fact]
    public void MissingOuterTrackSizesAreNoLimits()
    {
        var (min, max) = TrackLimits.Visible(null, null, new FrameInsets(7, 0, 7, 7));

        Assert.Null(min);
        Assert.Null(max);
    }

    [Fact]
    public void AMinimumUsesTheVisibleFrameAndPlacementAddsTheInsetsOnce()
    {
        var insets = new FrameInsets(7, 0, 7, 7);
        var outerMin = new Size(1414, 107);
        var (visible, _) = TrackLimits.Visible(outerMin, null, insets);
        Assert.Equal(new Size(1400, 100), visible);

        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { MinSize = visible },
        });

        var tile = BoundsOf(outcome, 2);
        Assert.Equal(1400, tile.Width);
        Assert.Equal(Area.Height, tile.Height);

        var placed = insets.OuterFor(tile);
        Assert.Equal(outerMin.Width, placed.Width);
        Assert.Equal(tile.Height + insets.Top + insets.Bottom, placed.Height);
    }

    [Fact]
    public void MaxSizeAndNoInitialFocusApplyTogether()
    {
        var core = new Core(ConfigParser.Parse("""
            windowrulev2 = max_size 400 300, class:^(small)$
            windowrulev2 = noinitialfocus, class:^(small)$
            """));
        var bounds = new Rectangle(12, 16, 200, 120);
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { Class = "small" },
            Bounds = bounds,
        });

        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(2));
        Assert.True(client.Floating);
        Assert.Equal(NewFloat.Centered(Area, bounds), BoundsOf(outcome, 2));
        Assert.Equal(new WindowId(1), outcome.Focus);
        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("killactive")).Close);
    }

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }
}
