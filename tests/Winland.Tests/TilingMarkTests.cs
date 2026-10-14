using FluentAssertions;
using Winland.Core;

namespace Winland.Tests;

/// <summary>The number a window carries for its Workspace's Layout and its Group (ADR 0050).</summary>
[TestClass]
public sealed class TilingMarkTests
{
    [TestMethod]
    public void A_plain_tile_in_the_dwindle_layout_carries_nothing()
    {
        TilingMark.Write(scrolling: false).Should().Be(TilingMark.None);
        TilingMark.Read(0).Should().Be(default(TilingMark.Reading));
    }

    [TestMethod]
    public void A_scrolling_layout_and_a_group_place_read_back_as_written()
    {
        TilingMark.Read(TilingMark.Write(scrolling: true)).Should().Be(new TilingMark.Reading(true, 0, 0, false));
        TilingMark.Read(TilingMark.Write(false, group: 3, position: 0, shown: false)).Should().Be(new TilingMark.Reading(false, 3, 0, false));
        TilingMark.Read(TilingMark.Write(true, group: 255, position: 254, shown: true)).Should().Be(new TilingMark.Reading(true, 255, 254, true));
    }

    [TestMethod]
    public void A_place_past_what_a_mark_can_name_is_a_plain_tile()
    {
        TilingMark.Read(TilingMark.Write(false, group: 1, position: TilingMark.Largest)).Group.Should().Be(0);
        TilingMark.Read(TilingMark.Write(false, group: TilingMark.Largest + 1, position: 0)).Group.Should().Be(0);
    }

    [TestMethod]
    public void A_number_another_program_set_is_no_mark()
    {
        TilingMark.Read(-1).Should().Be(default(TilingMark.Reading));
        TilingMark.Read(0x1_0000_0001).Should().Be(default(TilingMark.Reading));
        TilingMark.Read(0x4).Should().Be(default(TilingMark.Reading));

        // A shown bit with no Group, and a Group with no place, are not what Winoma writes.
        TilingMark.Read(0x2).Should().Be(default(TilingMark.Reading));
        TilingMark.Read(0x100).Should().Be(default(TilingMark.Reading));
    }
}
