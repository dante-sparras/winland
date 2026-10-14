using FluentAssertions;
using Winland.Core;
using Winland.Core.Tiling;
namespace Winland.Tests;

[TestClass]
public sealed class BarLayerTests
{
    private static readonly TileRect Bar = new(0, 0, 1920, 48);

    [TestMethod]
    public void Fullscreen_on_the_bar_monitor_puts_the_bar_behind_and_the_panel_is_not_topmost()
    {
        var verdict = BarLayer.Decide(fullscreenOnBarMonitor: true, windowFillsBarScreen: false, topmostCrossesBar: false);

        verdict.BarInFront.Should().BeFalse();
        verdict.PanelTopmost.Should().BeFalse();
    }

    [TestMethod]
    public void A_filling_window_puts_the_bar_behind()
    {
        var verdict = BarLayer.Decide(fullscreenOnBarMonitor: false, windowFillsBarScreen: true, topmostCrossesBar: false);

        verdict.BarInFront.Should().BeFalse();
        verdict.PanelTopmost.Should().BeFalse();
    }

    [TestMethod]
    public void A_topmost_window_overlapping_by_13_pixels_on_both_axes_crosses_and_puts_the_bar_behind()
    {
        var crossings = new (TileRect Window, bool Crosses)[]
        {
            (new TileRect(0, 0, 13, 13), true),
        };

        AssertCrossings(crossings);
        var verdict = BarLayer.Decide(fullscreenOnBarMonitor: false, windowFillsBarScreen: false, topmostCrossesBar: true);

        verdict.BarInFront.Should().BeFalse();
        verdict.PanelTopmost.Should().BeFalse();
    }

    [TestMethod]
    public void An_overlap_of_12_pixels_on_one_axis_does_not_cross_and_the_bar_stays_in_front()
    {
        var crossings = new (TileRect Window, bool Crosses)[]
        {
            (new TileRect(0, 0, 12, 48), false),
            (new TileRect(0, 0, 1920, 12), false),
            (new TileRect(0, 0, 13, 12), false),
            (new TileRect(0, 0, 12, 13), false),
        };

        AssertCrossings(crossings);
        var verdict = BarLayer.Decide(fullscreenOnBarMonitor: false, windowFillsBarScreen: false, topmostCrossesBar: false);

        verdict.BarInFront.Should().BeTrue();
        verdict.PanelTopmost.Should().BeTrue();
    }

    [TestMethod]
    public void Nothing_covering_leaves_the_bar_in_front()
    {
        var crossings = new (TileRect Window, bool Crosses)[]
        {
            (new TileRect(2000, 80, 40, 40), false),
        };

        AssertCrossings(crossings);
        var verdict = BarLayer.Decide(fullscreenOnBarMonitor: false, windowFillsBarScreen: false, topmostCrossesBar: false);

        verdict.BarInFront.Should().BeTrue();
        verdict.PanelTopmost.Should().BeTrue();
    }

    [TestMethod]
    public void The_loop_looks_for_a_cover_every_turn_only_while_the_bar_is_covered_or_a_window_is_fullscreen()
    {
        BarLayer.LooksAgain(covered: true, fullscreenOnBarMonitor: false, sinceLookMs: 0).Should().BeTrue();
        BarLayer.LooksAgain(covered: false, fullscreenOnBarMonitor: true, sinceLookMs: 0).Should().BeTrue();
        BarLayer.LooksAgain(covered: false, fullscreenOnBarMonitor: false, sinceLookMs: BarLayer.StaleMs - 1).Should().BeFalse();
        BarLayer.LooksAgain(covered: false, fullscreenOnBarMonitor: false, sinceLookMs: BarLayer.StaleMs).Should().BeTrue();
    }

    private static void AssertCrossings((TileRect Window, bool Crosses)[] rows)
    {
        foreach (var row in rows)
        {
            BarLayer.Crosses(row.Window, Bar).Should().Be(row.Crosses);
        }
    }
}

