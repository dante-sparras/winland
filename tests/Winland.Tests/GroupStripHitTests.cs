using FluentAssertions;
using Winland.Core.Tiling;

namespace Winland.Tests;

[TestClass]
public sealed class GroupStripHitTests
{
    [TestMethod]
    public void A_click_lands_on_the_tab_that_shares_the_width()
    {
        GroupStripHit.Tab(0, 300, 3).Should().Be(0);
        GroupStripHit.Tab(100, 300, 3).Should().Be(1);
        GroupStripHit.Tab(299, 300, 3).Should().Be(2);
        GroupStripHit.Tab(300, 300, 3).Should().Be(2);
        GroupStripHit.Tab(-1, 300, 3).Should().Be(-1);
        GroupStripHit.Tab(10, 300, 0).Should().Be(-1);
        GroupStripHit.Tab(10, 0, 3).Should().Be(-1);
    }

    [TestMethod]
    public void The_strip_is_28_dips_tall()
    {
        GroupStripMetrics.HeightDips.Should().Be(28);
        GroupStripMetrics.Pixels(96).Should().Be(28);
        GroupStripMetrics.Pixels(0).Should().Be(28);
        GroupStripMetrics.Pixels(144).Should().Be(42);
        GroupStripMetrics.Pixels(192).Should().Be(56);
    }
}
