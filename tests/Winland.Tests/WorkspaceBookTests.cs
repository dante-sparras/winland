using FluentAssertions;
using Winland.Core.Workspaces;

namespace Winland.Tests;

[TestClass]
public sealed class WorkspaceBookTests
{
    [TestMethod]
    public void Starts_on_workspace_1_and_lists_the_first_four()
    {
        var book = new WorkspaceBook();

        book.Current.Should().Be(1);
        Listed(book).Should().Equal(1, 2, 3, 4);
        Existing(book).Should().Equal(1);
    }

    [TestMethod]
    public void Jumping_to_seven_makes_just_that_one()
    {
        var book = new WorkspaceBook();

        book.Jump(7);

        book.Current.Should().Be(7);
        book.Former.Should().Be(1);
        Listed(book).Should().Equal(1, 2, 3, 4, 7);
        book.Exists(5).Should().BeFalse();
        book.Exists(6).Should().BeFalse();
    }

    [TestMethod]
    public void An_empty_workspace_stops_existing_once_another_is_in_front()
    {
        var book = new WorkspaceBook();
        book.Jump(7);

        book.Jump(1);

        book.Exists(7).Should().BeFalse();
        Listed(book).Should().Equal(1, 2, 3, 4);
    }

    [TestMethod]
    public void A_workspace_that_holds_a_window_exists_while_another_is_in_front()
    {
        var book = new WorkspaceBook();
        book.Jump(7);
        book.SetOccupancy(7, 1);

        book.Jump(1);

        book.Exists(7).Should().BeTrue();
        Listed(book).Should().Equal(1, 2, 3, 4, 7);
        book.Occupancy(5).Should().Be(0);
    }

    [TestMethod]
    public void Next_and_previous_visit_only_the_workspaces_that_exist_and_wrap()
    {
        var book = new WorkspaceBook();
        book.SetOccupancy(1, 2);
        book.SetOccupancy(7, 1);
        book.Jump(3);

        book.Next().Should().Be(7);
        book.Previous().Should().Be(1);

        book.Jump(7);

        book.Next().Should().Be(1, "3 is empty and no longer in front");
        book.Previous().Should().Be(1);
    }

    [TestMethod]
    public void Next_stays_on_the_only_workspace_that_exists()
    {
        var book = new WorkspaceBook();
        book.Jump(2);

        book.Next().Should().Be(2);
        book.Previous().Should().Be(2);
    }

    [TestMethod]
    public void A_number_no_chord_reaches_is_refused()
    {
        var book = new WorkspaceBook();

        var jump = () => book.Jump(10);

        jump.Should().Throw<ArgumentOutOfRangeException>();
        book.Exists(0).Should().BeFalse();
        book.Exists(10).Should().BeFalse();
    }

    [TestMethod]
    public void A_workspace_in_front_on_another_monitor_exists_while_it_is_empty_and_stops_when_it_is_no_longer_in_front()
    {
        var book = new WorkspaceBook();
        book.SeveralInFront.Should().BeFalse();

        book.SetFront([1, 6]);

        book.InFront(6).Should().BeTrue();
        book.SeveralInFront.Should().BeTrue();
        Existing(book).Should().Equal(1, 6);
        book.Next().Should().Be(6, "the cycle visits every Workspace that exists, on every monitor");

        book.SetFront([2]);

        book.InFront(1).Should().BeTrue("the Workspace with the keyboard is in front whatever is named");
        Existing(book).Should().Equal(1, 2);
    }

    [TestMethod]
    public void The_keyboard_going_to_a_workspace_in_front_on_another_monitor_leaves_both_in_front_once_the_fronts_are_named()
    {
        var book = new WorkspaceBook();
        book.SetFront([1, 2]);

        book.Jump(2);
        book.SetFront([1, 2]);

        book.Current.Should().Be(2);
        book.Former.Should().Be(1);
        book.InFront(1).Should().BeTrue();
        book.InFront(2).Should().BeTrue();
    }

    private static List<int> Listed(WorkspaceBook book) =>
        [.. Enumerable.Range(1, WorkspaceBook.Maximum).Where(book.Listed)];

    private static List<int> Existing(WorkspaceBook book) =>
        [.. Enumerable.Range(1, WorkspaceBook.Maximum).Where(book.Exists)];
}
