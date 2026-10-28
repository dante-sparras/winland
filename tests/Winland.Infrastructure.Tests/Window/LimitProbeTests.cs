namespace Winland.Infrastructure.Tests;

[Collection(DesktopCollection.Name)]
[Trait("Category", DesktopCollection.Name)]
public class LimitProbeTests
{
    [DesktopFact]
    public void RealWindowsFloatTheSmallMaximumAndKeepTheLargeMinimum()
    {
        _ = ProbeNative.SetProcessDpiAwarenessContext(new IntPtr(-4));
        var taskbar = ProbeNative.TaskbarVisible();
        using var probe = new LimitProbe();
        probe.Open();
        try
        {
            var area = new Rectangle(0, 0, 1920, 1080);
            var anchorLimits = Read(probe.Anchor);
            var wideLimits = Read(probe.Wide);
            var smallLimits = Read(probe.Small);
            var wideInsets = ProbeNative.Insets(probe.Wide);
            Assert.True(wideInsets.Left + wideInsets.Right > 0, "The probe window had no invisible frame.");
            Assert.Equal(1700 - wideInsets.Left - wideInsets.Right, wideLimits.Min!.Value.Width);
            Assert.Equal(200 - wideInsets.Top - wideInsets.Bottom, wideLimits.Min.Value.Height);
            Assert.True(smallLimits.Max!.Value.Width < area.Width);
            Assert.True(smallLimits.Max.Value.Height < area.Height);

            var anchorId = probe.Anchor.ToInt64();
            var wideId = probe.Wide.ToInt64();
            var smallId = probe.Small.ToInt64();
            var core = new WindowManager();
            using var adapter = new WindowAdapter(core);
            // The probe reads where each placement lands, so nothing animates on the way.
            core.ReducedMotion = () => true;
            core.Handle(new WindowAppeared(new WindowId(anchorId), area)
            {
                Traits = WindowTraits.Normal with { MinSize = anchorLimits.Min, MaxSize = anchorLimits.Max },
            });
            var tiled = core.Handle(new WindowAppeared(new WindowId(wideId), area)
            {
                Traits = WindowTraits.Normal with { MinSize = wideLimits.Min, MaxSize = wideLimits.Max },
            });
            var wideBox = BoundsOf(tiled, wideId);
            var anchorBox = BoundsOf(tiled, anchorId);
            Assert.True(wideBox.Width >= wideLimits.Min.Value.Width);
            Assert.Equal(area.Width - wideBox.Width, anchorBox.Width);
            Assert.True(anchorBox.Width < wideBox.Width);
            adapter.Apply(tiled);
            AssertClose(ProbeNative.Visible(probe.Wide), wideBox, "wide");
            AssertClose(ProbeNative.Visible(probe.Anchor), anchorBox, "anchor");

            var bounds = new Rectangle(80, 90, 200, 150);
            // It floats at the tile it would have taken, held to its maximum, in the middle of the area.
            var small = smallLimits.Max.Value;
            var centered = new Rectangle((area.Width - small.Width) / 2, (area.Height - small.Height) / 2, small.Width, small.Height);
            var floated = core.Handle(new WindowAppeared(new WindowId(smallId), area)
            {
                Traits = WindowTraits.Normal with { MinSize = smallLimits.Min, MaxSize = smallLimits.Max },
                Bounds = bounds,
            });
            Assert.True(Assert.Single(floated.State.Clients, client => client.Window == new WindowId(smallId)).Floating);
            Assert.Equal(centered, BoundsOf(floated, smallId));
            adapter.Apply(floated);
            var smallVisible = ProbeNative.Visible(probe.Small);
            AssertClose(smallVisible, centered, "small");
            Assert.True(smallVisible.Width < area.Width / 2, $"The small window filled a tile: {smallVisible.Width}");

            LimitProbe.SetMinimum(probe.Wide, 1850, 200);
            var grown = Read(probe.Wide);
            Assert.True(grown.Min!.Value.Width > wideLimits.Min.Value.Width);
            var updated = core.Handle(new WindowLimitsChanged(new WindowId(wideId), grown.Min, grown.Max));
            var grownBox = BoundsOf(updated, wideId);
            Assert.True(grownBox.Width >= grown.Min.Value.Width);
            Assert.True(grownBox.Width > wideBox.Width);
            adapter.Apply(updated);
            AssertClose(ProbeNative.Visible(probe.Wide), grownBox, "grown");
            Assert.True(ProbeNative.Visible(probe.Anchor).Width < anchorBox.Width);
        }
        finally
        {
            probe.Close();
            Assert.Equal(taskbar, ProbeNative.TaskbarVisible());
        }
    }

    private static (Size? Min, Size? Max) Read(IntPtr hwnd)
    {
        Assert.True(WindowLimits.TryRead(hwnd, ProbeNative.Insets(hwnd), out var min, out var max), "The track size was not read.");
        return (min, max);
    }

    private static void AssertClose(Rectangle actual, Rectangle expected, string name)
    {
        Assert.True(Math.Abs(actual.X - expected.X) <= 12, $"{name} x {actual.X} expected {expected.X}");
        Assert.True(Math.Abs(actual.Y - expected.Y) <= 12, $"{name} y {actual.Y} expected {expected.Y}");
        Assert.True(Math.Abs(actual.Width - expected.Width) <= 12, $"{name} width {actual.Width} expected {expected.Width}");
        Assert.True(Math.Abs(actual.Height - expected.Height) <= 12, $"{name} height {actual.Height} expected {expected.Height}");
    }

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        var placement = Assert.Single(outcome.Placements, candidate => candidate.Client == client.Id);
        return placement.Bounds;
    }
}
