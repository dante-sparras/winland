using Winland;
using Winland.Windows;

namespace Winland.Tests;

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
    public void ShadowOffDisablesNonClientRenderingAndOnOrResetPutsTheStylePolicyBack()
    {
        var surface = new FakeSurface();
        var effects = new DwmEffects(surface);

        effects.Apply(Window, Decorated());
        Assert.False(surface.Policies.ContainsKey(Window));

        effects.Apply(Window, Decorated(shadow: false));
        Assert.Equal(DwmEffects.DisabledPolicy, surface.Policies[Window]);

        effects.Apply(Window, Decorated());
        Assert.Equal(DwmEffects.StylePolicy, surface.Policies[Window]);

        effects.Apply(Window, Decorated(shadow: false));
        effects.Reset(Window);
        Assert.Equal(DwmEffects.StylePolicy, surface.Policies[Window]);
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
        effects.Apply(other, Decorated(blur: false, shadow: false));

        effects.ResetAll();

        Assert.Equal(2u, surface.Backdrops[Window]);
        Assert.Empty(surface.Regions[Window]);
        Assert.False(effects.CutsCorners(Window));
        Assert.Equal(DwmEffects.AutoBackdrop, surface.Backdrops[other]);
        Assert.Equal(DwmEffects.StylePolicy, surface.Policies[other]);
    }

    // After the exit cleanup the process ends with the state file gone, so an outcome that arrives
    // late must not change a backdrop, a shadow or a region again.
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

    private static Decoration Decorated(
        bool blur = true,
        bool shadow = true,
        CornerRegion? region = null,
        CornerPreference corners = CornerPreference.DoNotRound) =>
        new(new ClientId(1), new BorderColor(0xFF, 0, 0, 0), corners)
        {
            Blur = blur,
            Shadow = shadow,
            Region = region,
        };

    private sealed class FakeSurface : IDwmSurface
    {
        public Dictionary<WindowId, uint> Backdrops { get; } = [];

        public Dictionary<WindowId, uint> Policies { get; } = [];

        public Dictionary<WindowId, (Rectangle Outer, Rectangle Visible)> Frames { get; } = [];

        public Dictionary<WindowId, IReadOnlyList<Point>> Regions { get; } = [];

        public HashSet<WindowId> Owned { get; } = [];

        public bool ReadsBackdrop { get; init; } = true;

        public int BackdropWrites { get; private set; }

        public int RegionWrites { get; private set; }

        public bool TryReadBackdrop(WindowId window, out uint backdrop)
        {
            backdrop = Backdrops.GetValueOrDefault(window);
            return ReadsBackdrop;
        }

        public bool TrySetBackdrop(WindowId window, uint backdrop)
        {
            Backdrops[window] = backdrop;
            BackdropWrites++;
            return true;
        }

        public bool TrySetRenderingPolicy(WindowId window, uint policy)
        {
            Policies[window] = policy;
            return true;
        }

        public bool HasRegion(WindowId window) => Owned.Contains(window) || Regions.GetValueOrDefault(window) is { Count: > 0 };

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
