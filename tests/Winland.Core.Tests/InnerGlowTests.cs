using Winland;

namespace Winland.Tests;

public class InnerGlowTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);
    private static readonly BorderColor Cyan = new(0xEE, 0x33, 0xCC, 0xFF);
    private static readonly BorderColor HalfRed = new(0x80, 0xFF, 0, 0);

    [Fact]
    public void TheGlowIsStrongestAtTheEdgeAndFallsOffInward()
    {
        var linear = new InnerGlow(BorderGradient.Of(Cyan), 10, 1, 255);

        Assert.Equal(0.95, GlowPainter.Strength(0.5, 50, 100, 100, linear), 6);
        Assert.Equal(0.5, GlowPainter.Strength(5, 50, 100, 100, linear), 6);
        Assert.Equal(0.25, GlowPainter.Strength(5, 50, 100, 100, linear with { Power = 2 }), 6);
        Assert.Equal(0.5, GlowPainter.Strength(95, 50, 100, 100, linear), 6);
        Assert.Equal(0, GlowPainter.Strength(50, 50, 100, 100, linear));
        Assert.Equal(0, GlowPainter.Strength(5, 50, 100, 100, linear with { Range = 0 }));
    }

    [Fact]
    public void WhereTwoEdgesMeetTheGlowIsSmoothedAndRoundCornersAreCut()
    {
        var glow = new InnerGlow(BorderGradient.Of(Cyan), 10, 1, 255);

        Assert.Equal(1 - ((5 - (10 / 6.0)) / 10), GlowPainter.Strength(5, 5, 100, 100, glow), 6);
        Assert.True(GlowPainter.Strength(0.5, 0.5, 100, 100, glow) > 0);
        Assert.Equal(0, GlowPainter.Strength(0.5, 0.5, 100, 100, glow with { Rounding = 10 }));
    }

    [Fact]
    public void TheGlowCoversOnlyStripsAlongTheEdges()
    {
        var glow = new InnerGlow(BorderGradient.Of(Cyan), 10, 3, 255);

        var layout = GlowPainter.Layout(new Rectangle(10, 20, 100, 50), glow);

        Assert.NotNull(layout);
        Assert.Equal(new Rectangle(10, 20, 100, 50), layout.Box);
        Assert.Equal(new Rectangle(0, 0, 100, 50), layout.Frame);
        Assert.Equal(
            [
                new Rectangle(0, 0, 100, 15),
                new Rectangle(0, 35, 100, 15),
                new Rectangle(0, 15, 15, 20),
                new Rectangle(85, 15, 15, 20),
            ],
            layout.Pieces);

        // Nothing past the strips would show.
        Assert.Equal(0, GlowPainter.Strength(15.5, 25, 100, 50, glow with { Power = 1 }));
    }

    [Fact]
    public void ASmallWindowsStripsDoNotOverlap()
    {
        var layout = GlowPainter.Layout(new Rectangle(0, 0, 20, 10), new InnerGlow(BorderGradient.Of(Cyan), 10, 3, 255));

        Assert.NotNull(layout);
        Assert.Equal(new Rectangle(0, 0, 20, 10), layout.Pieces[0]);
        Assert.Equal(200, layout.Pieces.Sum(piece => Math.Max(0, piece.Width) * Math.Max(0, piece.Height)));
        Assert.Null(GlowPainter.Layout(new Rectangle(0, 0, 20, 10), new InnerGlow(BorderGradient.Of(Cyan), 0, 3, 255)));
    }

    [Fact]
    public void PaintingWritesPremultipliedPixelsAtTheWindowsOpacity()
    {
        var pixels = new uint[1];

        GlowPainter.Paint(pixels, new InnerGlow(BorderGradient.Of(HalfRed), 10, 1, 128), 1, 1, default, new Rectangle(0, 0, 1, 1), GradientMotion.Still);

        Assert.Equal(0x40400000u, pixels[0]);
    }

    [Fact]
    public void TheGlowIsOffByDefault()
    {
        var core = Started("");

        Assert.Null(Assert.Single(core.Handle(new WindowAppeared(new WindowId(1), Area)).Decorations).Glow);
    }

    [Fact]
    public void AnEnabledGlowTakesItsOptions()
    {
        var document = ConfigParser.Parse("""
            decoration {
                rounding = 10
                glow {
                    enabled = true
                    range = 500
                    render_power = 0
                }
            }
            """);
        Assert.Empty(document.Warnings);
        var core = new Core();
        core.Handle(new Configure(document));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));

        var glow = Assert.Single(core.Handle(new WindowAppeared(new WindowId(1), Area)).Decorations).Glow;

        Assert.NotNull(glow);
        Assert.Equal(100, glow.Range);
        Assert.Equal(1, glow.Power);
        Assert.Equal(9, glow.Rounding);
        Assert.Equal([Cyan], glow.Gradient.Colors);
        Assert.True(glow.Focused);
        Assert.NotNull(glow.Fade);
        Assert.Null(glow.Spin);
    }

    [Fact]
    public void TheRangeFollowsTheMonitorsScaleAndAnUnfocusedClientTakesTheInactiveColor()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("decoration:glow:enabled = 1\ndecoration:glow:color_inactive = rgba(ff000080)\n")));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 2, true, Area)]));
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Area));

        Assert.Equal(20, For(outcome, 2).Glow!.Range);
        Assert.Equal([HalfRed], For(outcome, 1).Glow!.Gradient.Colors);
        Assert.Equal([Cyan], For(outcome, 2).Glow!.Gradient.Colors);
    }

    [Fact]
    public void NoGlowDecorateOffAndFullscreenDrawNoGlow()
    {
        var core = Started("decoration:glow:enabled = true\n");
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        Assert.Null(Assert.Single(core.Execute("dispatch fullscreen 0").Effect.Decorations).Glow);
        core.Execute("dispatch fullscreen 0");
        Assert.NotNull(Assert.Single(core.Execute("dispatch no_op").Effect.Decorations).Glow);
        core.Execute("setprop address:0x1 no_glow 1");
        Assert.Null(Assert.Single(core.Execute("dispatch no_op").Effect.Decorations).Glow);
        core.Execute("setprop address:0x1 no_glow 0");
        core.Execute("setprop address:0x1 decorate 0");
        Assert.Null(Assert.Single(core.Execute("dispatch no_op").Effect.Decorations).Glow);
    }

    [Fact]
    public void TheGlowAngleAndFadeAnimationsAreRead()
    {
        var document = ConfigParser.Parse("""
            animation = glowangle, 1, 10, linear, loop
            animation = fadeGlow, 0
            decoration:glow:enabled = true
            decoration:glow:color = rgb(ff0000) rgb(0000ff) 45deg
            """);
        Assert.Empty(document.Warnings);
        var core = new Core();
        core.Handle(new Configure(document));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));

        var glow = Assert.Single(core.Handle(new WindowAppeared(new WindowId(1), Area)).Decorations).Glow;

        Assert.NotNull(glow);
        Assert.Equal(45, glow.Gradient.AngleDegrees);
        Assert.Equal(new Timing(Bezier.Linear, 1000), glow.Spin);
        Assert.True(glow.SpinLoops);
        Assert.Null(glow.Fade);
    }

    private static Core Started(string config)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(config)));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));
        return core;
    }

    private static Decoration For(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        return Assert.Single(outcome.Decorations, decoration => decoration.Client == client.Id);
    }
}
