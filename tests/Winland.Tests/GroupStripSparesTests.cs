using FluentAssertions;
using Winland.Core.Tiling;

namespace Winland.Tests;

[TestClass]
public sealed class GroupStripSparesTests
{
    [TestMethod]
    public void One_hidden_strip_is_kept_however_long_ago_it_was_shown()
    {
        GroupStripSpares.Closes(held: 1, shown: 0, hiddenForMs: 100 * GroupStripSpares.QuietMs).Should().BeFalse();
        GroupStripSpares.Closes(held: 3, shown: 2, hiddenForMs: 100 * GroupStripSpares.QuietMs).Should().BeFalse();
    }

    [TestMethod]
    public void A_second_hidden_strip_closes_once_it_has_been_hidden_long_enough()
    {
        GroupStripSpares.Closes(held: 3, shown: 1, hiddenForMs: GroupStripSpares.QuietMs - 1).Should().BeFalse();

        GroupStripSpares.Closes(held: 3, shown: 1, hiddenForMs: GroupStripSpares.QuietMs).Should().BeTrue();
    }

    [TestMethod]
    public void A_switch_away_from_a_workspace_with_groups_closes_nothing_at_once()
    {
        GroupStripSpares.Closes(held: 4, shown: 0, hiddenForMs: 0).Should().BeFalse();
    }
}
