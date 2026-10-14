using FluentAssertions;
using Winland.Core;

namespace Winland.Tests;

[TestClass]
public sealed class ElevatedBookTests
{
    [TestMethod]
    public void A_later_place_replaces_the_rectangle_and_keeps_its_slot()
    {
        var book = new ElevatedBook();

        book.Place(1, 0, 0, 10, 10);
        book.Place(2, 1, 1, 10, 10);
        book.Place(1, 5, 6, 20, 30).Should().BeTrue();

        book.Drain().Should().Equal(
            ElevatedAct.Place(1, 5, 6, 20, 30),
            ElevatedAct.Place(2, 1, 1, 10, 10));
    }

    [TestMethod]
    public void A_close_drops_the_other_acts_for_that_window_and_sticks()
    {
        var book = new ElevatedBook();
        book.Place(1, 0, 0, 10, 10);
        book.Raise(1);
        book.Focus(1);
        book.Place(2, 4, 4, 12, 12);
        book.Raise(2);

        book.Close(1).Should().BeTrue();
        book.Place(1, 1, 1, 10, 10).Should().BeFalse();
        book.Raise(1).Should().BeFalse();
        book.Focus(1).Should().BeFalse();

        book.Drain().Should().Equal(
            ElevatedAct.Place(2, 4, 4, 12, 12),
            ElevatedAct.Close(1),
            ElevatedAct.Raise(2));
    }

    [TestMethod]
    public void A_mark_is_written_after_the_places_and_the_latest_one_for_a_window_stands()
    {
        var book = new ElevatedBook();

        book.Mark(1, 3).Should().BeTrue();
        book.Place(2, 0, 0, 10, 10);
        book.Mark(2, WorkspaceMark.Scratchpad);
        book.Mark(1, 5).Should().BeTrue();

        book.Drain().Should().Equal(
            ElevatedAct.Place(2, 0, 0, 10, 10),
            ElevatedAct.Mark(1, 5),
            ElevatedAct.Mark(2, WorkspaceMark.Scratchpad));
        book.Drain().Should().BeEmpty();
    }

    [TestMethod]
    public void A_mark_of_none_takes_the_mark_off()
    {
        var book = new ElevatedBook();

        book.Mark(1, WorkspaceMark.None).Should().BeTrue();

        book.Drain().Should().Equal(ElevatedAct.Mark(1, WorkspaceMark.None));
    }

    [TestMethod]
    public void A_number_that_is_no_mark_is_not_handed_to_the_helper()
    {
        // The helper runs as administrator. It writes what Winoma writes, and nothing else.
        var book = new ElevatedBook();

        book.Mark(1, 42).Should().BeFalse();
        book.Mark(0, 3).Should().BeFalse();

        book.Drain().Should().BeEmpty();
    }

    [TestMethod]
    public void A_close_drops_a_mark_for_that_window()
    {
        var book = new ElevatedBook();
        book.Mark(1, 3);

        book.Close(1);
        book.Mark(1, 4).Should().BeFalse();

        book.Drain().Should().Equal(ElevatedAct.Close(1));
    }

    [TestMethod]
    public void A_mark_survives_its_line_and_a_line_with_another_number_is_not_read()
    {
        ElevatedAct.Mark(7, 3).Line.Should().Be("M 7 3");
        ElevatedBook.TryRead("M 7 3", out var written).Should().BeTrue();
        written.Should().Be(ElevatedAct.Mark(7, 3));
        written.Value.Should().Be(3);

        ElevatedBook.TryRead("M 7 0", out var removed).Should().BeTrue();
        removed.Should().Be(ElevatedAct.Mark(7, WorkspaceMark.None));

        ElevatedBook.TryRead("M 7 42", out _).Should().BeFalse();
        ElevatedBook.TryRead("M 7", out _).Should().BeFalse();
        ElevatedBook.TryRead("M 7 3 1", out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_close_leaves_a_focus_of_another_window()
    {
        var book = new ElevatedBook();
        book.Focus(2);
        book.Close(1);
        book.Focus(1).Should().BeFalse();

        book.Drain().Should().Equal(ElevatedAct.Close(1), ElevatedAct.Focus(2));
    }

    [TestMethod]
    public void The_latest_stacking_act_is_applied_last()
    {
        var book = new ElevatedBook();
        book.Raise(1);
        book.Focus(1);
        book.Raise(1);
        book.Focus(2);

        book.Drain().Should().Equal(ElevatedAct.Raise(1), ElevatedAct.Focus(2));
    }

    [TestMethod]
    public void A_repeated_close_keeps_its_place_ahead_of_a_later_close()
    {
        var book = new ElevatedBook();
        book.Close(1);
        book.Close(2);
        book.Close(1).Should().BeTrue();

        book.Drain().Should().Equal(ElevatedAct.Close(1), ElevatedAct.Close(2));
    }

    [TestMethod]
    public void An_empty_window_or_an_empty_size_leaves_the_book_unchanged()
    {
        var book = new ElevatedBook();
        book.Place(1, -11, 56, 40, 50);

        book.Place(0, 0, 0, 10, 10).Should().BeFalse();
        book.Place(1, 0, 0, 0, 10).Should().BeFalse();
        book.Place(1, 0, 0, 10, 0).Should().BeFalse();
        book.Close(0).Should().BeFalse();
        book.Raise(-1).Should().BeFalse();
        book.Focus(0).Should().BeFalse();

        book.Drain().Should().Equal(ElevatedAct.Place(1, -11, 56, 40, 50));
    }

    [TestMethod]
    public void A_place_is_accepted_after_the_close_has_been_drained()
    {
        var book = new ElevatedBook();
        book.Close(1);
        book.Drain().Should().Equal(ElevatedAct.Close(1));

        book.Place(1, 0, 0, 8, 8).Should().BeTrue();

        book.Drain().Should().Equal(ElevatedAct.Place(1, 0, 0, 8, 8));
    }

    [TestMethod]
    public void A_drained_act_reads_back_as_the_same_act()
    {
        var book = new ElevatedBook();
        book.Place(198906, -11, 56, 1302, 1555);
        book.Raise(198906);
        book.Focus(7);
        book.Close(4);

        foreach (var act in book.Drain())
        {
            ElevatedBook.TryRead(act.Line, out var read).Should().BeTrue();
            read.Should().Be(act);
        }
    }

    [TestMethod]
    public void A_line_this_book_would_not_emit_is_refused()
    {
        ElevatedBook.TryRead("", out _).Should().BeFalse();
        ElevatedBook.TryRead("nope", out _).Should().BeFalse();
        ElevatedBook.TryRead("C 0", out _).Should().BeFalse();
        ElevatedBook.TryRead("P 1 0 0 0 10", out _).Should().BeFalse();
    }
}
