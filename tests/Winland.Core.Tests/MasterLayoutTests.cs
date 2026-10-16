using Winland;

namespace Winland.Tests;

public class MasterLayoutTests
{
    private static readonly Rectangle Screen = new(0, 0, 2000, 1000);

    [Fact]
    public void DocumentedMasterOptionsMatchTheLayoutSource()
    {
        var options = MasterOptions.Documented;

        Assert.Equal(0.55, options.Mfact);
        Assert.False(options.AllowSmallSplit);
        Assert.Equal(MasterNewStatus.Slave, options.NewStatus);
        Assert.False(options.NewOnTop);
        Assert.Equal(MasterNewOnActive.None, options.NewOnActive);
        Assert.Equal(MasterOrientation.Left, options.Orientation);
        Assert.Equal(2, options.SlaveCountForCenter);
        Assert.Equal(MasterOrientation.Left, options.CenterFallback);
        Assert.True(options.SmartResizing);
        Assert.True(options.DropAtCursor);
        Assert.False(options.AlwaysKeepPosition);
        Assert.False(options.FocusMasterOnClose);
    }

    [Fact]
    public void MasterOptionsClampAndUnknownNamesFallBack()
    {
        var options = MasterOptions.From(ConfigParser.Parse("""
            master:mfact = 2
            master:slave_count_for_center_master = 99
            master:orientation = diagonal
            master:center_master_fallback = center
            master:new_on_top = yes
            """));

        Assert.Equal(1, options.Mfact);
        Assert.Equal(10, options.SlaveCountForCenter);
        Assert.Equal(MasterOrientation.Left, options.Orientation);
        Assert.Equal(MasterOrientation.Left, options.CenterFallback);
        Assert.True(options.NewOnTop);
    }

    [Fact]
    public void MasterLayoutAndItsOptionsDoNotWarn()
    {
        var document = ConfigParser.Parse("""
            general:layout = master
            master {
                mfact = 0.5
                orientation = top
            }
            """);

        Assert.Empty(document.Warnings);
        Assert.Contains(document.Settings, setting => setting == new Setting("general:layout", "master"));
        Assert.Contains(document.Settings, setting => setting == new Setting("master:mfact", "0.5"));
        Assert.Contains(document.Settings, setting => setting == new Setting("master:orientation", "top"));
    }

    [Fact]
    public void MasterOptionsThatCannotApplyStayGaps()
    {
        var document = ConfigParser.Parse("""
            master:special_scale_factor = 0.5
            master:center_ignores_reserved = true
            master:mfact = 0.5
            """);

        Assert.Equal(
            [
                new ConfigWarning(1, "Unsupported keyword 'master:special_scale_factor'."),
                new ConfigWarning(2, "Unsupported keyword 'master:center_ignores_reserved'."),
            ],
            document.Warnings);
        Assert.Equal(new Setting("master:mfact", "0.5"), Assert.Single(document.Settings));
    }

    [Fact]
    public void DefaultMasterPutsTheFirstClientOnTheLeft()
    {
        var outcome = Open(Flat(""), 1, 2);

        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(1100, 0, 900, 1000), BoundsOf(outcome, 2));
    }

    [Theory]
    [InlineData("right", 900, 0, 1100, 1000, 0, 0, 900, 1000)]
    [InlineData("top", 0, 0, 2000, 550, 0, 550, 2000, 450)]
    [InlineData("bottom", 0, 450, 2000, 550, 0, 0, 2000, 450)]
    public void OrientationPlacesTheMasterOnThatSide(
        string orientation,
        int masterX,
        int masterY,
        int masterWidth,
        int masterHeight,
        int slaveX,
        int slaveY,
        int slaveWidth,
        int slaveHeight)
    {
        var outcome = Open(Flat($"master:orientation = {orientation}\n"), 1, 2);

        Assert.Equal(new Rectangle(masterX, masterY, masterWidth, masterHeight), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(slaveX, slaveY, slaveWidth, slaveHeight), BoundsOf(outcome, 2));
    }

    [Fact]
    public void CenterWaitsForEnoughSlavesThenSplitsThemOnBothSides()
    {
        var fallback = Open(Flat("master:orientation = center\n"), 1, 2);

        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(fallback, 1));
        Assert.Equal(new Rectangle(1100, 0, 900, 1000), BoundsOf(fallback, 2));

        var centered = Open(Flat("master:orientation = center\n"), 1, 2, 3);

        Assert.Equal(new Rectangle(450, 0, 1100, 1000), BoundsOf(centered, 1));
        Assert.Equal(new Rectangle(0, 0, 450, 1000), BoundsOf(centered, 2));
        Assert.Equal(new Rectangle(1550, 0, 450, 1000), BoundsOf(centered, 3));
    }

    [Fact]
    public void CenterFallbackRightUsesThatSideUntilEnoughSlavesExist()
    {
        var outcome = Open(Flat("""
            master:orientation = center
            master:center_master_fallback = right
            """), 1, 2);

        Assert.Equal(new Rectangle(900, 0, 1100, 1000), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 0, 900, 1000), BoundsOf(outcome, 2));
    }

    [Fact]
    public void OddCenterSlaveGoesToTheFallbackSide()
    {
        var outcome = Open(Flat("master:orientation = center\n"), 1, 2, 3, 4);

        Assert.Equal(new Rectangle(450, 0, 1100, 1000), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 0, 450, 500), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(1550, 0, 450, 1000), BoundsOf(outcome, 3));
        Assert.Equal(new Rectangle(0, 500, 450, 500), BoundsOf(outcome, 4));
    }

    [Fact]
    public void GapsAndBorderShrinkEveryOuterEdgeAndEverySharedEdge()
    {
        var outcome = Open("""
            general:layout = master
            general:gaps_in = 10
            general:gaps_out = 20
            general:border_size = 2
            master:mfact = 0.5
            """, 1, 2);

        Assert.Equal(new Rectangle(22, 22, 966, 956), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(1012, 22, 966, 956), BoundsOf(outcome, 2));
    }

    [Fact]
    public void ADialogOpensCentered()
    {
        var core = CoreOf(Flat(""));
        var tiled = core.Handle(Appeared(1));
        var bounds = new Rectangle(400, 300, 320, 200);

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Screen)
        {
            Traits = WindowTraits.Normal with { Dialog = true },
            Bounds = bounds,
        });

        Assert.Equal(new Rectangle(0, 0, 2000, 1000), BoundsOf(tiled, 1));
        Assert.Equal(NewFloat.Centered(Screen, bounds), BoundsOf(outcome, 2));
        Assert.True(ClientOf(outcome, 2).Floating);
        Assert.Equal(new Rectangle(0, 0, 2000, 1000), BoundsOf(core.Handle(new Dispatch("layoutmsg", "orientationleft")), 1));
    }

    [Fact]
    public void NewOnTopStacksTheNewestSlaveFirst()
    {
        var outcome = Open(Flat("master:new_on_top = true\n"), 1, 2, 3);

        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(1100, 500, 900, 500), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(1100, 0, 900, 500), BoundsOf(outcome, 3));
    }

    [Fact]
    public void NewStatusMasterGivesTheNewestClientTheMasterArea()
    {
        var outcome = Open(Flat("master:new_status = master\n"), 1, 2);

        Assert.Equal(new Rectangle(1100, 0, 900, 1000), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(outcome, 2));
    }

    [Fact]
    public void NewOnActiveBeforeInsertsNextToTheFocusedClient()
    {
        var outcome = Open(Flat("master:new_on_active = before\n"), 1, 2, 3);

        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(1100, 500, 900, 500), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(1100, 0, 900, 500), BoundsOf(outcome, 3));
    }

    [Fact]
    public void InheritFollowsWhetherTheFocusedClientIsMaster()
    {
        var core = CoreOf(Flat("master:new_status = inherit\n"));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(new Dispatch("movefocus", "r"));

        var outcome = core.Handle(Appeared(3));

        Assert.Equal(new Rectangle(1100, 0, 900, 500), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(1100, 500, 900, 500), BoundsOf(outcome, 3));
    }

    [Fact]
    public void AlwaysKeepPositionHoldsOneClientAtItsMasterSize()
    {
        var left = Open(Flat("master:always_keep_position = true\n"), 1);
        var right = Open(Flat("""
            master:always_keep_position = true
            master:orientation = right
            """), 1);
        var center = Open(Flat("""
            master:always_keep_position = true
            master:orientation = center
            """), 1);

        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(left, 1));
        Assert.Equal(new Rectangle(900, 0, 1100, 1000), BoundsOf(right, 1));
        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(center, 1));
    }

    [Fact]
    public void ClosingTheMasterPromotesTheFirstSlave()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));

        var outcome = core.Handle(new WindowClosed(new WindowId(1)));

        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(1100, 0, 900, 1000), BoundsOf(outcome, 3));
    }

    [Fact]
    public void FocusMasterOnCloseFocusesTheMaster()
    {
        var kept = CoreOf(Flat(""));
        kept.Handle(Appeared(1));
        kept.Handle(Appeared(2));
        kept.Handle(Appeared(3));
        var usual = kept.Handle(new WindowClosed(new WindowId(3)));

        var master = CoreOf(Flat("master:focus_master_on_close = true\n"));
        master.Handle(Appeared(1));
        master.Handle(Appeared(2));
        master.Handle(Appeared(3));
        var focused = master.Handle(new WindowClosed(new WindowId(3)));

        Assert.Contains(usual.Events, item => item is FocusChanged changed && changed.Window == new WindowId(2));
        Assert.Contains(focused.Events, item => item is FocusChanged changed && changed.Window == new WindowId(1));
    }

    [Fact]
    public void MinimizingASlaveGivesItsSpaceBackUntilRestore()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));

        var minimized = core.Handle(new WindowMinimized(new WindowId(3)));
        Assert.Equal(new Rectangle(1100, 0, 900, 1000), BoundsOf(minimized, 2));

        var restored = core.Handle(new WindowRestored(new WindowId(3)));
        Assert.Equal(new Rectangle(1100, 0, 900, 500), BoundsOf(restored, 2));
        Assert.Equal(new Rectangle(1100, 500, 900, 500), BoundsOf(restored, 3));
    }

    [Fact]
    public void MfactAcceptsADeltaOrAnExactRatio()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        var delta = core.Handle(new Dispatch("layoutmsg", "mfact 0.05"));
        Assert.Equal(new Rectangle(0, 0, 1200, 1000), BoundsOf(delta, 1));
        Assert.Equal(new Rectangle(1200, 0, 800, 1000), BoundsOf(delta, 2));

        var exact = core.Handle(new Dispatch("layoutmsg", "mfact exact 0.5"));
        Assert.Equal(new Rectangle(0, 0, 1000, 1000), BoundsOf(exact, 1));
        Assert.Equal(new Rectangle(1000, 0, 1000, 1000), BoundsOf(exact, 2));

        var clamped = core.Handle(new Dispatch("layoutmsg", "mfact exact 2"));
        Assert.Equal(new Rectangle(0, 0, 1900, 1000), BoundsOf(clamped, 1));

        var bad = core.Handle(new Dispatch("layoutmsg", "mfact nope"));
        Assert.Equal("error: bad ratio", bad.Error);
        var kept = core.Handle(new Dispatch("layoutmsg", "orientationleft"));
        Assert.Equal(new Rectangle(0, 0, 1900, 1000), BoundsOf(kept, 1));
    }

    [Fact]
    public void OrientationMessagesCycleLeftTopRightBottomCenter()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));

        var center = core.Handle(new Dispatch("layoutmsg", "orientationprev"));
        Assert.Equal(new Rectangle(450, 0, 1100, 1000), BoundsOf(center, 1));
        Assert.Equal(new Rectangle(0, 0, 450, 1000), BoundsOf(center, 2));
        Assert.Equal(new Rectangle(1550, 0, 450, 1000), BoundsOf(center, 3));

        var left = core.Handle(new Dispatch("layoutmsg", "orientationnext"));
        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(left, 1));

        var top = core.Handle(new Dispatch("layoutmsg", "orientationnext"));
        Assert.Equal(new Rectangle(0, 0, 2000, 550), BoundsOf(top, 1));

        var right = core.Handle(new Dispatch("layoutmsg", "orientationnext"));
        Assert.Equal(new Rectangle(900, 0, 1100, 1000), BoundsOf(right, 1));

        var bottom = core.Handle(new Dispatch("layoutmsg", "orientationbottom"));
        Assert.Equal(new Rectangle(0, 450, 2000, 550), BoundsOf(bottom, 1));

        var jumped = core.Handle(new Dispatch("layoutmsg", "orientationcycle top bottom"));
        Assert.Equal(new Rectangle(0, 0, 2000, 550), BoundsOf(jumped, 1));
    }

    [Fact]
    public void AddMasterAndRemoveMasterChangeHowManyMastersShareTheArea()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));
        var refused = core.Handle(new Dispatch("layoutmsg", "addmaster"));
        Assert.Equal("error: nothing to do", refused.Error);

        core.Handle(Appeared(2));
        core.Handle(Appeared(3));
        var added = core.Handle(new Dispatch("layoutmsg", "addmaster"));

        Assert.Equal(new Rectangle(0, 0, 1100, 500), BoundsOf(added, 1));
        Assert.Equal(new Rectangle(1100, 0, 900, 1000), BoundsOf(added, 2));
        Assert.Equal(new Rectangle(0, 500, 1100, 500), BoundsOf(added, 3));

        var removed = core.Handle(new Dispatch("layoutmsg", "removemaster"));
        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(removed, 1));
        Assert.Equal(new Rectangle(1100, 0, 900, 500), BoundsOf(removed, 2));
        Assert.Equal(new Rectangle(1100, 500, 900, 500), BoundsOf(removed, 3));
    }

    [Fact]
    public void AllowSmallSplitAddsAMasterWhenOnlyOneSlaveExists()
    {
        var core = CoreOf(Flat("master:allow_small_split = true\n"));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        var outcome = core.Handle(new Dispatch("layoutmsg", "addmaster"));

        Assert.Equal(new Rectangle(0, 0, 2000, 500), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(0, 500, 2000, 500), BoundsOf(outcome, 2));
    }

    [Fact]
    public void FocusCycleSwapAndRollMoveAmongMasterAndSlaves()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));

        var next = core.Handle(new Dispatch("layoutmsg", "cyclenext"));
        Assert.Equal(new WindowId(1), next.Focus);

        var previous = core.Handle(new Dispatch("layoutmsg", "cycleprev"));
        Assert.Equal(new WindowId(3), previous.Focus);

        var stopped = core.Handle(new Dispatch("layoutmsg", "cyclenext noloop"));
        Assert.Null(stopped.Focus);

        var swapped = core.Handle(new Dispatch("layoutmsg", "swapwithmaster child"));
        Assert.Equal(new WindowId(1), swapped.Focus);
        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(swapped, 3));
        Assert.Equal(new Rectangle(1100, 0, 900, 500), BoundsOf(swapped, 2));
        Assert.Equal(new Rectangle(1100, 500, 900, 500), BoundsOf(swapped, 1));

        var fresh = CoreOf(Flat(""));
        fresh.Handle(Appeared(1));
        fresh.Handle(Appeared(2));
        fresh.Handle(Appeared(3));
        var rolled = fresh.Handle(new Dispatch("layoutmsg", "rollnext"));
        Assert.Equal(new WindowId(2), rolled.Focus);
        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(rolled, 2));
        Assert.Equal(new Rectangle(1100, 0, 900, 500), BoundsOf(rolled, 3));
        Assert.Equal(new Rectangle(1100, 500, 900, 500), BoundsOf(rolled, 1));
    }

    [Fact]
    public void FocusMasterPreviousReturnsToTheClientItLeft()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));

        var master = core.Handle(new Dispatch("layoutmsg", "focusmaster previous"));
        Assert.Equal(new WindowId(1), master.Focus);

        var back = core.Handle(new Dispatch("layoutmsg", "focusmaster previous"));
        Assert.Equal(new WindowId(3), back.Focus);

        core.Handle(new Dispatch("layoutmsg", "focusmaster"));
        var stayed = core.Handle(new Dispatch("layoutmsg", "focusmaster master"));
        Assert.Null(stayed.Focus);
    }

    [Fact]
    public void SwapNextExchangesTheActiveClientWithTheNextSlave()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));
        core.Handle(new Dispatch("movefocus", "u"));

        var outcome = core.Handle(new Dispatch("layoutmsg", "swapnext"));

        Assert.Equal(new Rectangle(1100, 0, 900, 500), BoundsOf(outcome, 3));
        Assert.Equal(new Rectangle(1100, 500, 900, 500), BoundsOf(outcome, 2));
    }

    [Fact]
    public void SmartResizingRedistributesTheSlaveStack()
    {
        var smart = CoreOf(Flat(""));
        smart.Handle(Appeared(1));
        smart.Handle(Appeared(2));
        smart.Handle(Appeared(3));
        smart.Handle(new Dispatch("movefocus", "u"));
        var grown = smart.Handle(new Dispatch("resizeactive", "0 100"));

        Assert.Equal(new Rectangle(1100, 0, 900, 600), BoundsOf(grown, 2));
        Assert.Equal(new Rectangle(1100, 600, 900, 400), BoundsOf(grown, 3));

        var plain = CoreOf(Flat("""
            master:smart_resizing = false
            """));
        plain.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 2000, 900)));
        plain.Handle(new WindowAppeared(new WindowId(2), new Rectangle(0, 0, 2000, 900)));
        plain.Handle(new WindowAppeared(new WindowId(3), new Rectangle(0, 0, 2000, 900)));
        plain.Handle(new WindowAppeared(new WindowId(4), new Rectangle(0, 0, 2000, 900)));
        plain.Handle(new Dispatch("movefocus", "u"));
        plain.Handle(new Dispatch("movefocus", "u"));
        var sequential = plain.Handle(new Dispatch("resizeactive", "0 600"));

        Assert.Equal(new Rectangle(1100, 0, 900, 585), BoundsOf(sequential, 2));
        Assert.Equal(new Rectangle(1100, 585, 900, 158), BoundsOf(sequential, 3));
        Assert.Equal(new Rectangle(1100, 743, 900, 157), BoundsOf(sequential, 4));
    }

    [Fact]
    public void ResizeActiveChangesTheMasterFraction()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        var outcome = core.Handle(new Dispatch("resizeactive", "200 0"));

        Assert.Equal(new Rectangle(0, 0, 1300, 1000), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(1300, 0, 700, 1000), BoundsOf(outcome, 2));
    }

    [Fact]
    public void MoveFocusAndMoveWindowUseTheMasterGeometry()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        var focused = core.Handle(new Dispatch("movefocus", "l"));
        Assert.Equal(new WindowId(1), focused.Focus);

        var moved = core.Handle(new Dispatch("movewindow", "r"));
        Assert.Equal(new Rectangle(1100, 0, 900, 1000), BoundsOf(moved, 1));
        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(moved, 2));
    }

    [Fact]
    public void DropAtCursorInsertsOnThePointerAndOffAppends()
    {
        var aimed = CoreOf(Flat("bindm = SUPER, mouse:272, movewindow\n"));
        aimed.Handle(Appeared(1));
        aimed.Handle(Appeared(2));
        aimed.Handle(Appeared(3));
        aimed.Handle(new Dispatch("movefocus", "u"));
        Press(aimed, "Super_L");
        aimed.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1500, 100) });
        aimed.Handle(new PointerMoved(new Point(1500, 100)));
        var kept = aimed.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(1500, 100) });

        Assert.Equal(new Rectangle(1100, 0, 900, 500), BoundsOf(kept, 2));
        Assert.Equal(new Rectangle(1100, 500, 900, 500), BoundsOf(kept, 3));

        var appended = CoreOf(Flat("""
            master:drop_at_cursor = false
            bindm = SUPER, mouse:272, movewindow
            """));
        appended.Handle(Appeared(1));
        appended.Handle(Appeared(2));
        appended.Handle(Appeared(3));
        appended.Handle(new Dispatch("movefocus", "u"));
        Press(appended, "Super_L");
        appended.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1500, 100) });
        appended.Handle(new PointerMoved(new Point(1500, 100)));
        var dropped = appended.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(1500, 100) });

        Assert.Equal(new Rectangle(1100, 0, 900, 500), BoundsOf(dropped, 3));
        Assert.Equal(new Rectangle(1100, 500, 900, 500), BoundsOf(dropped, 2));
    }

    [Fact]
    public void AnUnknownMasterMessageIsRejected()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));

        var outcome = core.Handle(new Dispatch("layoutmsg", "nosuch"));

        Assert.Equal("error: bad layoutmsg 'nosuch'", outcome.Error);
    }

    [Fact]
    public void ASlaveMinimumTakesSpaceFromItsNeighbour()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        var outcome = core.Handle(new WindowAppeared(new WindowId(3), Screen)
        {
            Traits = WindowTraits.Normal with { MinSize = new Size(1, 700) },
        });

        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(1100, 0, 900, 300), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(1100, 300, 900, 700), BoundsOf(outcome, 3));
    }

    [Fact]
    public void AMinimumTallerThanTheStackKeepsTheProportionalSplit()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        var outcome = core.Handle(new WindowAppeared(new WindowId(3), Screen)
        {
            Traits = WindowTraits.Normal with { MinSize = new Size(1, 1200) },
        });

        // The neighbour keeps its tile. A window cannot be smaller than its minimum, so the one taller
        // than the Monitor starts at the top of the work area.
        Assert.Equal(new Rectangle(1100, 0, 900, 500), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(1100, 0, 900, 1200), BoundsOf(outcome, 3));
    }

    [Fact]
    public void AMinimumAsTallAsTheStackLeavesItsNeighbourARow()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        var outcome = core.Handle(new WindowAppeared(new WindowId(3), Screen)
        {
            Traits = WindowTraits.Normal with { MinSize = new Size(1, 1000) },
        });

        Assert.True(BoundsOf(outcome, 2).Height > 0, $"window 2 is {BoundsOf(outcome, 2)}");
        Assert.Equal(new Rectangle(1100, 0, 900, 1000), BoundsOf(outcome, 3));
    }

    [Fact]
    public void MinimumsThatDoNotFitTheStackStayOnTheMonitor()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));
        core.Handle(new WindowAppeared(new WindowId(2), Screen)
        {
            Traits = WindowTraits.Normal with { MinSize = new Size(1, 600) },
        });

        var outcome = core.Handle(new WindowAppeared(new WindowId(3), Screen)
        {
            Traits = WindowTraits.Normal with { MinSize = new Size(1, 600) },
        });

        // Each window grows to its minimum around the middle of its tile, held inside the work area.
        Assert.Equal(new Rectangle(1100, 0, 900, 600), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(1100, 400, 900, 600), BoundsOf(outcome, 3));
    }

    [Fact]
    public void OneClientGrowsPastTheWorkAreaToMeetItsMinimum()
    {
        var core = CoreOf(Flat(""));

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Screen)
        {
            Traits = WindowTraits.Normal with { MinSize = new Size(2200, 100) },
        });

        Assert.Equal(new Rectangle(0, 0, 2200, 1000), BoundsOf(outcome, 1));
    }

    [Fact]
    public void ADraggedWindowDoesNotTakeTheMasterRoleFromNewStatus()
    {
        var core = CoreOf(Flat("master:new_status = master\nbindm = SUPER, mouse:272, movewindow\n"));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        Press(core, "Super_L");
        core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(1500, 500) });
        core.Handle(new PointerMoved(new Point(1500, 500)));
        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(1500, 500) });

        Assert.Equal(new Rectangle(1100, 0, 900, 1000), BoundsOf(dropped, 1));
        Assert.Equal(new Rectangle(0, 0, 1100, 1000), BoundsOf(dropped, 2));
    }

    [Fact]
    public void AStackResizeWithoutMinimumsGoesBelowAFifthShare()
    {
        var core = CoreOf(Flat("master:smart_resizing = false\n"));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));
        core.Handle(new Dispatch("movefocus", "u"));

        var outcome = core.Handle(new Dispatch("resizeactive", "0 -475"));

        Assert.Equal(new Rectangle(1100, 0, 900, 25), BoundsOf(outcome, 2));
        Assert.Equal(new Rectangle(1100, 25, 900, 975), BoundsOf(outcome, 3));
    }

    [Fact]
    public void CycleNextOnAFloatingClientSucceedsWithoutMovingFocus()
    {
        var core = CoreOf(Flat(""));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(new Dispatch("togglefloating"));

        var outcome = core.Handle(new Dispatch("layoutmsg", "cyclenext"));

        Assert.Null(outcome.Error);
        Assert.Null(outcome.Focus);
    }

    private static string Flat(string extra) =>
        "general:layout = master\ngeneral:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + extra;

    private static Outcome Open(string text, params long[] windows)
    {
        var core = CoreOf(text);
        Outcome outcome = null!;
        foreach (var window in windows)
            outcome = core.Handle(Appeared(window));

        return outcome;
    }

    private static Core CoreOf(string text)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(text)));
        return core;
    }

    private static WindowAppeared Appeared(long window) =>
        new(new WindowId(window), Screen);

    private static void Press(Core core, string key) =>
        core.Handle(new KeyPressed(key));

    private static Client ClientOf(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = ClientOf(outcome, window);
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }
}
