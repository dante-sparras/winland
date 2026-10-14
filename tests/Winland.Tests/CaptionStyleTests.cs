using FluentAssertions;
using Winland.Core;

namespace Winland.Tests;

[TestClass]
public sealed class CaptionStyleTests
{
    [TestMethod]
    public void Close_minimize_and_maximize_come_off_and_the_caption_stays()
    {
        const long caption = 0x00C00000;
        const long sysMenu = 0x00080000;
        const long minimize = 0x00020000;
        const long maximize = 0x00010000;
        const long thickFrame = 0x00040000;
        var style = caption | sysMenu | minimize | maximize | thickFrame;

        var next = CaptionStyle.WithoutButtons(style);

        (next & sysMenu).Should().Be(0);
        (next & minimize).Should().Be(0);
        (next & maximize).Should().Be(0);
        (next & thickFrame).Should().Be(0);
        (next & caption).Should().Be(caption);
    }

    [TestMethod]
    public void A_popup_keeps_its_thick_frame_when_the_caption_buttons_come_off()
    {
        const long thickFrame = 0x00040000;
        const long sysMenu = 0x00080000;
        var style = thickFrame | sysMenu;

        var next = CaptionStyle.WithoutButtons(style, keepThickFrame: true);

        (next & thickFrame).Should().Be(thickFrame);
        (next & sysMenu).Should().Be(0);
    }

    [TestMethod]
    public void Minimize_and_maximize_buttons_are_ignored()
    {
        CaptionStyle.IsSizeButton(CaptionStyle.MinimizeButton).Should().BeTrue();
        CaptionStyle.IsSizeButton(CaptionStyle.MaximizeButton).Should().BeTrue();
        CaptionStyle.IsSizeButton(1).Should().BeFalse();
        CaptionStyle.IsSizeButton(2).Should().BeFalse();
        CaptionStyle.IsSizeButton(20).Should().BeFalse();
    }
}
