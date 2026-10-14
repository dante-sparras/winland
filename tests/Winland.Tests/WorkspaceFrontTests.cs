using FluentAssertions;
using Winland.Core.Workspaces;

namespace Winland.Tests;

[TestClass]
public sealed class WorkspaceFrontTests
{
    [TestMethod]
    public void After_a_replace_the_workspace_whose_windows_are_on_the_glass_is_in_front()
    {
        var onGlass = Counts((3, 2));
        var held = Counts((1, 4), (3, 2), (5, 1));

        WorkspaceFront.Pick(onGlass, held, keyboard: 0).Should().Be(3);
    }

    [TestMethod]
    public void After_a_crash_the_workspace_of_the_window_with_the_keyboard_is_in_front()
    {
        // The watcher showed every window, so every Workspace has its windows on the glass.
        var counts = Counts((1, 4), (2, 1), (5, 2));

        WorkspaceFront.Pick(counts, counts, keyboard: 2).Should().Be(2);
    }

    [TestMethod]
    public void Without_a_keyboard_the_workspace_with_the_most_windows_on_the_glass_is_in_front()
    {
        var counts = Counts((1, 1), (2, 3), (5, 2));

        WorkspaceFront.Pick(counts, counts, keyboard: 0).Should().Be(2);
    }

    [TestMethod]
    public void Among_equals_the_lowest_number_is_in_front()
    {
        var counts = Counts((2, 2), (5, 2));

        WorkspaceFront.Pick(counts, counts, keyboard: 0).Should().Be(2);
    }

    [TestMethod]
    public void A_keyboard_on_a_workspace_with_nothing_on_the_glass_does_not_count()
    {
        var onGlass = Counts((4, 1));
        var held = Counts((2, 1), (4, 1));

        WorkspaceFront.Pick(onGlass, held, keyboard: 2).Should().Be(4);
    }

    [TestMethod]
    public void With_no_window_on_the_glass_the_lowest_number_that_holds_none_is_in_front()
    {
        var held = Counts((1, 2), (2, 1), (4, 1));

        WorkspaceFront.Pick(Counts(), held, keyboard: 0).Should().Be(3);
    }

    [TestMethod]
    public void A_host_with_no_window_starts_on_workspace_1()
    {
        WorkspaceFront.Pick(Counts(), Counts(), keyboard: 0).Should().Be(1);
    }

    [TestMethod]
    public void Each_other_monitor_has_the_workspace_with_the_most_windows_on_the_glass_there_in_front()
    {
        // Workspaces 1 and 5 are on monitor 0, where the keyboard is. 2, 3, and 6 are on monitor 1, and 7 is on monitor 2.
        var onGlass = Counts((1, 1), (5, 4), (2, 1), (3, 2), (6, 2), (7, 0));
        int MonitorOf(int workspace) => workspace switch { 1 or 5 => 0, 7 => 2, _ => 1 };

        WorkspaceFront.PerMonitor(onGlass, MonitorOf, keyboard: 1).Should().Equal([3], "3 and 6 are equal, and the lowest number wins. Monitor 2 shows no window, and is given none");
    }

    private static int[] Counts(params (int Workspace, int Windows)[] pairs)
    {
        var counts = new int[WorkspaceBook.Maximum + 1];
        foreach (var (workspace, windows) in pairs)
        {
            counts[workspace] = windows;
        }

        return counts;
    }
}
