namespace Winland.Infrastructure.Tests;

public class TrayBadgeTests
{
    private const int Size = 16;

    // A font's cell puts a digit above and beside the middle. The label moves so its ink sits on the center.
    [Fact]
    public void TheLabelIsCenteredByItsInk()
    {
        var pixels = new uint[Size * Size];
        for (var y = 2; y < 8; y++)
            pixels[(y * Size) + 3] = 0xFFFFFF;

        TrayBadge.Compose(pixels, Size);

        Assert.Equal(0xFFFFFFFFu, pixels[(5 * Size) + 8]);
        Assert.Equal(0xFFFFFFFFu, pixels[(10 * Size) + 8]);
        Assert.NotEqual(0xFFFFFFFFu, pixels[(4 * Size) + 8]);
        Assert.NotEqual(0xFFFFFFFFu, pixels[(11 * Size) + 8]);
        Assert.NotEqual(0xFFFFFFFFu, pixels[(7 * Size) + 3]);
    }

    [Fact]
    public void TheDiscIsOpaqueInsideClearOutsideAndPartlyCoveredOnItsEdge()
    {
        var pixels = new uint[Size * Size];

        TrayBadge.Compose(pixels, Size);

        Assert.Equal(0xFF2F6FEDu, pixels[(8 * Size) + 8]);
        Assert.True(TrayBadge.Clear(pixels[0]));
        Assert.Contains(pixels, pixel => pixel >> 24 is > 0 and < 255);
    }
}
