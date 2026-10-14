using FluentAssertions;
using Winland.Core;

namespace Winland.Tests;

/// <summary>A window carries the caption bits Winoma took off it, and gets them back from that.</summary>
[TestClass]
public sealed class CaptionLedgerTests
{
    private const long Caption = 0x00C00000;
    private const long Border = 0x00800000;
    private const long SysMenu = 0x00080000;
    private const long ThickFrame = 0x00040000;
    private const long MinimizeBox = 0x00020000;

    [TestMethod]
    public void The_bits_a_change_took_off_are_what_the_window_gets_back()
    {
        var before = Caption | SysMenu | ThickFrame;
        var after = CaptionStyle.WithoutButtons(before);

        var bits = CaptionLedger.Marked(0, before, after);

        bits.Should().Be(SysMenu | ThickFrame);
        CaptionLedger.Returns(after, bits, out var next).Should().BeTrue();
        next.Should().Be(before);
    }

    [TestMethod]
    public void A_change_that_took_nothing_off_marks_nothing()
    {
        CaptionLedger.Marked(0, Caption, Caption).Should().Be(0);
    }

    [TestMethod]
    public void A_second_change_to_the_same_window_adds_its_bits()
    {
        var first = CaptionLedger.Marked(0, Caption | SysMenu, Caption);

        CaptionLedger.Marked(first, Caption | MinimizeBox, Caption).Should().Be(SysMenu | MinimizeBox);
    }

    [TestMethod]
    public void A_window_that_carries_nothing_is_left_alone()
    {
        CaptionLedger.Returns(Caption, 0, out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_window_that_has_its_bits_already_is_left_alone()
    {
        CaptionLedger.Returns(Caption | SysMenu, SysMenu, out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_window_that_has_gone_fullscreen_since_keeps_no_frame()
    {
        CaptionLedger.Returns(0, SysMenu | ThickFrame, out _).Should().BeFalse();
    }

    [TestMethod]
    public void A_tile_with_half_a_caption_gets_its_frame_back()
    {
        CaptionLedger.Returns(Border, ThickFrame, out var next).Should().BeTrue();
        next.Should().Be(Border | ThickFrame);
    }
}
