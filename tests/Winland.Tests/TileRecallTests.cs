using FluentAssertions;
using Winland.Core.Tiling;

namespace Winland.Tests;

[TestClass]
public sealed class TileRecallTests
{
    private static readonly TileRect Area = new(0, 0, 1000, 800);

    [TestMethod]
    public void Two_windows_keep_their_sides_whichever_was_used_last()
    {
        var session = new TilingSession();

        // Window 2 is on the right and was used last, so it is listed first.
        session.Recall([Seen(2, 500, 0, 500, 800), Seen(1, 0, 0, 500, 800)]).Should().BeTrue();

        var placed = session.Arrange(Area);
        placed.Single(item => item.Id == 1).Should().Be(Seen(1, 0, 0, 500, 800));
        placed.Single(item => item.Id == 2).Should().Be(Seen(2, 500, 0, 500, 800));
    }

    [TestMethod]
    public void A_resized_layout_of_five_windows_comes_back_pixel_for_pixel()
    {
        var before = new TilingSession();
        for (var id = 1; id <= 5; id++)
        {
            before.Add(id, Area);
        }

        before.Focus(1);
        before.Resize(137, 0, Area);
        before.Focus(3);
        before.Resize(0, 61, Area);
        var shown = before.Arrange(Area);
        shown.Single(item => item.Id == 1).Width.Should().NotBe(500);

        var after = new TilingSession();
        after.Recall([shown[3], shown[0], shown[4], shown[2], shown[1]]);

        after.Arrange(Area).Should().BeEquivalentTo(shown);
        // A second start reads the same tiles again, so nothing creeps a pixel at a time.
        var again = new TilingSession();
        again.Recall(after.Arrange(Area));
        again.Arrange(Area).Should().BeEquivalentTo(shown);
    }

    [TestMethod]
    public void Gutters_between_the_tiles_are_not_taken_for_a_resize()
    {
        var before = new TilingSession();
        before.SetSpacing(12);
        before.ToggleSpacing();
        before.Add(1, Area);
        before.Add(2, Area);
        before.Add(3, Area);
        var shown = before.Arrange(Area);

        var after = new TilingSession();
        after.SetSpacing(12);
        after.ToggleSpacing();
        after.Recall([shown[2], shown[1], shown[0]]);

        after.Arrange(Area).Should().BeEquivalentTo(shown);
    }

    [TestMethod]
    public void Windows_that_overlap_are_tiled_in_the_order_they_sit()
    {
        var session = new TilingSession();

        // No line separates them: 1 is fullscreen over the left tile's neighbor.
        session.Recall([Seen(2, 0, 0, 1000, 800), Seen(1, 0, 0, 500, 800), Seen(3, 300, 100, 900, 600)]);

        session.Arrange(Area).OrderBy(item => item.X).ThenBy(item => item.Y).Select(item => item.Id).Should().Equal(1, 2, 3);
    }

    [TestMethod]
    public void The_keyboard_stays_on_the_window_that_had_it()
    {
        var session = new TilingSession();

        session.Recall([Seen(2, 500, 0, 500, 800), Seen(1, 0, 0, 500, 800)], focused: 1);

        session.Focused.Should().Be(1);
    }

    [TestMethod]
    public void The_window_used_last_is_focused_when_the_keyboard_is_elsewhere()
    {
        var session = new TilingSession();

        session.Recall([Seen(2, 500, 0, 500, 800), Seen(1, 0, 0, 500, 800)], focused: 9);

        session.Focused.Should().Be(2);
    }

    [TestMethod]
    public void A_session_that_has_windows_keeps_its_own_layout()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);
        var shown = session.Arrange(Area);

        session.Recall([Seen(1, 500, 0, 500, 800), Seen(2, 0, 0, 500, 800)]).Should().BeFalse();

        session.Arrange(Area).Should().BeEquivalentTo(shown);
    }

    private static Placement Seen(long id, int x, int y, int width, int height) => new(id, x, y, width, height, true);
}
