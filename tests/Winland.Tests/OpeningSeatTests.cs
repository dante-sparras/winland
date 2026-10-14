using FluentAssertions;
using Winland.Core.Tiling;

namespace Winland.Tests;

[TestClass]
public sealed class OpeningSeatTests
{
    private static readonly TileRect Left = new(0, 0, 800, 600);
    private static readonly TileRect Right = new(800, 0, 800, 600);

    [TestMethod]
    public void A_window_on_the_target_monitor_stays()
    {
        OpeningSeat.On(new TileRect(900, 50, 400, 300), [Left, Right], Right).Should().BeNull();
    }

    [TestMethod]
    public void A_window_on_another_monitor_keeps_its_distance_from_the_corner()
    {
        OpeningSeat.On(new TileRect(100, 50, 400, 300), [Left, Right], Right).Should().Be(new TileRect(900, 50, 400, 300));
    }

    [TestMethod]
    public void A_window_larger_than_the_target_is_cut_to_it_and_one_past_its_edge_is_brought_in()
    {
        var small = new TileRect(800, 0, 400, 300);

        OpeningSeat.On(new TileRect(0, 0, 800, 600), [Left, small], small).Should().Be(small);
        OpeningSeat.On(new TileRect(600, 350, 300, 200), [Left, Right], Right).Should().Be(new TileRect(1300, 350, 300, 200));
    }

    [TestMethod]
    public void A_window_on_no_monitor_takes_the_middle()
    {
        OpeningSeat.On(new TileRect(-5000, -5000, 400, 300), [Left, Right], Right).Should().Be(new TileRect(1000, 150, 400, 300));
    }
}
