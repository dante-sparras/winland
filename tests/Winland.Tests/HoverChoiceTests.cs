using FluentAssertions;
using Winland.Core.Input;

namespace Winland.Tests;

[TestClass]
public sealed class HoverChoiceTests
{
    [TestMethod]
    public void The_pointer_focuses_the_tile_it_is_over()
    {
        HoverChoice.Target(new HoverFacts(Tile: 10, Drawn: 10, Focused: 11, ButtonHeld: false, SurfaceHeld: false)).Should().Be(10);
    }

    [TestMethod]
    public void The_focused_tile_is_left_alone()
    {
        HoverChoice.Target(new HoverFacts(Tile: 10, Drawn: 10, Focused: 10, ButtonHeld: false, SurfaceHeld: false)).Should().BeNull();
    }

    [TestMethod]
    public void A_window_drawn_over_the_tile_keeps_the_keyboard()
    {
        // The snipping overlay covers every tile. Its drag must not hand the keyboard to the tile underneath.
        HoverChoice.Target(new HoverFacts(Tile: 10, Drawn: 99, Focused: 11, ButtonHeld: false, SurfaceHeld: false)).Should().BeNull();
    }

    [TestMethod]
    public void A_drag_does_not_move_the_keyboard()
    {
        HoverChoice.Target(new HoverFacts(Tile: 10, Drawn: 10, Focused: 11, ButtonHeld: true, SurfaceHeld: false)).Should().BeNull();
    }

    [TestMethod]
    public void An_open_panel_keeps_the_keyboard()
    {
        HoverChoice.Target(new HoverFacts(Tile: 10, Drawn: 10, Focused: 11, ButtonHeld: false, SurfaceHeld: true)).Should().BeNull();
    }

    [TestMethod]
    public void No_tile_under_the_pointer_focuses_nothing()
    {
        HoverChoice.Target(new HoverFacts(Tile: null, Drawn: 10, Focused: 11, ButtonHeld: false, SurfaceHeld: false)).Should().BeNull();
    }
}
