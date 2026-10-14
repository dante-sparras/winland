using FluentAssertions;
using Winland.Core;
using Winland.Core.Tiling;

namespace Winoma.Tests;

[TestClass]
public sealed class BarSetTests
{
    private static readonly TileRect Left = new(0, 0, 1920, 1080);
    private static readonly TileRect Right = new(1920, 0, 1920, 1080);

    [TestMethod]
    public void The_bar_on_a_monitor_is_the_one_whose_monitor_it_is()
    {
        BarSet.On([Left, Right], Right).Should().Be(1);
        BarSet.On([Right, Left], Right).Should().Be(0);
    }

    [TestMethod]
    public void A_monitor_that_changed_size_is_still_its_bars_and_one_no_bar_is_on_falls_to_the_first()
    {
        BarSet.On([Left, Right], new TileRect(1920, 0, 2560, 1440)).Should().Be(1, "the Bar that covers the most of it");
        BarSet.On([Left, Right], new TileRect(0, 5000, 800, 600)).Should().Be(0);
        BarSet.On([], Left).Should().Be(0);
    }

    [TestMethod]
    public void A_monitor_with_no_bar_gets_one_and_a_bar_on_its_monitor_stays()
    {
        var plan = BarSet.Follow([Left], [Left, Right]);

        plan.Kept.Should().Equal(Left);
        plan.Made.Should().Equal(Right);
        plan.Changed.Should().BeTrue();
    }

    [TestMethod]
    public void Bars_that_are_each_on_a_listed_monitor_are_left_alone()
    {
        var plan = BarSet.Follow([Right, Left], [Left, Right]);

        plan.Kept.Should().Equal(Right, Left);
        plan.Made.Should().BeEmpty();
        plan.Changed.Should().BeFalse();
    }

    [TestMethod]
    public void A_monitor_that_goes_takes_its_bar_with_it()
    {
        var plan = BarSet.Follow([Left, Right], [Left]);

        plan.Kept.Should().Equal(Left, null);
        plan.Made.Should().BeEmpty();
        plan.Changed.Should().BeTrue();
    }

    [TestMethod]
    public void The_first_bar_stays_when_its_monitor_goes_and_the_newest_is_removed()
    {
        // The desktop started on the left monitor, and that monitor is unplugged. Every Bar is the same, so the first moves over.
        var plan = BarSet.Follow([Left, Right], [Right]);

        plan.Kept.Should().Equal(Right, null);
        plan.Made.Should().BeEmpty();
        plan.Changed.Should().BeTrue();
    }

    [TestMethod]
    public void A_monitor_at_a_new_size_keeps_its_bar()
    {
        var larger = new TileRect(1920, 0, 2560, 1440);

        var plan = BarSet.Follow([Left, Right], [Left, larger]);

        plan.Kept.Should().Equal(Left, larger);
        plan.Made.Should().BeEmpty();
        plan.Changed.Should().BeTrue("the Bar is placed again on its monitor as it is now");
    }

    [TestMethod]
    public void A_bar_that_lost_its_monitor_takes_the_one_that_covers_where_it_was()
    {
        // Both monitors changed: the left one is larger, so the right one starts further right.
        var left = new TileRect(0, 0, 2560, 1440);
        var right = new TileRect(2560, 0, 1920, 1080);

        var plan = BarSet.Follow([Right, Left], [left, right]);

        plan.Kept.Should().Equal(right, left);
        plan.Made.Should().BeEmpty();
    }

    [TestMethod]
    public void A_desktop_that_starts_has_no_bar_and_gets_one_for_each_monitor()
    {
        var plan = BarSet.Follow([], [Left, Right]);

        plan.Kept.Should().BeEmpty();
        plan.Made.Should().Equal(Left, Right);
    }

    [TestMethod]
    public void With_no_monitor_listed_every_bar_stays()
    {
        var plan = BarSet.Follow([Left, Right], []);

        plan.Kept.Should().Equal(Left, Right);
        plan.Made.Should().BeEmpty();
        plan.Changed.Should().BeFalse();
    }
}
