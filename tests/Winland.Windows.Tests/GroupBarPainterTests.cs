using Winland.Windows;

namespace Winland.Windows.Tests;

public class GroupBarPainterTests
{
    // GDI leaves the alpha of the pixels it draws text on at 0. They take the title color's alpha,
    // premultiplied, and the rest of the strip keeps its own.
    [Fact]
    public void TitlePixelsTakeTheTitleColorsAlpha()
    {
        uint[] pixels = [0x00FF8040, 0x01000000];

        GroupBarPainter.ApplyTextAlpha(pixels, 2, 1, new Rectangle(0, 0, 2, 1), 128);

        Assert.Equal(0x80804020u, pixels[0]);
        Assert.Equal(0x01000000u, pixels[1]);
    }

    // A fully transparent title still leaves the strip's alpha of 1, so the bar keeps taking clicks.
    [Fact]
    public void ATransparentTitleStillTakesClicks()
    {
        uint[] pixels = [0x00FFFFFF];

        GroupBarPainter.ApplyTextAlpha(pixels, 1, 1, new Rectangle(0, 0, 1, 1), 0);

        Assert.Equal(0x01000000u, pixels[0]);
    }
}
