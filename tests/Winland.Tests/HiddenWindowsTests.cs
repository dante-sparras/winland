using FluentAssertions;
using Winland.Core;

namespace Winland.Tests;

[TestClass]
public sealed class HiddenWindowsTests
{
    private static readonly HiddenWindow Editor = new(1312904, 4567, 134012345678901234, HideWay.Cloak);
    private static readonly HiddenWindow Tool = new(66012, 912, 0, HideWay.Hide);

    private static HiddenReading Now(HiddenWindow window, bool visible, bool cloaked) =>
        new(window.Process, window.Started, visible, cloaked);

    [TestMethod]
    public void A_record_reads_back_what_was_written_with_how_each_window_was_hidden()
    {
        var written = new HiddenWindows(8124, [Editor, Tool]);

        written.Text().Should().Be("desktop 8124\n1312904 4567 134012345678901234 cloak\n66012 912 0 hide\n");

        var read = HiddenWindows.Read(written.Text());

        read.Desktop.Should().Be(8124);
        read.Windows.Should().Equal(written.Windows);
    }

    [TestMethod]
    public void A_rescue_waits_three_seconds_in_all_for_an_Explorer_that_cannot_be_reached()
    {
        // The desktop may have died with Explorer, which takes a moment to come back. The cloak outlives both.
        var total = TimeSpan.Zero;
        var waited = 0;
        while (HiddenWindows.RescueWait(CloakRefusal.Unreachable, waited) is TimeSpan wait)
        {
            total += wait;
            waited++;
        }

        waited.Should().Be(10);
        total.Should().Be(TimeSpan.FromSeconds(3));
    }

    [TestMethod]
    public void A_rescue_does_not_wait_for_a_window_Explorer_reached_and_would_not_uncloak()
    {
        HiddenWindows.RescueWait(CloakRefusal.Refused, 0).Should().BeNull();
        HiddenWindows.RescueWait(CloakRefusal.NoView, 0).Should().BeNull();
        HiddenWindows.RescueWait(CloakRefusal.None, 0).Should().BeNull();
    }

    [TestMethod]
    public void A_missing_or_torn_record_reads_as_the_lines_that_are_whole()
    {
        HiddenWindows.Read(null).Windows.Should().BeEmpty();
        HiddenWindows.Read("").Desktop.Should().Be(0);

        var torn = HiddenWindows.Read("desktop 8124\n1312904 4567 134012345678901234 cloak\n66012 912 0 hi");

        torn.Desktop.Should().Be(8124);
        torn.Windows.Should().Equal(Editor);
    }

    [TestMethod]
    public void A_watcher_takes_only_the_record_its_own_desktop_wrote_and_a_desktop_that_starts_takes_any()
    {
        var record = new HiddenWindows(8124, [Editor]);

        record.WrittenBy(8124).Should().BeTrue();

        // A newer desktop took the record over, or wrote its own. Its windows are hidden on purpose.
        record.WrittenBy(9000).Should().BeFalse();
        record.WrittenBy(null).Should().BeTrue();
    }

    [TestMethod]
    public void A_window_still_cloaked_in_the_same_process_is_stranded()
    {
        var record = new HiddenWindows(8124, [Editor]);

        record.Stranded(_ => Now(Editor, visible: true, cloaked: true)).Should().Equal(Editor);
    }

    [TestMethod]
    public void A_window_still_hidden_with_SW_HIDE_in_the_same_process_is_stranded()
    {
        var record = new HiddenWindows(8124, [Tool]);

        record.Stranded(_ => Now(Tool, visible: false, cloaked: false)).Should().Equal(Tool);
    }

    [TestMethod]
    public void A_window_that_closed_is_not_stranded()
    {
        var record = new HiddenWindows(8124, [Editor]);

        record.Stranded(_ => null).Should().BeEmpty();
    }

    [TestMethod]
    public void A_handle_another_process_took_is_left_alone()
    {
        var record = new HiddenWindows(8124, [Editor]);

        record.Stranded(_ => Now(Editor with { Process = 9000 }, visible: true, cloaked: true)).Should().BeEmpty();
    }

    [TestMethod]
    public void A_process_id_another_program_took_is_left_alone()
    {
        var record = new HiddenWindows(8124, [Editor]);

        record.Stranded(_ => Now(Editor with { Started = Editor.Started + 1 }, visible: true, cloaked: true)).Should().BeEmpty();
    }

    [TestMethod]
    public void A_window_already_on_the_glass_is_left_alone()
    {
        var record = new HiddenWindows(8124, [Editor, Tool]);

        record.Stranded(_ => new HiddenReading(0, 0, Visible: true, Cloaked: false)).Should().BeEmpty();
        record.Stranded(id => Now(id == Editor.Id ? Editor : Tool, visible: true, cloaked: false)).Should().BeEmpty();
    }

    [TestMethod]
    public void A_window_is_put_back_only_the_way_it_was_taken_off()
    {
        var record = new HiddenWindows(8124, [Editor, Tool]);

        // The cloaked one hid itself since, and the hidden one is on another virtual desktop. Neither is Winoma's to undo.
        record.Stranded(id => id == Editor.Id
            ? Now(Editor, visible: false, cloaked: false)
            : Now(Tool, visible: true, cloaked: true)).Should().BeEmpty();
    }
}
