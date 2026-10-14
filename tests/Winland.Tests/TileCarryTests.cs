using FluentAssertions;
using Winland.Core.Tiling;

namespace Winland.Tests;

[TestClass]
public sealed class TileCarryTests
{
    [TestMethod]
    public void Super_and_a_press_on_the_tile_drawn_there_claim_it()
    {
        TileCarry.Claims(superHeld: true, tile: 7, drawn: 7).Should().BeTrue();
    }

    [TestMethod]
    public void A_press_without_super_is_left_to_the_window()
    {
        TileCarry.Claims(superHeld: false, tile: 7, drawn: 7).Should().BeFalse();
    }

    [TestMethod]
    public void A_dialog_over_the_tile_keeps_its_press()
    {
        TileCarry.Claims(superHeld: true, tile: 7, drawn: 9).Should().BeFalse();
    }

    [TestMethod]
    public void A_press_outside_every_tile_is_not_claimed()
    {
        TileCarry.Claims(superHeld: true, tile: null, drawn: 9).Should().BeFalse();
    }

    [TestMethod]
    public void Super_and_a_press_on_a_floated_or_sticky_window_claim_it_over_a_tile_or_over_none()
    {
        TileCarry.Claims(superHeld: true, tile: 7, drawn: 9, floated: true).Should().BeTrue();
        TileCarry.Claims(superHeld: true, tile: null, drawn: 9, floated: true).Should().BeTrue();
        TileCarry.Claims(superHeld: false, tile: null, drawn: 9, floated: true).Should().BeFalse();
        TileCarry.Claims(superHeld: true, tile: null, drawn: null, floated: true).Should().BeFalse();
    }

    [TestMethod]
    public void Letting_go_names_the_rectangle_the_window_was_carried_to()
    {
        var carry = new TileCarry();
        carry.Press(9, 100, 100, new TileRect(40, 50, 400, 300));

        carry.Release(300, 120).Should().Be(new TileDrag(9, Drop: true, 300, 120, 0, 0, new TileRect(240, 70, 400, 300)));
    }
}
