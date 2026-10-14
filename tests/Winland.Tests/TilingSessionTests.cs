using FluentAssertions;
using Winland.Core.Tiling;

namespace Winland.Tests;

[TestClass]
public sealed class TilingSessionTests
{
    private static readonly TileRect Area = new(0, 0, 1000, 800);

    [TestMethod]
    public void Two_windows_split_the_area_without_overlap_or_gaps()
    {
        var session = new TilingSession();
        session.Add(1);
        session.Add(2);

        var placed = session.Arrange(Area).Where(item => item.Visible).ToList();

        placed.Should().HaveCount(2);
        var first = placed.Single(item => item.Id == 1);
        var second = placed.Single(item => item.Id == 2);
        (first.Width + second.Width).Should().Be(1000);
        first.X.Should().Be(0);
        second.X.Should().Be(first.Width);
        placed.Should().OnlyContain(item => item.Height == 800);
    }

    [TestMethod]
    public void A_preview_names_the_tile_a_window_would_take_and_changes_nothing()
    {
        var session = Three();
        var before = session.Arrange(Area);
        var version = session.Version;
        var focused = session.Focused;

        var tile = session.Preview(4, Area, Area);

        session.Version.Should().Be(version);
        session.Focused.Should().Be(focused);
        session.Windows.Should().NotContain(4);
        session.Arrange(Area).Should().BeSameAs(before);
        session.Add(4, Area);
        var placed = session.Arrange(Area).Single(item => item.Id == 4);
        tile.Should().Be(new TileRect(placed.X, placed.Y, placed.Width, placed.Height));
    }

    [TestMethod]
    public void A_preview_of_the_first_window_is_the_whole_area_and_a_window_the_session_holds_has_none()
    {
        var session = new TilingSession();

        session.Preview(1, Area, Area).Should().Be(Area);
        session.Windows.Should().BeEmpty();
        session.Add(1, Area);
        session.Preview(1, Area, Area).Should().BeNull();
    }

    [TestMethod]
    public void The_same_tiles_are_handed_out_until_something_changes()
    {
        var session = Three();
        var first = session.Arrange(Area);

        session.Arrange(Area).Should().BeSameAs(first);
        session.Arrange(new TileRect(0, 0, 900, 800)).Should().NotBeSameAs(first);
    }

    [TestMethod]
    public void A_fullscreen_window_is_placed_again_when_its_monitor_changes()
    {
        var session = Three();
        session.ToggleFullscreen();
        var first = session.Arrange(Area, new TileRect(0, 0, 1000, 848));

        var second = session.Arrange(Area, new TileRect(0, 0, 1000, 900));

        second.Single(item => item.Raised).Height.Should().Be(900);
        first.Single(item => item.Raised).Height.Should().Be(848);
    }

    [TestMethod]
    public void Every_change_to_a_layout_computes_the_tiles_again()
    {
        var changes = new (string Name, Action<TilingSession> Change)[]
        {
            ("add", session => session.Add(4, Area)),
            ("add a known window", session => session.Add(1, Area)),
            ("remove", session => session.Remove(3, Area)),
            ("focus", session => session.Focus(1)),
            ("focus direction", session => session.FocusDirection(Direction.Left, Area)),
            ("swap", session => session.Swap(Direction.Left, Area)),
            ("float", session => session.ToggleFloat()),
            ("fullscreen", session => session.ToggleFullscreen()),
            ("full width", session => session.ToggleFullWidth()),
            ("layout", session => session.ToggleLayout()),
            ("spacing", session => session.ToggleSpacing()),
            ("spacing size", session => session.SetSpacing(4)),
            ("group bar", session => session.SetGroupBar(20)),
            ("arm a group", session => session.ToggleGroup()),
            ("resize", session => session.Resize(-40, 0, Area)),
            ("resize down", session => session.Resize(0, -40, Area)),
            ("pseudotile", session => session.TogglePseudo(Area)),
            ("drop", session => session.Drop(3, Area, 10, 10)),
            ("split", session => session.ToggleSplit(Area)),
            ("a reported minimum", session => session.ReportMinimum(1, 700, 100)),
            ("a kept size", session => session.NoteMinimum(1, 700, 100)),
        };
        foreach (var (name, change) in changes)
        {
            var session = Three();
            Changes(session, change, name);
        }

        var grouped = new (string Name, Action<TilingSession> Change)[]
        {
            ("cycle the group", session => session.CycleGroup(1, Area)),
            ("show a group member", session => session.ShowGroupMember(1, Area)),
            ("leave the group", session => session.LeaveGroup()),
            ("disassemble the group", session => session.ToggleGroup()),
            ("join the group", session =>
            {
                session.LeaveGroup();
                var apart = session.Arrange(Area);
                session.JoinNeighbor(Direction.Left, Area);
                session.Arrange(Area).Should().NotBeSameAs(apart, "join the group");
            }),
        };
        foreach (var (name, change) in grouped)
        {
            var session = new TilingSession();
            session.Add(1, Area);
            session.ToggleGroup();
            session.Add(2, Area);
            session.Add(3, Area);
            Changes(session, change, name);
        }
    }

    [TestMethod]
    public void A_pseudotile_that_is_resized_is_placed_again()
    {
        var session = Three();
        session.TogglePseudo(Area);
        var before = session.Arrange(Area);

        session.Resize(-40, 0, Area);

        session.Arrange(Area).Should().NotBeSameAs(before);
    }

    [TestMethod]
    public void A_place_asks_a_minimum_until_every_window_was_asked_in_that_area()
    {
        var session = Three();
        session.AsksMinimum(Area).Should().BeTrue();
        foreach (var id in session.Windows)
        {
            session.NoteAsked(id, Area);
        }

        session.AsksMinimum(Area).Should().BeFalse();
        session.AsksMinimum(new TileRect(0, 0, 900, 800)).Should().BeTrue("another area asks again");

        session.AskAgain(1);

        session.AsksMinimum(Area).Should().BeTrue();
    }

    [TestMethod]
    public void A_workspace_without_a_group_has_no_strips_to_draw()
    {
        var session = Three();
        session.SetGroupBar(24);
        session.HasStrips.Should().BeFalse();
        session.Strips(Area, Area).Should().BeEmpty();

        session.ToggleGroup();

        session.HasStrips.Should().BeTrue();
        session.Strips(Area, Area).Should().HaveCount(1);
    }

    private static TilingSession Three()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);
        session.Add(3, Area);
        return session;
    }

    private static void Changes(TilingSession session, Action<TilingSession> change, string name)
    {
        var before = session.Arrange(Area);
        var version = session.Version;

        change(session);

        session.Version.Should().NotBe(version, name);
        session.Arrange(Area).Should().NotBeSameAs(before, name);
    }

    [TestMethod]
    public void Spacing_is_off_until_toggled()
    {
        var session = new TilingSession();
        session.Add(1);
        session.Add(2);
        session.SetSpacing(20);

        session.Arrange(Area).Sum(item => item.Visible ? item.Width : 0).Should().Be(1000);

        session.ToggleSpacing();
        var gapped = session.Arrange(Area).Where(item => item.Visible).OrderBy(item => item.X).ToList();
        (gapped[0].Width + gapped[1].Width).Should().Be(980);
        gapped[1].X.Should().Be(gapped[0].Width + 20);
    }

    [TestMethod]
    public void Fullscreen_covers_the_screen_and_keeps_the_other_window()
    {
        var session = new TilingSession();
        session.Add(1);
        session.Add(2);
        session.Focus(2);
        session.ToggleFullscreen();

        var placed = session.Arrange(Area);
        var cover = placed.Single(item => item.Id == 2);
        var other = placed.Single(item => item.Id == 1);
        cover.Visible.Should().BeTrue();
        cover.Raised.Should().BeTrue();
        cover.Width.Should().Be(1000);
        cover.Height.Should().Be(800);
        other.Visible.Should().BeTrue();
        other.Raised.Should().BeFalse();
        other.Width.Should().BeLessThan(1000);
    }

    [TestMethod]
    public void Fullscreen_covers_the_monitor_while_the_other_window_keeps_its_tile()
    {
        var session = new TilingSession();
        session.Add(1);
        session.Add(2);
        session.Focus(2);
        session.ToggleFullscreen();
        var work = new TileRect(0, 48, 1000, 752);
        var screen = new TileRect(0, 0, 1000, 800);

        var placed = session.Arrange(work, screen);
        var cover = placed.Single(item => item.Id == 2);
        var other = placed.Single(item => item.Id == 1);

        cover.Raised.Should().BeTrue();
        cover.X.Should().Be(0);
        cover.Y.Should().Be(0);
        cover.Width.Should().Be(1000);
        cover.Height.Should().Be(800);
        other.Raised.Should().BeFalse();
        other.Y.Should().BeGreaterThanOrEqualTo(48);
        other.Height.Should().BeLessThan(800);
    }

    [TestMethod]
    public void A_window_that_is_not_focused_can_be_made_fullscreen_and_tiled_again()
    {
        var session = new TilingSession();
        session.Add(1);
        session.Add(2);
        session.Focus(2);

        session.SetFullscreen(1, covers: true);

        session.Fullscreen.Should().Be(1);
        session.Arrange(Area).Single(item => item.Raised).Id.Should().Be(1);

        session.SetFullscreen(2, covers: false);

        session.Fullscreen.Should().Be(1);

        session.SetFullscreen(1, covers: false);

        session.Fullscreen.Should().BeNull();
        session.Arrange(Area).Should().NotContain(item => item.Raised);
    }

    [TestMethod]
    public void A_third_window_splits_the_tall_tile_downward()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);
        session.Add(3, Area);

        var placed = session.Arrange(Area).Where(item => item.Visible).ToDictionary(item => item.Id);
        placed[1].Height.Should().Be(800);
        placed[2].Height.Should().Be(400);
        placed[3].Height.Should().Be(400);
        placed[3].Y.Should().Be(placed[2].Height);
        placed[2].Width.Should().Be(placed[3].Width);
        placed[1].Width.Should().Be(placed[2].Width);
    }

    [TestMethod]
    public void Closing_one_window_leaves_the_untouched_tile_where_it_was()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);
        session.Add(3, Area);
        var before = session.Arrange(Area).Single(item => item.Id == 1);

        session.Remove(3, Area);
        var after = session.Arrange(Area).ToDictionary(item => item.Id);

        after[1].Should().Be(before);
        after[2].Height.Should().Be(800);
        after.ContainsKey(3).Should().BeFalse();
    }

    [TestMethod]
    public void Four_windows_still_cover_the_area_after_two_close()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);
        session.Add(3, Area);
        session.Add(4, Area);
        session.Remove(4, Area);
        session.Remove(2, Area);

        var placed = session.Arrange(Area).Where(item => item.Visible).ToList();
        placed.Sum(item => (long)item.Width * item.Height).Should().Be(Area.Width * (long)Area.Height);
        placed.Min(item => item.X).Should().Be(0);
        placed.Min(item => item.Y).Should().Be(0);
        placed.Max(item => item.X + item.Width).Should().Be(Area.Width);
        placed.Max(item => item.Y + item.Height).Should().Be(Area.Height);
    }

    [TestMethod]
    public void A_window_that_cannot_shrink_takes_space_from_its_neighbor()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);
        session.NoteMinimum(2, 700, 200);

        var placed = session.Arrange(Area).Where(item => item.Visible).ToDictionary(item => item.Id);

        placed[2].Width.Should().Be(700);
        placed[1].Width.Should().Be(300);
        (placed[1].X + placed[1].Width).Should().Be(placed[2].X);
        (placed[1].Width + placed[2].Width).Should().Be(Area.Width);
    }

    [TestMethod]
    public void Windows_whose_minimums_cannot_all_fit_share_the_last_tile_instead_of_shrinking()
    {
        var session = new TilingSession();
        for (var id = 1; id <= 8; id++)
        {
            session.Add(id, Area);
            session.NoteMinimum(id, 400, 300);
        }

        var all = session.Arrange(Area);
        var placed = all.Where(item => item.Visible).ToList();

        all.Should().HaveCount(8);
        placed.Should().HaveCount(4);
        AssertHoldsMinimums(placed, 400, 300);
        AssertFlush(placed, Area);
    }

    /// <summary>Eight windows that each keep 400 by 300, where four fit. A strip is 20 pixels.</summary>
    private static TilingSession Crowded()
    {
        var session = new TilingSession();
        session.SetGroupBar(20);
        for (var id = 1; id <= 8; id++)
        {
            session.Add(id, Area);
            session.NoteMinimum(id, 400, 300);
        }

        return session;
    }

    [TestMethod]
    public void The_last_tile_has_a_strip_with_a_tab_for_each_window_that_shares_it()
    {
        var session = Crowded();

        var strip = session.Strips(Area, Area).Should().ContainSingle().Subject;
        var shown = session.Arrange(Area).Single(item => item.Id == 8);

        strip.Members.Should().Equal(4, 5, 6, 7, 8);
        strip.Active.Should().Be(4);
        shown.Visible.Should().BeTrue();
        shown.Height.Should().BeGreaterThanOrEqualTo(300, "the strip is not taken out of the size the window keeps");
        strip.Rect.Should().Be(new TileRect(shown.X, shown.Y - 20, shown.Width, 20));
        session.Arrange(Area).Where(item => item.Id is >= 4 and < 8).Should().OnlyContain(
            item => !item.Visible && item.Y == shown.Y && item.Height == shown.Height,
            "the windows behind are hidden at the size of the one shown");
    }

    [TestMethod]
    public void The_group_cycle_moves_among_the_windows_that_share_the_last_tile()
    {
        var session = Crowded();

        session.CycleGroup(1, Area);

        session.Focused.Should().Be(4, "the cycle wraps from the last window to the first");
        session.Arrange(Area).Where(item => item.Visible).Select(item => item.Id).Should().BeEquivalentTo([1, 2, 3, 4]);
        session.Strips(Area, Area).Single().Active.Should().Be(0);

        session.CycleGroup(-2, Area);

        session.Focused.Should().Be(7);
        session.Arrange(Area).Where(item => item.Visible).Select(item => item.Id).Should().BeEquivalentTo([1, 2, 3, 7]);
    }

    [TestMethod]
    public void A_number_shows_that_tab_of_the_last_tile()
    {
        var session = Crowded();

        session.ShowGroupMember(2, Area);

        session.Focused.Should().Be(5);
        session.Strips(Area, Area).Single().Active.Should().Be(1);

        session.ShowGroupMember(6, Area);

        session.Focused.Should().Be(5, "the tile has five tabs");
    }

    [TestMethod]
    public void The_last_tile_keeps_showing_its_window_while_the_keyboard_is_on_another_tile()
    {
        var session = Crowded();
        session.CycleGroup(-1, Area);
        session.Focused.Should().Be(7);

        session.Focus(1);

        session.Arrange(Area).Where(item => item.Visible).Select(item => item.Id).Should().BeEquivalentTo([1, 2, 3, 7]);
        session.Strips(Area, Area).Single().Active.Should().Be(3);
    }

    [TestMethod]
    public void The_last_tile_is_tall_enough_for_its_strip_and_the_size_its_windows_keep()
    {
        var session = new TilingSession();
        session.SetGroupBar(20);
        for (var id = 1; id <= 8; id++)
        {
            session.Add(id, Area);
            session.NoteMinimum(id, 400, 390);
        }

        var placed = session.Arrange(Area).Where(item => item.Visible).ToList();

        placed.Should().HaveCount(4);
        AssertHoldsMinimums(placed, 400, 390);
        placed.Single(item => item.Id == 8).Height.Should().Be(390);
    }

    [TestMethod]
    public void Cycling_the_last_tile_carries_fullscreen_to_the_window_shown()
    {
        var session = Crowded();
        session.ToggleFullscreen();

        session.CycleGroup(1, Area);

        session.Fullscreen.Should().Be(4);
        session.Arrange(Area).Single(item => item.Raised).Id.Should().Be(4);
    }

    [TestMethod]
    public void A_window_takes_a_tile_of_its_own_as_soon_as_there_is_room()
    {
        var session = Crowded();

        session.Remove(1, Area);
        session.Remove(2, Area);

        session.Arrange(Area).Where(item => item.Visible).Select(item => item.Id).Should().BeEquivalentTo([3, 4, 5, 8]);
        session.Strips(Area, Area).Should().ContainSingle().Which.Members.Should().Equal(6, 7, 8);

        session.Remove(6, Area);
        session.Remove(7, Area);

        session.Arrange(Area).Should().OnlyContain(item => item.Visible);
        session.Strips(Area, Area).Should().BeEmpty();
        session.HasStrips.Should().BeFalse();
    }

    [TestMethod]
    public void A_group_in_the_last_tile_is_still_a_group_when_there_is_room_again()
    {
        var session = Crowded();
        session.ToggleGroup();
        session.Add(9, Area);
        session.NoteMinimum(9, 400, 300);

        session.Strips(Area, Area).Should().ContainSingle("the group is under the tile's strip")
            .Which.Members.Should().Equal(4, 5, 6, 7, 8, 9);

        foreach (var id in new long[] { 1, 2, 3, 4 })
        {
            session.Remove(id, Area);
        }

        session.Arrange(Area).Where(item => item.Visible).Select(item => item.Id).Should().BeEquivalentTo([5, 6, 7, 9]);
        session.Grouped(8).Should().BeTrue();
        session.Strips(Area, Area).Should().ContainSingle().Which.Members.Should().Equal(8, 9);
    }

    [TestMethod]
    public void Five_windows_cover_the_area_without_overlapping()
    {
        var session = new TilingSession();
        for (var id = 1; id <= 5; id++)
        {
            session.Add(id, Area);
        }

        var placed = session.Arrange(Area).Where(item => item.Visible).OrderBy(item => item.Id).ToList();
        placed.Should().HaveCount(5);
        placed.Sum(item => (long)item.Width * item.Height).Should().Be(Area.Width * (long)Area.Height);
        AssertNoOverlap(placed);
    }

    [TestMethod]
    public void Six_terminals_each_get_their_minimum_and_tile_flush()
    {
        var session = Terminals(6);

        var placed = session.Arrange(Screen).Where(item => item.Visible).ToList();

        placed.Should().HaveCount(6);
        AssertHoldsMinimums(placed, TerminalWidth, TerminalHeight);
        AssertFlush(placed, Screen);
    }

    [TestMethod]
    public void Eight_terminals_get_a_grid_instead_of_tiles_squeezed_to_their_minimum()
    {
        var session = Terminals(8);

        var placed = session.Arrange(Screen).Where(item => item.Visible).ToList();

        placed.Min(item => item.Height).Should().BeGreaterThan(TerminalHeight * 3);
    }

    [TestMethod]
    public void Two_windows_too_wide_to_sit_side_by_side_stack()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);
        session.ReportMinimum(1, 600, 100);
        session.ReportMinimum(2, 600, 100);

        var placed = session.Arrange(Area).Where(item => item.Visible).OrderBy(item => item.Y).ToList();

        placed.Should().OnlyContain(item => item.X == 0 && item.Width == Area.Width && item.Height == 400);
        placed[1].Y.Should().Be(400);
    }

    [TestMethod]
    public void Toggling_the_split_turns_two_columns_into_two_rows_and_back()
    {
        var area = new TileRect(0, 0, 2560, 1440);
        var session = new TilingSession();
        session.Add(1, area);
        session.Add(2, area);
        session.ReportMinimum(1, 899, 599);
        session.ReportMinimum(2, 749, 779);

        session.ToggleSplit(area);
        var rows = session.Arrange(area).Where(item => item.Visible).ToDictionary(item => item.Id);
        rows[1].X.Should().Be(rows[2].X);
        rows[2].Y.Should().BeGreaterThan(rows[1].Y);

        session.ToggleSplit(area);
        var columns = session.Arrange(area).Where(item => item.Visible).ToDictionary(item => item.Id);
        columns[1].Y.Should().Be(columns[2].Y);
        columns[2].X.Should().BeGreaterThan(columns[1].X);
    }

    [TestMethod]
    public void Toggling_the_split_turns_two_rows_into_two_columns_when_a_kept_width_filled_the_workspace()
    {
        var area = new TileRect(0, 0, 2560, 1440);
        var session = new TilingSession();
        session.Add(1, area);
        session.Add(2, area);
        session.ReportMinimum(1, 899, 599);
        session.ReportMinimum(2, 749, 779);
        session.NoteMinimum(1, area.Width, 0);

        var before = session.Arrange(area).Where(item => item.Visible).ToDictionary(item => item.Id);
        before[1].Width.Should().Be(area.Width);
        before[2].Y.Should().BeGreaterThan(before[1].Y);

        session.ToggleSplit(area);
        var after = session.Arrange(area).Where(item => item.Visible).ToDictionary(item => item.Id);
        after[1].Y.Should().Be(after[2].Y);
        after[2].X.Should().BeGreaterThan(after[1].X);
        after[1].Width.Should().BeLessThan(area.Width);
    }

    [TestMethod]
    public void Any_number_of_terminals_never_overlap_or_leave_the_screen()
    {
        for (var count = 1; count <= 45; count++)
        {
            var session = Terminals(count);

            var all = session.Arrange(Screen);
            var placed = all.Where(item => item.Visible).ToList();

            all.Should().HaveCount(count);
            placed.Should().Contain(item => item.Id == session.Focused, $"{count} windows");
            AssertHoldsMinimums(placed, TerminalWidth, TerminalHeight);
            AssertFlush(placed, Screen);
        }
    }

    [TestMethod]
    public void Windows_past_what_the_screen_holds_share_the_last_tile_and_the_focused_one_shows()
    {
        var session = Terminals(45);
        session.Focus(44);

        var all = session.Arrange(Screen);
        var shown = all.Single(item => item.Id == 44);
        var hidden = all.Where(item => !item.Visible).ToList();

        shown.Visible.Should().BeTrue();
        hidden.Should().NotBeEmpty();
        hidden.Should().OnlyContain(item => item.X == shown.X && item.Y == shown.Y && item.Width == shown.Width && item.Height == shown.Height);
    }

    [TestMethod]
    public void Minimums_that_already_fit_leave_the_layout_alone()
    {
        var plain = new TilingSession();
        var limited = new TilingSession();
        for (var id = 1; id <= 3; id++)
        {
            plain.Add(id, Area);
            limited.Add(id, Area);
            limited.ReportMinimum(id, 300, 200);
        }

        limited.Arrange(Area).Should().Equal(plain.Arrange(Area));
    }

    [TestMethod]
    public void A_width_that_overflowed_does_not_raise_the_minimum_height()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);
        session.Add(3, Area);

        session.NoteMinimum(3, 600, 0).Should().BeTrue();
        var third = session.Arrange(Area).Single(item => item.Id == 3);

        third.Width.Should().Be(600);
        third.Height.Should().Be(400);
    }

    [TestMethod]
    public void A_new_report_replaces_the_last_one_but_a_kept_size_stays()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);

        session.ReportMinimum(2, 700, 100);
        session.ReportMinimum(2, 600, 100).Should().BeTrue();
        session.Arrange(Area).Single(item => item.Id == 2).Width.Should().Be(600);

        session.NoteMinimum(2, 650, 0);
        session.ReportMinimum(2, 300, 100);
        session.Arrange(Area).Single(item => item.Id == 2).Width.Should().Be(650);
    }

    [TestMethod]
    public void A_floated_window_gives_its_tile_back_to_its_neighbor()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);
        session.ToggleFloat();

        var placed = session.Arrange(Area);

        placed.Should().ContainSingle();
        placed[0].Id.Should().Be(1);
        placed[0].Width.Should().Be(Area.Width);
        placed[0].Height.Should().Be(Area.Height);
        placed.Should().NotContain(item => item.Id == 2);
    }

    [TestMethod]
    public void A_floated_window_that_is_fullscreen_covers_the_workspace()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);
        session.ToggleFloat();
        session.ToggleFullscreen();

        var cover = session.Arrange(Area).Single(item => item.Id == 2);

        cover.Visible.Should().BeTrue();
        cover.Raised.Should().BeTrue();
        cover.X.Should().Be(Area.X);
        cover.Y.Should().Be(Area.Y);
        cover.Width.Should().Be(Area.Width);
        cover.Height.Should().Be(Area.Height);
    }

    [TestMethod]
    public void Full_width_spans_the_workspace_and_not_the_virtual_screen()
    {
        var area = new TileRect(1920, 56, 1000, 800);
        var session = new TilingSession();
        session.Add(1, area);
        session.Add(2, area);
        session.Focus(1);
        session.ToggleFullWidth();

        var wide = session.Arrange(area).Single(item => item.Id == 1 && item.Visible);
        wide.X.Should().Be(area.X);
        wide.Width.Should().Be(area.Width);
        wide.Y.Should().Be(area.Y);
    }

    [TestMethod]
    public void Full_width_fills_the_workspace_at_the_full_tile_height_from_a_split_drawn_one_above_the_other()
    {
        var area = new TileRect(1920, 56, 1000, 800);
        var session = new TilingSession();
        session.Add(1, area);
        session.Add(2, area);
        session.ToggleSplit(area);
        session.Arrange(area).Single(item => item.Id == 2).Should().Be(new Placement(2, 1920, 456, 1000, 400, true));

        session.ToggleFullWidth();

        var placed = session.Arrange(area).ToDictionary(item => item.Id);
        placed[2].Should().Be(new Placement(2, 1920, 56, 1000, 800, true, Above: true), "the bar is above the work area, and stays up");
        placed[1].Should().Be(new Placement(1, 1920, 56, 1000, 400, true), "the other tile keeps its place under it");
    }

    [TestMethod]
    public void A_full_width_window_is_kept_above_the_tiles_it_covers_and_stays_a_tile()
    {
        var session = Three();
        session.Focus(1);
        session.Arrange(Area).Should().OnlyContain(item => !item.Above);

        session.ToggleFullWidth();

        var placed = session.Arrange(Area);
        placed.Where(item => item.Above).Select(item => item.Id).Should().Equal(1);
        placed.Should().OnlyContain(item => !item.Raised, "it does not cover the Bar, and it is still a tile");

        session.ToggleFullWidth();

        session.Arrange(Area).Should().OnlyContain(item => !item.Above);
    }

    [TestMethod]
    public void A_full_width_group_keeps_only_the_window_it_shows_above_the_tiles()
    {
        var session = Three();
        session.Focus(1);
        session.ToggleGroup();
        session.Add(4, Area);
        session.ToggleFullWidth();

        var placed = session.Arrange(Area);

        placed.Where(item => item.Above).Select(item => item.Id).Should().Equal(4);
        placed.Single(item => item.Id == 1).Visible.Should().BeFalse();
    }

    [TestMethod]
    public void The_pointer_finds_the_full_width_window_and_not_a_tile_under_it()
    {
        var session = Three();
        session.Focus(1);
        session.ToggleFullWidth();

        var placed = session.Arrange(Area);

        placed.Single(item => item.Id == 3).Should().Be(new Placement(3, 500, 400, 500, 400, true));
        TileHit.At(placed, 750, 600).Should().Be(1);
        TileHit.At(placed, 750, 100).Should().Be(1);
    }

    [TestMethod]
    public void Closing_an_earlier_group_member_keeps_the_focused_window_shown()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.ToggleGroup();
        session.Add(2, Area);
        session.Add(3, Area);
        session.Add(4, Area);
        session.ShowGroupMember(3, Area);

        session.Remove(1, Area);

        session.Focused.Should().Be(3);
        session.Arrange(Area).Single(item => item.Visible).Id.Should().Be(3);
    }

    [TestMethod]
    public void Closing_the_shown_member_of_a_group_keeps_focus_in_the_group()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.ToggleGroup();
        session.Add(2, Area);
        session.Add(3, Area);

        session.Remove(session.Focused!.Value, Area);

        session.Focused.Should().NotBeNull();
        session.Arrange(Area).Single(item => item.Visible).Id.Should().Be(session.Focused!.Value);
    }

    [TestMethod]
    public void A_window_that_joins_a_fullscreen_group_is_the_one_that_covers_the_screen()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.ToggleFullscreen();
        session.ToggleGroup();

        session.Add(2, Area);

        session.Arrange(Area).Single(item => item.Visible).Id.Should().Be(2);
        session.Fullscreen.Should().Be(2);
    }

    [TestMethod]
    public void Closing_the_shown_member_of_a_fullscreen_group_leaves_the_next_member_fullscreen()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.ToggleGroup();
        session.Add(2, Area);
        session.ToggleFullscreen();

        session.Remove(2, Area);

        session.Fullscreen.Should().Be(1);
    }

    [TestMethod]
    public void Cycling_a_fullscreen_group_carries_fullscreen_to_the_member_shown()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.ToggleGroup();
        session.Add(2, Area);
        session.ToggleFullscreen();

        session.CycleGroup(1, Area);

        session.Fullscreen.Should().Be(session.Focused);
        session.Arrange(Area).Single(item => item.Visible).Id.Should().Be(session.Focused!.Value);
    }

    private static readonly TileRect Screen = new(0, 56, 2560, 1544);
    private const int TerminalWidth = 714;
    private const int TerminalHeight = 127;

    private static TilingSession Terminals(int count)
    {
        var session = new TilingSession();
        for (var id = 1; id <= count; id++)
        {
            session.Add(id, Screen);
            session.ReportMinimum(id, TerminalWidth, TerminalHeight);
        }

        return session;
    }

    private static void AssertHoldsMinimums(List<Placement> placed, int width, int height)
    {
        placed.Should().OnlyContain(
            item => item.Width >= width && item.Height >= height,
            $"every tile of {placed.Count} holds {width}x{height}");
    }

    private static void AssertFlush(List<Placement> placed, TileRect area)
    {
        placed.Should().OnlyContain(item =>
            item.X >= area.X
            && item.Y >= area.Y
            && item.X + item.Width <= area.X + area.Width
            && item.Y + item.Height <= area.Y + area.Height);
        AssertNoOverlap(placed);
        placed.Sum(item => (long)item.Width * item.Height).Should().Be((long)area.Width * area.Height, $"{placed.Count} tiles cover the area");
    }

    private static void AssertNoOverlap(List<Placement> placed)
    {
        for (var i = 0; i < placed.Count; i++)
        {
            for (var j = i + 1; j < placed.Count; j++)
            {
                var a = placed[i];
                var b = placed[j];
                var separated = a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
                separated.Should().BeTrue($"{a.Id} ({a.X},{a.Y},{a.Width}x{a.Height}) overlaps {b.Id} ({b.X},{b.Y},{b.Width}x{b.Height})");
            }
        }
    }

    [TestMethod]
    public void Focus_moves_to_the_window_on_the_right()
    {
        var session = new TilingSession();
        session.Add(1);
        session.Add(2);

        session.Focus(1);
        session.FocusDirection(Direction.Right, Area);

        session.Focused.Should().Be(2);
    }

    [TestMethod]
    public void Closing_the_focused_window_focuses_the_nearest_tile()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);
        session.Add(3, Area);

        session.Remove(3, Area);

        session.Focused.Should().Be(2);
    }

    [TestMethod]
    public void One_scrolling_window_fills_the_work_area_and_a_second_takes_half()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.ToggleLayout();

        var alone = session.Arrange(Area).Single();
        alone.Width.Should().Be(Area.Width);
        alone.Height.Should().Be(Area.Height);

        session.Add(2, Area);
        var focused = session.Arrange(Area).Single(item => item.Id == 2 && item.Visible);
        focused.Width.Should().Be(Area.Width * 49 / 100);
        focused.X.Should().Be(Area.X);
    }

    [TestMethod]
    public void Resize_expands_the_left_edge_and_the_inner_neighbor_keeps_its_size()
    {
        var wide = new TileRect(0, 0, 2000, 800);
        var session = new TilingSession();
        session.Add(1, wide);
        session.Add(2, wide);
        session.Add(3, wide);
        session.Focus(2);

        session.Resize(-100, 0, wide);

        var placed = session.Arrange(wide).Where(item => item.Visible).ToDictionary(item => item.Id);
        placed[1].Width.Should().BeInRange(890, 910);
        placed[2].Width.Should().BeInRange(590, 610);
        placed[3].Width.Should().BeInRange(490, 510);
    }

    [TestMethod]
    public void A_pseudotile_is_centered_in_its_tile_and_resize_changes_its_stored_size()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);
        session.TogglePseudo(Area);
        session.Resize(100, 0, Area);

        var placed = session.Arrange(Area).Where(item => item.Visible).ToDictionary(item => item.Id);
        placed[1].Width.Should().Be(500);
        placed[2].Width.Should().BeLessThan(500);
        placed[2].X.Should().BeGreaterThan(500);
        (placed[2].X + placed[2].Width).Should().BeLessThan(1000);
    }

    [TestMethod]
    public void An_empty_workspace_does_not_keep_grouping_armed()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.ToggleGroup();
        session.Add(2, Area);
        session.Arrange(Area).Count(item => item.Visible).Should().Be(1);

        session.Remove(2, Area);
        session.Remove(1, Area);

        session.GroupingArmed.Should().BeFalse();
        session.Add(3, Area);
        session.Add(4, Area);
        session.Arrange(Area).Count(item => item.Visible).Should().Be(2);
    }

    [TestMethod]
    public void A_window_joins_the_group_in_that_direction_and_the_group_cycles()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.ToggleGroup();
        session.Add(2, Area);
        session.Add(3, Area);
        session.LeaveGroup();

        session.JoinNeighbor(Direction.Left, Area);
        session.Windows.Should().HaveCount(3);
        session.Arrange(Area).Count(item => item.Visible).Should().Be(1);

        var shown = session.Focused;
        session.CycleGroup(1, Area);
        session.Focused.Should().NotBe(shown);
        session.ShowGroupMember(2, Area);
        session.Focused.Should().Be(2);
    }

    [TestMethod]
    public void While_grouping_is_armed_a_window_joins_a_lone_neighbor_and_the_two_are_a_group()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);

        session.JoinNeighbor(Direction.Left, Area);
        session.Arrange(Area).Count(item => item.Visible).Should().Be(2);

        session.Focus(1);
        session.ToggleGroup();
        session.Focus(2);
        session.JoinNeighbor(Direction.Left, Area);

        session.Grouped(1).Should().BeTrue();
        session.Focused.Should().Be(2);
        session.Arrange(Area).Where(item => item.Visible).Select(item => item.Id).Should().Equal(2);
    }

    [TestMethod]
    public void A_group_keeps_a_strip_free_along_the_top_of_its_tile_and_the_strip_lists_its_members()
    {
        var session = new TilingSession();
        session.SetGroupBar(20);
        session.Add(1, Area);
        session.Add(2, Area);
        session.ToggleGroup();
        session.Add(3, Area);

        var placed = session.Arrange(Area).ToDictionary(item => item.Id);
        var tile = placed[3];
        placed[2].Should().Be(tile with { Id = 2, Visible = false }, "the member behind is kept on the same rectangle");
        placed[1].Y.Should().Be(0, "a window that is not in a group keeps its whole tile");
        placed[1].Height.Should().Be(Area.Height);
        tile.Y.Should().Be(20);
        tile.Height.Should().Be(Area.Height - 20);

        var strip = session.Strips(Area, Area).Should().ContainSingle().Subject;
        strip.Rect.Should().Be(new TileRect(tile.X, 0, tile.Width, 20));
        strip.Members.Should().Equal(2, 3);
        strip.Active.Should().Be(1);

        session.CycleGroup(1, Area);
        session.Strips(Area, Area).Single().Active.Should().Be(0);
    }

    [TestMethod]
    public void An_armed_window_shows_its_strip_before_anything_has_joined_and_loses_it_when_disarmed()
    {
        var session = new TilingSession();
        session.SetGroupBar(20);
        session.Add(1, Area);
        session.Strips(Area, Area).Should().BeEmpty();

        session.ToggleGroup();

        session.Strips(Area, Area).Should().ContainSingle().Which.Members.Should().Equal(1);
        session.Arrange(Area).Single().Y.Should().Be(20);

        session.ToggleGroup();

        session.Strips(Area, Area).Should().BeEmpty();
        session.Arrange(Area).Single().Y.Should().Be(0);
    }

    [TestMethod]
    public void A_fullscreen_group_member_covers_its_strip_and_no_height_draws_none()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.ToggleGroup();
        session.Add(2, Area);
        session.Strips(Area, Area).Should().BeEmpty("no strip height was set");

        session.SetGroupBar(20);
        session.ToggleFullscreen();

        session.Strips(Area, Area).Should().BeEmpty();
        session.Arrange(Area).Single(item => item.Visible).Should().Be(new Placement(2, 0, 0, Area.Width, Area.Height, true, true));
    }

    [TestMethod]
    public void Dropping_on_the_left_half_of_a_tile_puts_the_window_there()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);
        var left = session.Arrange(Area).Single(item => item.Id == 1);

        session.Drop(2, Area, left.X + 10, left.Y + 10);

        var placed = session.Arrange(Area).Where(item => item.Visible).ToDictionary(item => item.Id);
        placed[2].X.Should().Be(0);
        placed[2].Width.Should().Be(Area.Width / 2);
        placed[1].X.Should().Be(placed[2].Width);
    }

    [TestMethod]
    public void A_workspace_with_a_group_hands_out_the_same_tiles_until_something_changes()
    {
        var session = Three();
        session.SetGroupBar(24);
        session.ToggleGroup();
        var first = session.Arrange(Area);
        first.Single(item => item.Id == 3).Y.Should().Be(424, "the armed window keeps its strip free");

        session.Arrange(Area).Should().BeSameAs(first);
        session.Strips(Area, Area).Should().ContainSingle();
        session.Arrange(Area).Should().BeSameAs(first, "asking for the strips moves no tile");

        session.ToggleGroup();

        session.Arrange(Area).Single(item => item.Id == 3).Y.Should().Be(400);
    }

    [TestMethod]
    public void Focusing_the_window_that_already_has_the_focus_hands_out_the_same_tiles()
    {
        var session = Three();
        var first = session.Arrange(Area);

        session.Focus(3);

        session.Arrange(Area).Should().BeSameAs(first);
    }

    [TestMethod]
    public void Resize_moves_the_edge_by_the_pixels_asked()
    {
        var area = new TileRect(0, 0, 1920, 1080);
        var session = new TilingSession();
        session.Add(1, area);
        session.Add(2, area);
        session.Focus(1);

        session.Resize(455, 0, area);

        var placed = session.Arrange(area).ToDictionary(item => item.Id);
        placed[1].Width.Should().Be(505);
        placed[2].Width.Should().Be(1415);
    }

    [TestMethod]
    public void Resize_leaves_the_inner_neighbor_at_the_width_it_had()
    {
        var wide = new TileRect(0, 0, 2560, 1000);
        var session = new TilingSession();
        session.Add(1, wide);
        session.Add(2, wide);
        session.Add(3, wide);
        session.Focus(2);

        session.Resize(-60, 0, wide);

        var placed = session.Arrange(wide).ToDictionary(item => item.Id);
        placed[1].Width.Should().Be(1220);
        placed[2].Width.Should().Be(700);
        placed[3].Width.Should().Be(640);
    }

    [TestMethod]
    public void A_floated_member_of_a_group_that_is_stacked_away_is_not_hidden()
    {
        var session = new TilingSession();
        session.Add(1, Area);
        session.Add(2, Area);
        session.Add(3, Area);
        session.Focus(1);
        session.ToggleGroup();
        session.Add(4, Area);
        session.ToggleFloat();
        session.Focus(3);
        for (var id = 1; id <= 3; id++)
        {
            session.ReportMinimum(id, 600, 500);
        }

        var placed = session.Arrange(Area);

        placed.Single(item => item.Visible).Id.Should().Be(3, "three tiles of 600 by 500 share one cell");
        placed.Should().NotContain(item => item.Id == 4);
    }
}
