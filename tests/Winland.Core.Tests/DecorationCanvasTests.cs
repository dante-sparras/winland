using Winland;

namespace Winland.Tests;

public class DecorationCanvasTests
{
    private static readonly BorderGradient Red = BorderGradient.Of(new BorderColor(255, 255, 0, 0));
    private static readonly BorderGradient Black = BorderGradient.Of(new BorderColor(255, 0, 0, 0));

    [Fact]
    public void NothingToPaintGivesNoCanvas()
    {
        Assert.Null(DecorationCanvas.Paint(100, 80, null, null, null));
        Assert.Null(DecorationCanvas.Paint(0, 80, new BorderRing(Red, 2, 0, 2, 255), null, null));
        Assert.Null(DecorationCanvas.Paint(100, 80, new BorderRing(Red, 0, 0, 2, 255), null, null));
    }

    [Fact]
    public void TheBorderGoesRoundTheWindowAtFullAlphaAndLeavesItsInsideClear()
    {
        var canvas = DecorationCanvas.Paint(100, 80, new BorderRing(Red, 3, 0, 2, 128), null, null)!;

        Assert.Equal(new Rectangle(-3, -3, 106, 86), canvas.Box);
        Assert.Equal(0xFFFF0000u, At(canvas, -2, 40));
        Assert.Equal(0xFFFF0000u, At(canvas, 50, -3));
        Assert.Equal(0u, At(canvas, 50, 40));
        Assert.Equal(0u, At(canvas, 0, 0));
    }

    [Fact]
    public void TheShadowWidensTheBoxAndTheBorderIsLaidOverIt()
    {
        var shadow = new DropShadow(Black, 10, 3, 255) { Border = 2 };
        var canvas = DecorationCanvas.Paint(100, 80, new BorderRing(Red, 2, 0, 2, 255), shadow, null)!;

        Assert.Equal(new Rectangle(-12, -12, 124, 104), canvas.Box);
        Assert.Equal(0xFFFF0000u, At(canvas, -1, 40));
        Assert.True(At(canvas, -5, 40) >> 24 > 0);
        Assert.Equal(0u, At(canvas, 50, 40));
    }

    [Fact]
    public void TheGlowIsPaintedInsideTheWindowsEdges()
    {
        var glow = new InnerGlow(Red, 6, 2, 255);
        var canvas = DecorationCanvas.Paint(100, 80, null, null, glow)!;

        Assert.Equal(new Rectangle(0, 0, 100, 80), canvas.Box);
        Assert.True(At(canvas, 0, 40) >> 24 > 0);
        Assert.Equal(0u, At(canvas, 50, 40));
    }

    [Theory]
    [InlineData(0xFF102030u, 0xFFFFFFFFu, 0xFF102030u)]
    [InlineData(0x80400000u, 0u, 0x80400000u)]
    [InlineData(0x80400000u, 0xFF0000FFu, 0xFF40007Fu)]
    public void OverKeepsWhatShowsOfThePixelUnder(uint top, uint under, uint blended) =>
        Assert.Equal(blended, DecorationCanvas.Blend(top, under));

    private static uint At(DecorationCanvas canvas, int x, int y) =>
        canvas.Pixels[((y - canvas.Box.Y) * canvas.Box.Width) + (x - canvas.Box.X)];
}
