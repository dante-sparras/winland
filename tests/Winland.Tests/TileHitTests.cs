using FluentAssertions;
using Winland.Core.Tiling;

namespace Winland.Tests;

[TestClass]
public sealed class TileHitTests
{
    [TestMethod]
    public void The_pointer_hits_the_tile_it_is_over()
    {
        var placed = new List<Placement>
        {
            new(1, 0, 0, 100, 80, true),
            new(2, 100, 0, 100, 80, true),
        };

        TileHit.At(placed, 10, 10).Should().Be(1);
        TileHit.At(placed, 150, 10).Should().Be(2);
        TileHit.At(placed, 100, 10).Should().Be(2);
        TileHit.At(placed, 250, 10).Should().BeNull();
    }

    [TestMethod]
    public void A_raised_window_wins_over_the_tile_under_it()
    {
        var placed = new List<Placement>
        {
            new(1, 0, 0, 100, 80, true),
            new(2, 0, 0, 200, 80, true, true),
        };

        TileHit.At(placed, 10, 10).Should().Be(2);
    }
}
