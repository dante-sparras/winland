using Winland.Core;
using Winland.Windows;

namespace Winland.Windows.Tests;

public class DwmEffectsTests
{
    private static readonly WindowId Window = new(42);

    [Fact]
    public void BlurSetsTheBlurBackdropAndResetRestoresTheOriginalOne()
    {
        var surface = new FakeSurface();
        surface.Backdrops[Window] = 2;
        var effects = new DwmEffects(surface);

        effects.Apply(Window, Decorated(blur: true));
        Assert.Equal(DwmEffects.BlurBackdrop, surface.Backdrops[Window]);

        effects.Apply(Window, Decorated(blur: false));
        Assert.Equal(DwmEffects.NoBackdrop, surface.Backdrops[Window]);

        effects.Apply(Window, Decorated(blur: false));
        Assert.Equal(2, surface.BackdropWrites);

        effects.Reset(Window);
        Assert.Equal(2u, surface.Backdrops[Window]);
    }

    [Fact]
    public void AnUnreadableBackdropIsRestoredToTheSystemChoice()
    {
        var surface = new FakeSurface { ReadsBackdrop = false };
        var effects = new DwmEffects(surface);

        effects.Apply(Window, Decorated(blur: true));
        effects.Reset(Window);

        Assert.Equal(DwmEffects.AutoBackdrop, surface.Backdrops[Window]);
    }

    [Fact]
    public void ARegionIsCutInsideTheVisibleFrameAndClearedOnReset()
    {
        var surface = new FakeSurface();
        surface.Frames[Window] = (new Rectangle(93, 100, 214, 207), new Rectangle(100, 100, 200, 200));
        var effects = new DwmEffects(surface);

        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));

        var outline = surface.Regions[Window];
        Assert.Equal(new Point(7, 10), outline[0]);
        Assert.All(outline, point => Assert.InRange(point.X, 7, 207));
        Assert.All(outline, point => Assert.InRange(point.Y, 0, 200));
        Assert.True(effects.CutsCorners(Window));

        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));
        Assert.Equal(1, surface.RegionWrites);

        effects.Reset(Window);
        Assert.Empty(surface.Regions[Window]);
        Assert.False(effects.CutsCorners(Window));
    }

    [Fact]
    public void RefitFollowsANewSizeAndADwmCornerClearsTheRegion()
    {
        var surface = new FakeSurface();
        surface.Frames[Window] = (new Rectangle(0, 0, 200, 200), new Rectangle(0, 0, 200, 200));
        var effects = new DwmEffects(surface);
        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));

        effects.Refit(Window);
        Assert.Equal(1, surface.RegionWrites);

        surface.Frames[Window] = (new Rectangle(0, 0, 300, 100), new Rectangle(0, 0, 300, 100));
        effects.Refit(Window);
        Assert.Equal(2, surface.RegionWrites);
        Assert.Contains(new Point(300, 90), surface.Regions[Window]);

        effects.Apply(Window, Decorated(corners: CornerPreference.Round));
        Assert.Empty(surface.Regions[Window]);
        Assert.False(effects.CutsCorners(Window));
    }

    [Fact]
    public void AWindowWithItsOwnRegionKeepsItAndBorderlessCutsNothing()
    {
        var surface = new FakeSurface();
        surface.Frames[Window] = (new Rectangle(0, 0, 200, 200), new Rectangle(0, 0, 200, 200));
        surface.Owned.Add(Window);
        var effects = new DwmEffects(surface);

        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));
        effects.Reset(Window);
        Assert.Equal(0, surface.RegionWrites);

        var other = new WindowId(7);
        surface.Frames[other] = (new Rectangle(0, 0, 200, 200), new Rectangle(0, 0, 200, 200));
        effects.Apply(other, Decorated(region: new CornerRegion(10, 2f)) with { Borderless = true });
        Assert.Equal(0, surface.RegionWrites);
    }

    [Fact]
    public void ResettingAllPutsBackEveryWindowItChanged()
    {
        var surface = new FakeSurface();
        surface.Backdrops[Window] = 2;
        surface.Frames[Window] = (new Rectangle(0, 0, 200, 200), new Rectangle(0, 0, 200, 200));
        var other = new WindowId(8);
        var effects = new DwmEffects(surface);
        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));
        effects.Apply(other, Decorated(blur: false));

        effects.ResetAll();

        Assert.Equal(2u, surface.Backdrops[Window]);
        Assert.Empty(surface.Regions[Window]);
        Assert.False(effects.CutsCorners(Window));
        Assert.Equal(DwmEffects.AutoBackdrop, surface.Backdrops[other]);
    }

    // After the exit cleanup the process ends with the state file gone, so an outcome that arrives
    // late must not change a backdrop or a region again.
    [Fact]
    public void AfterResettingAllNothingIsChangedUntilReopened()
    {
        var surface = new FakeSurface();
        surface.Backdrops[Window] = 2;
        surface.Frames[Window] = (new Rectangle(0, 0, 200, 200), new Rectangle(0, 0, 200, 200));
        var effects = new DwmEffects(surface);
        effects.ResetAll();

        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));

        Assert.Equal(2u, surface.Backdrops[Window]);
        Assert.Equal(0, surface.RegionWrites);
        Assert.False(effects.CutsCorners(Window));

        effects.Reopen();
        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));
        Assert.True(effects.CutsCorners(Window));
    }

    [Fact]
    public void ACutRegionIsRecordedAndAfterACrashRemovedSoItIsNotTakenAsTheWindowsOwn()
    {
        var surface = new FakeSurface();
        surface.Frames[Window] = (new Rectangle(0, 0, 200, 200), new Rectangle(0, 0, 200, 200));
        var effects = new DwmEffects(surface);
        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));

        var stamped = effects.Stamp(new RecoverySnapshot(
        [
            new RecoveredClient(Window, WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1),
            new RecoveredClient(new WindowId(9), WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1),
        ]));
        Assert.True(stamped.Clients[0].Region);
        Assert.False(stamped.Clients[1].Region);

        // The region stays on the window when the process dies, and the next run would see it as the window's own.
        var restarted = new DwmEffects(surface);
        restarted.DropLeftoverRegion(Window);
        Assert.Empty(surface.Regions[Window]);

        restarted.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));
        Assert.True(restarted.CutsCorners(Window));
    }

    [Fact]
    public void TheRegionIsShiftedByTheInvisibleFrameOnEachSide()
    {
        var surface = new FakeSurface();
        surface.Frames[Window] = (new Rectangle(90, 80, 220, 230), new Rectangle(100, 100, 200, 200));
        var effects = new DwmEffects(surface);

        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));

        Assert.Equal(new Point(10, 30), surface.Regions[Window][0]);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(200, 0)]
    public void AFrameWithNothingVisibleIsNotCut(int width, int height)
    {
        var surface = new FakeSurface();
        surface.Frames[Window] = (new Rectangle(0, 0, 200, 200), new Rectangle(0, 0, width, height));
        var effects = new DwmEffects(surface);

        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));

        Assert.Equal(0, surface.RegionWrites);
        Assert.False(effects.CutsCorners(Window));
    }

    // A rule can change the radius of a window whose corners are already cut.
    [Fact]
    public void ANewRadiusCutsTheWindowAgain()
    {
        var surface = new FakeSurface();
        surface.Frames[Window] = (new Rectangle(0, 0, 200, 200), new Rectangle(0, 0, 200, 200));
        var effects = new DwmEffects(surface);

        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));
        effects.Apply(Window, Decorated(region: new CornerRegion(20, 2f)));
        effects.Apply(Window, Decorated(region: new CornerRegion(30, 2f)));

        Assert.Equal(3, surface.RegionWrites);
        Assert.Equal(1, surface.RegionChecks);
        Assert.True(effects.CutsCorners(Window));
    }

    // Whether a window has a region of its own is asked once. A reset forgets the answer.
    [Fact]
    public void AWindowsOwnRegionIsAskedAboutOnceUntilReset()
    {
        var surface = new FakeSurface();
        surface.Frames[Window] = (new Rectangle(0, 0, 200, 200), new Rectangle(0, 0, 200, 200));
        surface.Owned.Add(Window);
        var effects = new DwmEffects(surface);

        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));
        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));
        var checks = surface.RegionChecks;
        effects.Reset(Window);
        surface.Owned.Remove(Window);
        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));

        Assert.Equal(1, checks);
        Assert.True(effects.CutsCorners(Window));
    }

    [Fact]
    public void ResettingAllForgetsAWindowsOwnRegion()
    {
        var surface = new FakeSurface();
        surface.Frames[Window] = (new Rectangle(0, 0, 200, 200), new Rectangle(0, 0, 200, 200));
        surface.Owned.Add(Window);
        var effects = new DwmEffects(surface);
        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));

        effects.ResetAll();
        effects.Reopen();
        surface.Owned.Remove(Window);
        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));

        Assert.True(effects.CutsCorners(Window));
    }

    // A window whose backdrop could not be set still gets its region back.
    [Fact]
    public void ResettingAllReachesAWindowWhoseBackdropWasNeverSet()
    {
        var surface = new FakeSurface { SetsBackdrop = false };
        surface.Frames[Window] = (new Rectangle(0, 0, 200, 200), new Rectangle(0, 0, 200, 200));
        var effects = new DwmEffects(surface);
        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));

        effects.ResetAll();

        Assert.Empty(surface.Regions[Window]);
        Assert.False(effects.CutsCorners(Window));
    }

    [Fact]
    public void DroppingALeftoverRegionForgetsWhatThisRunKnew()
    {
        var surface = new FakeSurface();
        surface.Frames[Window] = (new Rectangle(0, 0, 200, 200), new Rectangle(0, 0, 200, 200));
        var owned = new WindowId(5);
        surface.Frames[owned] = (new Rectangle(0, 0, 200, 200), new Rectangle(0, 0, 200, 200));
        surface.Owned.Add(owned);
        var effects = new DwmEffects(surface);
        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));
        effects.Apply(owned, Decorated(region: new CornerRegion(10, 2f)));

        effects.DropLeftoverRegion(Window);
        effects.DropLeftoverRegion(owned);
        surface.Owned.Remove(owned);
        effects.Apply(owned, Decorated(region: new CornerRegion(10, 2f)));

        Assert.False(effects.CutsCorners(Window));
        Assert.Empty(surface.Regions[Window]);
        Assert.True(effects.CutsCorners(owned));
    }

    // The snapshot is replaced only when a mark changes, and every changed mark is taken.
    [Fact]
    public void StampChangesOnlyTheMarksThatAreWrong()
    {
        var surface = new FakeSurface();
        surface.Frames[Window] = (new Rectangle(0, 0, 200, 200), new Rectangle(0, 0, 200, 200));
        var effects = new DwmEffects(surface);
        effects.Apply(Window, Decorated(region: new CornerRegion(10, 2f)));
        var right = new RecoverySnapshot(
        [
            new RecoveredClient(Window, WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1) { Region = true },
            new RecoveredClient(new WindowId(9), WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1),
        ]);
        var wrong = new RecoverySnapshot(
        [
            new RecoveredClient(new WindowId(10), WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1) { Region = true },
            new RecoveredClient(new WindowId(11), WorkspaceId.Initial, MonitorId.Primary, false, "", "", 1) { Region = true },
        ]);

        var unchanged = effects.Stamp(right);
        var corrected = effects.Stamp(wrong);

        Assert.Same(right, unchanged);
        Assert.All(corrected.Clients, client => Assert.False(client.Region));
        Assert.Throws<ArgumentNullException>(() => effects.Stamp(null!));
        Assert.Throws<ArgumentNullException>(() => new DwmEffects(null!));
    }

    private static Decoration Decorated(
        bool blur = true,
        CornerRegion? region = null,
        CornerPreference corners = CornerPreference.DoNotRound) =>
        new(new ClientId(1), new BorderColor(0xFF, 0, 0, 0), corners)
        {
            Blur = blur,
            Region = region,
        };

    private sealed class FakeSurface : IDwmSurface
    {
        public Dictionary<WindowId, uint> Backdrops { get; } = [];

        public Dictionary<WindowId, (Rectangle Outer, Rectangle Visible)> Frames { get; } = [];

        public Dictionary<WindowId, IReadOnlyList<Point>> Regions { get; } = [];

        public HashSet<WindowId> Owned { get; } = [];

        public bool ReadsBackdrop { get; init; } = true;

        public bool SetsBackdrop { get; init; } = true;

        public int RegionChecks { get; private set; }

        public int BackdropWrites { get; private set; }

        public int RegionWrites { get; private set; }

        public bool TryReadBackdrop(WindowId window, out uint backdrop)
        {
            backdrop = Backdrops.GetValueOrDefault(window);
            return ReadsBackdrop;
        }

        public bool TrySetBackdrop(WindowId window, uint backdrop)
        {
            if (!SetsBackdrop)
                return false;

            Backdrops[window] = backdrop;
            BackdropWrites++;
            return true;
        }

        public bool HasRegion(WindowId window)
        {
            RegionChecks++;
            return Owned.Contains(window) || Regions.GetValueOrDefault(window) is { Count: > 0 };
        }

        public bool TryFrame(WindowId window, out Rectangle outer, out Rectangle visible)
        {
            (outer, visible) = Frames.GetValueOrDefault(window);
            return Frames.ContainsKey(window);
        }

        public bool TrySetRegion(WindowId window, IReadOnlyList<Point> outline)
        {
            Regions[window] = outline;
            if (outline.Count > 0)
                RegionWrites++;
            return true;
        }
    }
}
