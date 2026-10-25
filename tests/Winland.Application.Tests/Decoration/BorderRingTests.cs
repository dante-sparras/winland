namespace Winland.Application.Tests;

public class BorderRingTests
{
    private static readonly BorderColor Cyan = new(0xFF, 0x33, 0xCC, 0xFF);
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void TheRingCoversOnlyTheBandAroundTheWindow()
    {
        var ring = new BorderRing(Cyan, 4, 0, 255);

        Assert.Equal(1, BorderPainter.Coverage(0, 50, 100, 100, ring));
        Assert.Equal(1, BorderPainter.Coverage(3, 50, 100, 100, ring));
        Assert.Equal(0, BorderPainter.Coverage(4, 50, 100, 100, ring));
        Assert.Equal(0, BorderPainter.Coverage(50, 50, 100, 100, ring));
        Assert.Equal(1, BorderPainter.Coverage(99, 99, 100, 100, ring));
    }

    [Fact]
    public void RoundCornersLeaveTheOuterCornerClear()
    {
        var ring = new BorderRing(Cyan, 4, 10, 255);

        Assert.Equal(0, BorderPainter.Coverage(0, 0, 100, 100, ring));
        Assert.Equal(1, BorderPainter.Coverage(50, 1, 100, 100, ring));
        Assert.Equal(14, ring.OuterRadius, 6);
    }

    [Fact]
    public void PaintingWritesPremultipliedPixelsOnTheRingOnly()
    {
        var ring = new BorderRing(new BorderColor(0x80, 0xFF, 0, 0), 2, 0, 255);
        var pixels = new uint[10 * 10];

        BorderPainter.Paint(pixels, ring, 10, 10, new Rectangle(0, 0, 10, 10));

        Assert.Equal(0x80800000u, pixels[0]);
        Assert.Equal(0u, pixels[(5 * 10) + 5]);
    }

    [Fact]
    public void EachClientGetsARingFromItsBorderSizeAndColors()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("general:border_size = 3\ngeneral:col.active_border = rgba(33ccffff)\n")));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area));

        var ring = Assert.Single(outcome.Decorations).Ring;
        Assert.NotNull(ring);
        Assert.Equal(3, ring.Thickness);
        Assert.Equal(Cyan, ring.Color);
    }

    [Fact]
    public void NoRingWithoutABorderInFullscreenOrWithDecorateOff()
    {
        var none = new WindowManager(ConfigParser.Parse("general:border_size = 0\n"));
        none.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));
        Assert.Null(Assert.Single(none.Handle(new WindowAppeared(new WindowId(1), Area)).Decorations).Ring);

        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("general:border_size = 2\n")));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));
        Assert.NotNull(Assert.Single(core.Handle(new WindowAppeared(new WindowId(1), Area)).Decorations).Ring);

        Assert.Null(Assert.Single(core.Execute("dispatch fullscreen 0").Effect.Decorations).Ring);
        core.Execute("dispatch fullscreen 0");
        core.Execute("setprop address:0x1 decorate 0");
        Assert.Null(Assert.Single(core.Execute("dispatch no_op").Effect.Decorations).Ring);
    }

    [Fact]
    public void AGroupBorderTakesItsColorAndAGradientDoesNot()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("general:border_size = 2\ngroup:col.border_active = rgba(33ccffff)\n")));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var ring = Assert.Single(core.Execute("dispatch togglegroup").Effect.Decorations).Ring;

        Assert.NotNull(ring);
        Assert.Equal(Cyan, ring.Color);

        var kept = new WindowManager();
        kept.Handle(new Configure(ConfigParser.Parse("general:border_size = 2\ngroup:col.border_active = rgba(33ccffff) rgba(00ff99ff) 90deg\n")));
        kept.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));
        kept.Handle(new WindowAppeared(new WindowId(1), Area));
        Assert.Equal(GroupOptions.Documented.ActiveBorder, Assert.Single(kept.Execute("dispatch togglegroup").Effect.Decorations).Ring!.Color);
    }

    [Fact]
    public void TheBorderAngleAnimationDoesNotExist()
    {
        var document = ConfigParser.Parse("animation = borderangle, 1, 10, linear, loop\n");

        Assert.Contains(document.Warnings, warning => warning.Message == "Animation 'borderangle' does not exist.");
    }
}
