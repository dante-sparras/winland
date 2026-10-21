using System.Text.Json;
using Winland;

namespace Winland.Tests;

public class DropShadowTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);
    private static readonly BorderColor HalfRed = new(0x80, 0xFF, 0, 0);
    private static readonly BorderColor Black = new(0xFF, 0, 0, 0);
    private static readonly BorderColor White = new(0xFF, 0xFF, 0xFF, 0xFF);

    [Fact]
    public void TheShadowFallsOffOverItsRangeAtItsPower()
    {
        var linear = new DropShadow(BorderGradient.Of(Black), 10, 1, 255);
        var squared = linear with { Power = 2 };

        Assert.Equal(1, ShadowPainter.Falloff(50, 50, 100, 100, linear));
        Assert.Equal(0.5, ShadowPainter.Falloff(5, 50, 100, 100, linear), 6);
        Assert.Equal(0.05, ShadowPainter.Falloff(0.5, 50, 100, 100, linear), 6);
        Assert.Equal(0.25, ShadowPainter.Falloff(5, 50, 100, 100, squared), 6);
        Assert.Equal(0.5, ShadowPainter.Falloff(95, 50, 100, 100, linear), 6);
    }

    [Fact]
    public void TheShadowRoundsItsCornersOverTheRange()
    {
        var shadow = new DropShadow(BorderGradient.Of(Black), 10, 1, 255);

        Assert.Equal(0, ShadowPainter.Falloff(0.5, 0.5, 100, 100, shadow));
        Assert.Equal(1 - (Math.Sqrt(50) / 10), ShadowPainter.Falloff(5, 5, 100, 100, shadow), 6);
        Assert.Equal(1, ShadowPainter.Falloff(10, 10, 100, 100, shadow));

        // A flatter rounding power reaches further into the corner.
        Assert.True(ShadowPainter.Falloff(5, 5, 100, 100, shadow with { RoundingPower = 4 }) > ShadowPainter.Falloff(5, 5, 100, 100, shadow));
    }

    [Fact]
    public void TheBoxGrowsByTheBorderAndRangeAndMovesByTheOffset()
    {
        var shadow = new DropShadow(BorderGradient.Of(Black), 10, 3, 255) { Border = 2, OffsetX = 5, OffsetY = -3 };

        var layout = ShadowPainter.Layout(new Rectangle(100, 100, 200, 100), shadow);

        Assert.NotNull(layout);
        Assert.Equal(new Rectangle(93, 85, 224, 124), layout.Box);
        Assert.Equal(new Rectangle(7, 15, 200, 100), layout.Frame);
        Assert.Equal(
            [
                new Rectangle(0, 0, 224, 15),
                new Rectangle(0, 115, 224, 9),
                new Rectangle(0, 15, 7, 100),
                new Rectangle(207, 15, 17, 100),
            ],
            layout.Pieces);
    }

    [Fact]
    public void TheHoleUnderTheWindowStopsAtItsCorners()
    {
        var shadow = new DropShadow(BorderGradient.Of(Black), 4, 3, 255) { WindowRounding = 6 };

        var layout = ShadowPainter.Layout(new Rectangle(0, 0, 100, 50), shadow);

        Assert.NotNull(layout);
        Assert.Equal(new Rectangle(-4, -4, 108, 58), layout.Box);
        Assert.Equal(new Rectangle(0, 0, 108, 10), layout.Pieces[0]);
        Assert.Equal(new Rectangle(0, 48, 108, 10), layout.Pieces[1]);
        Assert.Equal(new Rectangle(0, 10, 10, 38), layout.Pieces[2]);
    }

    [Fact]
    public void AScaledShadowShrinksAboutItsMiddleAndCanHideUnderTheWindow()
    {
        var shadow = new DropShadow(BorderGradient.Of(Black), 0, 3, 255) { Scale = 0.5 };

        var layout = ShadowPainter.Layout(new Rectangle(0, 0, 100, 100), shadow);

        Assert.NotNull(layout);
        Assert.Equal(new Rectangle(25, 25, 50, 50), layout.Box);
        Assert.All(layout.Pieces, piece => Assert.True(piece.Width <= 0 || piece.Height <= 0));
        Assert.Null(ShadowPainter.Layout(new Rectangle(0, 0, 4, 4), shadow with { Scale = 0.05 }));
    }

    [Fact]
    public void ThePlaceUnderTheWindowFollowsItsCorners()
    {
        var frame = new Rectangle(10, 10, 80, 80);

        Assert.True(ShadowPainter.UnderWindow(50, 50, frame, 10, 2));
        Assert.False(ShadowPainter.UnderWindow(5, 50, frame, 10, 2));
        Assert.False(ShadowPainter.UnderWindow(10.5, 10.5, frame, 10, 2));
        Assert.True(ShadowPainter.UnderWindow(10.5, 10.5, frame, 0, 2));
    }

    [Fact]
    public void PaintingCutsTheShadowAwayUnderTheWindowUnlessItIsSharp()
    {
        var shadow = new DropShadow(BorderGradient.Of(HalfRed), 0, 3, 255);
        var soft = new uint[10 * 10];
        var sharp = new uint[10 * 10];

        ShadowPainter.Paint(soft, shadow, 10, 10, new Rectangle(2, 2, 6, 6), new Rectangle(0, 0, 10, 10), GradientMotion.Still);
        ShadowPainter.Paint(sharp, shadow with { Sharp = true }, 10, 10, new Rectangle(2, 2, 6, 6), new Rectangle(0, 0, 10, 10), GradientMotion.Still);

        Assert.Equal(0x80800000u, soft[0]);
        Assert.Equal(0u, soft[(5 * 10) + 5]);
        Assert.Equal(0x80800000u, sharp[(5 * 10) + 5]);
    }

    [Fact]
    public void TheShadowTakesTheWindowsOpacity()
    {
        var pixels = new uint[4];

        ShadowPainter.Paint(pixels, new DropShadow(BorderGradient.Of(HalfRed), 0, 3, 128), 2, 2, default, new Rectangle(0, 0, 2, 2), GradientMotion.Still);

        Assert.Equal(0x40400000u, pixels[0]);
    }

    [Fact]
    public void AFadeMixesTheOldColorsIntoTheNewOnes()
    {
        var shadow = new DropShadow(BorderGradient.Of(White), 0, 3, 255);
        var from = BorderGradient.Of(Black);

        Assert.Equal(0xFF000000u, PaintOne(shadow, new GradientMotion(0, from, 0)));
        Assert.Equal(0xFFFFFFFFu, PaintOne(shadow, new GradientMotion(0, from, 1)));
        var middle = PaintOne(shadow, new GradientMotion(0, from, 0.5));
        var red = (middle >> 16) & 0xFF;
        Assert.InRange(red, 1u, 254u);
        Assert.Equal(red, middle & 0xFF);

        // A sharp shadow blends its first colors in plain RGB.
        Assert.Equal(0xFF808080u, PaintOne(shadow with { Sharp = true }, new GradientMotion(0, from, 0.5)));
    }

    [Fact]
    public void AGradientShadowTurnsWithItsAngle()
    {
        var shadow = new DropShadow(new BorderGradient([Black, White], 0), 0, 3, 255);
        var pixels = new uint[100];

        ShadowPainter.Paint(pixels, shadow, 100, 1, default, new Rectangle(0, 0, 100, 1), GradientMotion.Still);
        Assert.InRange(pixels[0] & 0xFF, 0u, 0x10u);
        Assert.InRange(pixels[99] & 0xFF, 0xF0u, 0xFFu);

        ShadowPainter.Paint(pixels, shadow, 100, 1, default, new Rectangle(0, 0, 100, 1), new GradientMotion(180, null, 1));
        Assert.InRange(pixels[0] & 0xFF, 0xF0u, 0xFFu);
        Assert.InRange(pixels[99] & 0xFF, 0u, 0x10u);
    }

    [Fact]
    public void AFadeAndATurnEaseAlongTheirTiming()
    {
        var timing = new Timing(Bezier.Linear, 1000);

        Assert.Equal(0.25, GradientMotion.Eased(timing, 250), 6);
        Assert.Equal(1, GradientMotion.Eased(timing, 5000));
        Assert.Equal(1, GradientMotion.Eased(new Timing(Bezier.Linear, 0), 0));
        Assert.True(GradientMotion.Same(BorderGradient.Of(Black), BorderGradient.Of(Black)));
        Assert.False(GradientMotion.Same(BorderGradient.Of(Black), new BorderGradient([Black], 90)));
    }

    [Fact]
    public void TheShadowOptionsAreReadAndClamped()
    {
        var document = ConfigParser.Parse("""
            decoration {
                shadow {
                    range = 500
                    render_power = 9
                    sharp = true
                    color = rgba(ff000080)
                    color_inactive = rgba(00000040) rgba(ffffff40) 90deg
                    offset = 300 -4.5
                    scale = 3
                }
            }
            """);

        var options = DecorationOptions.From(document);

        Assert.Empty(document.Warnings);
        Assert.True(options.ShadowDrawn);
        Assert.Equal(100, options.ShadowRange);
        Assert.Equal(4, options.ShadowPower);
        Assert.True(options.ShadowSharp);
        Assert.Equal([HalfRed], options.ShadowColor.Colors);
        Assert.NotNull(options.ShadowColorInactive);
        Assert.Equal(90, options.ShadowColorInactive.AngleDegrees);
        Assert.Equal(new Vec2(250, -4.5), options.ShadowOffset);
        Assert.Equal(2f, options.ShadowScale);
        Assert.False(DecorationOptions.From(ConfigParser.Parse("decoration:shadow:enabled = true\n")).ShadowDrawn);
    }

    [Fact]
    public void ByDefaultDwmKeepsItsShadowAndWinlandDrawsNone()
    {
        var core = Started("");

        var decoration = Assert.Single(core.Handle(new WindowAppeared(new WindowId(1), Area)).Decorations);

        Assert.True(decoration.Shadow);
        Assert.Null(decoration.DrawnShadow);
    }

    [Fact]
    public void ATunedShadowIsDrawnByWinlandAndDwmsIsTurnedOff()
    {
        var core = Started("""
            general:border_size = 2
            decoration:rounding = 10
            decoration:shadow:range = 10
            decoration:shadow:color = rgba(ff000080)
            """);

        var decoration = Assert.Single(core.Handle(new WindowAppeared(new WindowId(1), Area)).Decorations);

        Assert.False(decoration.Shadow);
        var shadow = decoration.DrawnShadow;
        Assert.NotNull(shadow);
        Assert.Equal(10, shadow.Range);
        Assert.Equal(3, shadow.Power);
        Assert.Equal(2, shadow.Border);
        Assert.Equal(10, shadow.WindowRounding);
        Assert.Equal(12, shadow.Rounding);
        Assert.Equal([HalfRed], shadow.Gradient.Colors);
        Assert.True(shadow.Focused);
        Assert.NotNull(shadow.Fade);
        Assert.Null(shadow.Spin);
    }

    [Fact]
    public void WithoutBorderPartOfWindowTheShadowGrowsFromTheWindowAlone()
    {
        var core = Started("""
            general:border_size = 2
            decoration:rounding = 10
            decoration:shadow:range = 10
            decoration:border_part_of_window = false
            """);

        var shadow = Assert.Single(core.Handle(new WindowAppeared(new WindowId(1), Area)).Decorations).DrawnShadow;

        Assert.NotNull(shadow);
        Assert.Equal(0, shadow.Border);
        Assert.Equal(10, shadow.Rounding);
        Assert.Equal(10, shadow.WindowRounding);
        Assert.True(DecorationOptions.From(ConfigParser.Parse("")).BorderPartOfWindow);
        Assert.False(DecorationOptions.From(ConfigParser.Parse("decoration:border_part_of_window = 0")).ShadowDrawn);
    }

    [Fact]
    public void TheRangeAndOffsetFollowTheMonitorsScale()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("decoration:shadow:offset = 3 -2\n")));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 2, true, Area)]));

        var shadow = Assert.Single(core.Handle(new WindowAppeared(new WindowId(1), Area)).Decorations).DrawnShadow;

        Assert.NotNull(shadow);
        Assert.Equal(8, shadow.Range);
        Assert.Equal(6, shadow.OffsetX);
        Assert.Equal(-4, shadow.OffsetY);
    }

    [Fact]
    public void AnUnfocusedClientTakesTheInactiveColor()
    {
        var core = Started("decoration:shadow:color_inactive = rgba(00000040)\n");
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Area));

        Assert.Equal([new BorderColor(0x40, 0, 0, 0)], For(outcome, 1).DrawnShadow!.Gradient.Colors);
        Assert.Equal([new BorderColor(0xEE, 0x1A, 0x1A, 0x1A)], For(outcome, 2).DrawnShadow!.Gradient.Colors);
    }

    [Fact]
    public void NoShadowTurnsOffBothShadows()
    {
        var tuned = Started("decoration:shadow:range = 6\n");
        tuned.Handle(new WindowAppeared(new WindowId(1), Area));
        Assert.Equal("ok", tuned.Execute("setprop address:0x1 no_shadow 1").Text);

        var drawn = Assert.Single(tuned.Execute("dispatch no_op").Effect.Decorations);

        Assert.Null(drawn.DrawnShadow);
        Assert.False(drawn.Shadow);

        var plain = Started("windowrulev2 = no_shadow, class:.*\n");
        var dwm = Assert.Single(plain.Handle(new WindowAppeared(new WindowId(1), Area)).Decorations);
        Assert.False(dwm.Shadow);
    }

    [Fact]
    public void AWorkspaceRuleTurnsTheShadowOffOnItsWorkspace()
    {
        var document = ConfigParser.Parse("workspace = 1, shadow:0\ndecoration:shadow:range = 6\n");
        Assert.Empty(document.Warnings);
        var core = new Core();
        core.Handle(new Configure(document));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));

        var decoration = Assert.Single(core.Handle(new WindowAppeared(new WindowId(1), Area)).Decorations);

        Assert.Null(decoration.DrawnShadow);
        Assert.False(decoration.Shadow);
        Assert.Contains("true", core.Execute("getprop activewindow no_shadow").Text, StringComparison.Ordinal);
        using var rules = JsonDocument.Parse(core.Execute("workspacerules").Text);
        Assert.False(rules.RootElement[0].GetProperty("shadow").GetBoolean());
    }

    [Fact]
    public void TheShadowAngleAndFadeAnimationsAreRead()
    {
        var document = ConfigParser.Parse("""
            animation = shadowangle, 1, 10, linear, loop
            animation = fadeShadow, 0
            decoration:shadow:color = rgb(ff0000) rgb(0000ff)
            """);
        Assert.Empty(document.Warnings);
        var core = new Core();
        core.Handle(new Configure(document));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));

        var shadow = Assert.Single(core.Handle(new WindowAppeared(new WindowId(1), Area)).Decorations).DrawnShadow;

        Assert.NotNull(shadow);
        Assert.Equal(new Timing(Bezier.Linear, 1000), shadow.Spin);
        Assert.True(shadow.SpinLoops);
        Assert.Null(shadow.Fade);
        Assert.Contains(
            ConfigParser.Parse("animation = shadowangle, 1, 10, linear, spin\n").Warnings,
            warning => warning.Message == "Style 'spin' is not valid for animation 'shadowangle'.");
    }

    [Fact]
    public void FullscreenAndDecorateOffDrawNoShadow()
    {
        var core = Started("decoration:shadow:range = 6\n");
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        Assert.Null(Assert.Single(core.Execute("dispatch fullscreen 0").Effect.Decorations).DrawnShadow);
        core.Execute("dispatch fullscreen 0");
        core.Execute("setprop address:0x1 decorate 0");
        Assert.Null(Assert.Single(core.Execute("dispatch no_op").Effect.Decorations).DrawnShadow);
    }

    private static uint PaintOne(DropShadow shadow, GradientMotion motion)
    {
        var pixels = new uint[1];
        ShadowPainter.Paint(pixels, shadow, 1, 1, default, new Rectangle(0, 0, 1, 1), motion);
        return pixels[0];
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
