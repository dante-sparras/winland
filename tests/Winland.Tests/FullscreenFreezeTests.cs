using FluentAssertions;
using Winland.Core.Tiling;

namespace Winland.Tests;

[TestClass]
public sealed class FullscreenFreezeTests
{
    private static readonly Placement[] Tiles =
    [
        new(7, 0, 48, 500, 752, true, false),
        new(8, 500, 48, 500, 752, true, false),
        new(9, 0, 0, 1000, 800, true, true),
        new(10, 0, 0, 0, 0, false, false),
    ];

    [TestMethod]
    public void F11_on_a_tile_that_is_fullscreen_in_its_app_freezes_the_screen()
    {
        FullscreenFreeze.Takes(7, Tiles, inApp: true, modified: false).Should().BeTrue();
    }

    [TestMethod]
    public void F11_that_makes_a_tile_fullscreen_goes_straight_to_the_app()
    {
        FullscreenFreeze.Takes(7, Tiles, inApp: false, modified: false).Should().BeFalse();
    }

    [TestMethod]
    public void F11_with_a_modifier_goes_straight_to_the_app()
    {
        FullscreenFreeze.Takes(7, Tiles, inApp: true, modified: true).Should().BeFalse();
    }

    [TestMethod]
    public void F11_on_a_window_that_is_not_a_tile_goes_straight_to_the_app()
    {
        FullscreenFreeze.Takes(42, Tiles, inApp: true, modified: false).Should().BeFalse();
        FullscreenFreeze.Takes(null, Tiles, inApp: true, modified: false).Should().BeFalse();
    }

    [TestMethod]
    public void F11_on_the_window_super_f_made_fullscreen_goes_straight_to_the_app()
    {
        FullscreenFreeze.Takes(9, Tiles, inApp: true, modified: false).Should().BeFalse();
    }

    [TestMethod]
    public void F11_on_a_hidden_tile_goes_straight_to_the_app()
    {
        FullscreenFreeze.Takes(10, Tiles, inApp: true, modified: false).Should().BeFalse();
    }

    [TestMethod]
    public void The_picture_stays_while_the_window_still_moves()
    {
        FullscreenFreeze.Lifts(sinceKeyMs: 300, sinceMoveMs: 40, onTile: true).Should().BeFalse();
    }

    [TestMethod]
    public void The_picture_stays_while_the_window_is_off_its_tile()
    {
        FullscreenFreeze.Lifts(sinceKeyMs: 300, sinceMoveMs: 200, onTile: false).Should().BeFalse();
    }

    [TestMethod]
    public void The_picture_lifts_once_the_window_settles_on_its_tile()
    {
        FullscreenFreeze.Lifts(sinceKeyMs: 300, sinceMoveMs: FullscreenFreeze.SettleMs, onTile: true).Should().BeTrue();
    }

    [TestMethod]
    public void The_app_gets_the_shortest_freeze_to_answer_the_key()
    {
        FullscreenFreeze.Lifts(sinceKeyMs: 100, sinceMoveMs: 500, onTile: true).Should().BeFalse();
    }

    [TestMethod]
    public void The_picture_lifts_after_the_longest_freeze_whatever_the_window_does()
    {
        FullscreenFreeze.Lifts(sinceKeyMs: FullscreenFreeze.LongestMs, sinceMoveMs: 0, onTile: false).Should().BeTrue();
    }
}
