using FluentAssertions;
using Winland.Core.Input;

namespace Winland.Tests;

[TestClass]
public sealed class PointerYieldTests
{
    [TestMethod]
    public void A_key_stops_the_pointer_from_choosing_the_window()
    {
        var yield = new PointerYield();

        yield.Pressed().Should().BeTrue();
        yield.Yielded.Should().BeTrue();
        yield.Pressed().Should().BeFalse();
    }

    [TestMethod]
    public void A_move_while_a_key_is_held_does_not_give_the_pointer_back()
    {
        var yield = new PointerYield();
        yield.Pressed();

        yield.Moved(keyHeld: true).Should().BeFalse();
        yield.Yielded.Should().BeTrue();
    }

    [TestMethod]
    public void A_move_with_every_key_up_gives_the_pointer_back()
    {
        var yield = new PointerYield();
        yield.Pressed();

        yield.Moved(keyHeld: false).Should().BeTrue();
        yield.Yielded.Should().BeFalse();
        yield.Moved(keyHeld: false).Should().BeFalse();
    }

    [TestMethod]
    public void A_move_before_any_key_changes_nothing()
    {
        var yield = new PointerYield();

        yield.Moved(keyHeld: false).Should().BeFalse();
        yield.Yielded.Should().BeFalse();
    }
}
