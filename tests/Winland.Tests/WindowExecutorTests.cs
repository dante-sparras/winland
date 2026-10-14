using FluentAssertions;
using Winland.Core;

namespace Winland.Tests;

[TestClass]
public sealed class WindowExecutorTests
{
    [TestMethod]
    public void A_refused_place_is_the_literal_place()
    {
        WindowExecutor.Place(7, 10, 20, 300, 400, succeeded: false, error: 5)
            .Should().Be(ElevatedAct.Place(7, 10, 20, 300, 400));
    }

    [TestMethod]
    public void A_place_that_succeeded_is_no_act()
    {
        WindowExecutor.Place(7, 10, 20, 300, 400, succeeded: true, error: 5)
            .Should().BeNull();
    }

    [TestMethod]
    public void A_place_with_error_6_is_no_act()
    {
        WindowExecutor.Place(7, 10, 20, 300, 400, succeeded: false, error: 6)
            .Should().BeNull();
    }

    [TestMethod]
    public void A_refused_mark_is_that_mark_for_the_helper_to_write()
    {
        // An administrator window refuses a property from a desktop that is not one, with access denied.
        WindowExecutor.Mark(7, 3, succeeded: false, error: 5)
            .Should().Be(ElevatedAct.Mark(7, 3));
        WindowExecutor.Mark(7, WorkspaceMark.None, succeeded: false, error: 5)
            .Should().Be(ElevatedAct.Mark(7, WorkspaceMark.None));
    }

    [TestMethod]
    public void A_mark_that_was_written_or_failed_another_way_is_no_act()
    {
        WindowExecutor.Mark(7, 3, succeeded: true, error: 5).Should().BeNull();
        WindowExecutor.Mark(7, 3, succeeded: false, error: 1400).Should().BeNull("the window closed, and the helper could do no more");
    }

    [TestMethod]
    public void A_refused_raise_is_that_raise()
    {
        WindowExecutor.Raise(7, succeeded: false, error: 5)
            .Should().Be(ElevatedAct.Raise(7));
    }

    [TestMethod]
    public void A_raise_that_succeeded_is_no_act()
    {
        WindowExecutor.Raise(7, succeeded: true, error: 5)
            .Should().BeNull();
    }

    [TestMethod]
    public void A_raise_with_error_6_is_no_act()
    {
        WindowExecutor.Raise(7, succeeded: false, error: 6)
            .Should().BeNull();
    }

    [TestMethod]
    public void An_elevated_close_is_that_close_and_a_normal_close_is_no_act()
    {
        WindowExecutor.Close(7, elevated: true).Should().Be(ElevatedAct.Close(7));
        WindowExecutor.Close(7, elevated: false).Should().BeNull();
    }

    [TestMethod]
    public void An_administrator_window_in_front_starts_the_helper()
    {
        WindowExecutor.Front(elevated: true, helperRunning: false, refused: false).Should().BeTrue();
    }

    [TestMethod]
    public void A_normal_window_in_front_starts_no_helper()
    {
        WindowExecutor.Front(elevated: false, helperRunning: false, refused: false).Should().BeFalse();
    }

    [TestMethod]
    public void A_running_helper_is_not_started_again()
    {
        WindowExecutor.Front(elevated: true, helperRunning: true, refused: false).Should().BeFalse();
    }

    [TestMethod]
    public void A_refused_helper_is_not_asked_for_again()
    {
        WindowExecutor.Front(elevated: true, helperRunning: false, refused: true).Should().BeFalse();
    }

    [TestMethod]
    public void A_helper_that_exited_without_connecting_counts_as_refused()
    {
        WindowExecutor.LeftUnconnected(exited: true, connected: false).Should().BeTrue();
    }

    [TestMethod]
    public void A_helper_that_connected_and_exited_later_is_not_a_refusal()
    {
        WindowExecutor.LeftUnconnected(exited: true, connected: true).Should().BeFalse();
    }

    [TestMethod]
    public void A_helper_that_has_not_connected_yet_is_not_a_refusal()
    {
        WindowExecutor.LeftUnconnected(exited: false, connected: false).Should().BeFalse();
    }

    [TestMethod]
    public void Two_refused_places_of_one_window_drain_to_one_place_line()
    {
        var book = new ElevatedBook();
        Record(book, WindowExecutor.Place(7, 10, 20, 300, 400, succeeded: false, error: 5));
        Record(book, WindowExecutor.Place(7, 10, 20, 300, 400, succeeded: false, error: 5));

        var drained = book.Drain();
        drained.Should().ContainSingle();
        drained[0].Line.Should().Be("P 7 10 20 300 400");
    }

    private static void Record(ElevatedBook book, ElevatedAct? act)
    {
        if (act is not ElevatedAct place)
        {
            Assert.Fail("expected a place");
            return;
        }

        book.Place(place.Id, place.X, place.Y, place.Width, place.Height).Should().BeTrue();
    }
}
