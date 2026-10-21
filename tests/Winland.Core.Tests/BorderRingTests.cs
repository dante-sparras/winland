using Winland;

namespace Winland.Tests;

public class BorderRingTests
{
    private static readonly BorderColor Cyan = new(0xFF, 0x33, 0xCC, 0xFF);
    private static readonly BorderColor Green = new(0xFF, 0x00, 0xFF, 0x99);
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void TheRingCoversOnlyTheBandAroundTheWindow()
    {
        var ring = new BorderRing(BorderGradient.Of(Cyan), 4, 0, 2, 255);

        Assert.Equal(1, BorderPainter.Coverage(0, 50, 100, 100, ring));
        Assert.Equal(1, BorderPainter.Coverage(3, 50, 100, 100, ring));
        Assert.Equal(0, BorderPainter.Coverage(4, 50, 100, 100, ring));
        Assert.Equal(0, BorderPainter.Coverage(50, 50, 100, 100, ring));
        Assert.Equal(1, BorderPainter.Coverage(99, 99, 100, 100, ring));
    }

    [Fact]
    public void RoundCornersLeaveTheOuterCornerClear()
    {
        var ring = new BorderRing(BorderGradient.Of(Cyan), 4, 10, 2, 255);

        Assert.Equal(0, BorderPainter.Coverage(0, 0, 100, 100, ring));
        Assert.Equal(1, BorderPainter.Coverage(50, 1, 100, 100, ring));
        Assert.Equal(14, ring.OuterRadius, 6);

        // A flatter corner gives some of the outer radius back, as Winland's correction does.
        Assert.True(new BorderRing(BorderGradient.Of(Cyan), 4, 10, 1, 255).OuterRadius < 14);
    }

    [Fact]
    public void AGradientRunsAlongItsAngle()
    {
        var gradient = new BorderGradient([Cyan, Green], 0);

        Assert.Equal(Cyan, BorderPainter.ColorAt(gradient, 0, 0.5));
        Assert.Equal(Green, BorderPainter.ColorAt(gradient, 1, 0.5));

        var down = new BorderGradient([Cyan, Green], 90);
        Assert.Equal(Cyan, BorderPainter.ColorAt(down, 0.5, 0));
        Assert.Equal(Green, BorderPainter.ColorAt(down, 0.5, 1));
    }

    [Fact]
    public void PaintingWritesPremultipliedPixelsOnTheRingOnly()
    {
        var ring = new BorderRing(BorderGradient.Of(new BorderColor(0x80, 0xFF, 0, 0)), 2, 0, 2, 255);
        var pixels = new uint[10 * 10];

        BorderPainter.Paint(pixels, ring, 10, 10, new Rectangle(0, 0, 10, 10));

        Assert.Equal(0x80800000u, pixels[0]);
        Assert.Equal(0u, pixels[(5 * 10) + 5]);
    }

    [Fact]
    public void EachClientGetsARingFromItsBorderSizeAndColors()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("general:border_size = 3\ngeneral:col.active_border = rgba(33ccffff) rgba(00ff99ff) 45deg\n")));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area));

        var ring = Assert.Single(outcome.Decorations).Ring;
        Assert.NotNull(ring);
        Assert.Equal(3, ring.Thickness);
        Assert.Equal([Cyan, Green], ring.Gradient.Colors);
        Assert.Equal(45, ring.Gradient.AngleDegrees);
    }

    [Fact]
    public void NoRingWithoutABorderInFullscreenOrWithDecorateOff()
    {
        var none = new Core(ConfigParser.Parse("general:border_size = 0\n"));
        none.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));
        Assert.Null(Assert.Single(none.Handle(new WindowAppeared(new WindowId(1), Area)).Decorations).Ring);

        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("general:border_size = 2\n")));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));
        Assert.NotNull(Assert.Single(core.Handle(new WindowAppeared(new WindowId(1), Area)).Decorations).Ring);

        Assert.Null(Assert.Single(core.Execute("dispatch fullscreen 0").Effect.Decorations).Ring);
        core.Execute("dispatch fullscreen 0");
        core.Execute("setprop address:0x1 decorate 0");
        Assert.Null(Assert.Single(core.Execute("dispatch no_op").Effect.Decorations).Ring);
    }

    [Fact]
    public void AGroupBorderTakesItsGradient()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("general:border_size = 2\ngroup:col.border_active = rgba(33ccffff) rgba(00ff99ff) 90deg\n")));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var ring = Assert.Single(core.Execute("dispatch togglegroup").Effect.Decorations).Ring;

        Assert.NotNull(ring);
        Assert.Equal([Cyan, Green], ring.Gradient.Colors);
        Assert.Equal(90, ring.Gradient.AngleDegrees);
    }

    [Fact]
    public void ATurnGoesOnceRoundOverItsAnimation()
    {
        var spin = new Timing(Bezier.Linear, 1000);

        Assert.Equal(0, BorderPainter.Turn(spin, 0));
        Assert.Equal(90, BorderPainter.Turn(spin, 250), 3);
        Assert.Equal(360, BorderPainter.Turn(spin, 2000));
    }

    [Fact]
    public void TheBorderAngleAnimationTurnsAGradientRing()
    {
        var document = ConfigParser.Parse("""
            animation = borderangle, 1, 10, linear, loop
            general:border_size = 2
            general:col.active_border = rgb(ff0000) rgb(0000ff)
            """);
        Assert.Empty(document.Warnings);
        Assert.Contains(ConfigParser.Parse("animation = borderangle, 1, 10, linear, spin\n").Warnings, warning => warning.Message == "Style 'spin' is not valid for animation 'borderangle'.");

        var core = new Core();
        core.Handle(new Configure(document));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));
        var ring = Assert.Single(core.Handle(new WindowAppeared(new WindowId(1), Area)).Decorations).Ring;

        Assert.NotNull(ring);
        Assert.Equal(new Timing(Bezier.Linear, 1000), ring.Spin);
        Assert.True(ring.SpinLoops);
        Assert.True(ring.Spins);
        Assert.True(ring.Focused);
        Assert.False((ring with { Gradient = BorderGradient.Of(Cyan) }).Spins);
    }
}
