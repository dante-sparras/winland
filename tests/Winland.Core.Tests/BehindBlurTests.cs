using Winland;

namespace Winland.Tests;

public class BehindBlurTests
{
    private static readonly Rectangle Area = new(0, 0, 800, 600);

    [Fact]
    public void ShaderParametersClampThePassesAndSplitBrightness()
    {
        var parameters = BlurShaderParameters.From(new BlurTuning
        {
            Size = 12,
            Passes = 0,
            Noise = 0.4f,
            Contrast = 1.5f,
            Brightness = 1.4f,
            Vibrancy = 0.2f,
            VibrancyDarkness = 0.3f,
        });

        Assert.Equal(1, parameters.Passes);
        Assert.Equal(12, parameters.Radius);
        Assert.Equal(1.5f, parameters.Contrast);
        Assert.Equal(1.4f, parameters.PrepareBrightness);
        Assert.Equal(1f, parameters.FinishBrightness);
        Assert.Equal(0.4f, parameters.Noise);
        Assert.Equal(0.2f, parameters.Vibrancy);
        Assert.Equal(0.3f, parameters.VibrancyDarkness);

        var heavy = BlurShaderParameters.From(new BlurTuning { Passes = 99, Size = -3, Brightness = 0.25f, Contrast = float.NaN, Noise = float.PositiveInfinity });
        Assert.Equal(BlurPipeline.MaxPasses, heavy.Passes);
        Assert.Equal(0, heavy.Radius);
        Assert.Equal(1f, heavy.Contrast);
        Assert.Equal(1f, heavy.PrepareBrightness);
        Assert.Equal(0.25f, heavy.FinishBrightness);
        Assert.Equal(0f, heavy.Noise);

        var buffer = new float[16];
        parameters.Write(buffer, 40, 20, 10, 8);
        Assert.Equal([12f, 1.5f, 1.4f, 1f, 0.4f, 0.2f, 0.3f, 1f, 40f, 20f, 10f, 8f, 0f, 0f, 0f, 0f], buffer);
    }

    [Fact]
    public void SpreadGrowsWithThePassesAndKeepsATapWhenTheSizeIsZero()
    {
        Assert.Equal(16, BlurShaderParameters.Spread(new BlurTuning { Size = 8, Passes = 1 }));
        Assert.Equal(2, BlurShaderParameters.Spread(new BlurTuning { Size = 0, Passes = 1 }));
        Assert.Equal(48, BlurShaderParameters.Spread(new BlurTuning { Size = 8, Passes = 2 }));
        Assert.Equal(2 * 8 * ((1 << BlurPipeline.MaxPasses) - 1), BlurShaderParameters.Spread(new BlurTuning { Size = 8, Passes = 10 }));
    }

    [Fact]
    public void CoverGrowsTheRectangleUntilASideWouldPassTheLimit()
    {
        Assert.Equal(new Rectangle(-4, 6, 28, 48), BehindBlurPlan.Cover(new Rectangle(0, 10, 20, 40), 4));
        Assert.Equal(new Rectangle(0, 10, 20, 40), BehindBlurPlan.Cover(new Rectangle(0, 10, 20, 40), 0));
        Assert.Equal(new Rectangle(0, 0, 4000, 100), BehindBlurPlan.Cover(new Rectangle(0, 0, 4000, 100), 100));
    }

    [Fact]
    public void AQuadMapsTheOverlapOntoTheWindowTexture()
    {
        Assert.True(BehindBlurPlan.TryQuad(new Rectangle(0, 0, 100, 80), new Rectangle(10, 20, 30, 40), out var inside));
        Assert.Equal(new BlurQuad(10, 20, 30, 40, 0, 0, 1, 1), inside);

        Assert.True(BehindBlurPlan.TryQuad(new Rectangle(0, 0, 100, 100), new Rectangle(-10, -20, 30, 40), out var clipped));
        Assert.Equal(0f, clipped.X);
        Assert.Equal(0f, clipped.Y);
        Assert.Equal(20f, clipped.Width);
        Assert.Equal(20f, clipped.Height);
        Assert.Equal(10f / 30f, clipped.U0);
        Assert.Equal(20f / 40f, clipped.V0);
        Assert.Equal(1f, clipped.U1);
        Assert.Equal(1f, clipped.V1);
        Assert.False(BehindBlurPlan.TryQuad(new Rectangle(0, 0, 10, 10), new Rectangle(20, 20, 5, 5), out _));
    }

    [Fact]
    public void SourcesFollowTheStackSkipTheClientAndLeaveOutWhatDoesNotOverlap()
    {
        var visible = new[]
        {
            new BehindCandidate(new WindowId(1), new Rectangle(0, 0, 100, 100)),
            new BehindCandidate(new WindowId(2), new Rectangle(50, 0, 100, 100)),
            new BehindCandidate(new WindowId(3), new Rectangle(400, 400, 10, 10)),
            new BehindCandidate(new WindowId(4), new Rectangle(0, 0, 80, 80)),
        };
        var skip = new HashSet<WindowId> { new(2) };

        var sources = BehindBlurPlan.Sources([new WindowId(1), new WindowId(2), new WindowId(3)], visible, new Rectangle(0, 0, 120, 120), skip);

        Assert.Equal([new WindowId(1), new WindowId(4)], sources);
    }

    [Fact]
    public void TheComposerMixesStraightAlphaAndSkipsAClearPixel()
    {
        Assert.Equal(0xFFFF0000u, BehindBlurComposer.Over(0xFFFF0000, 0x00FFFFFF));
        Assert.Equal(0xFF00FF00u, BehindBlurComposer.Over(0xFFFF0000, 0xFF00FF00));
        Assert.Equal(0xFFFF8080u, BehindBlurComposer.Over(0xFFFF0000, 0x80FFFFFF));

        var pixels = new uint[4];
        BehindBlurComposer.Compose(
            pixels,
            new Rectangle(0, 0, 2, 2),
            [
                new BackdropImage(new Rectangle(0, 0, 2, 1), [0xFFFF0000, 0xFF00FF00]),
                new BackdropImage(new Rectangle(1, 0, 1, 2), [0x800000FF, 0xFF0000FF]),
            ]);

        Assert.Equal(0xFFFF0000u, pixels[0]);
        Assert.Equal(0xFF007F80u, pixels[1]);
        Assert.Equal(0u, pixels[2]);
        Assert.Equal(0xFF0000FFu, pixels[3]);
    }

    [Fact]
    public void TheCpuBlurFillsTheDestination()
    {
        var source = new uint[4];
        Array.Fill(source, 0xFF224466u);
        var destination = new uint[4];

        Assert.True(new CpuPictureBlur().TryApply(source, 2, 2, new BlurTuning { Passes = 1, Noise = 0, Vibrancy = 0, Contrast = 1, Brightness = 1, Size = 1 }, destination));
        Assert.NotEqual(0u, destination[0]);
        Assert.False(new CpuPictureBlur().TryApply(source, 2, 2, BlurTuning.Documented, new uint[1]));
    }

    [Fact]
    public void NewOptimizationsAndSpecialAreReadWithoutAWarning()
    {
        var document = ConfigParser.Parse("""
            decoration {
                blur {
                    new_optimizations = false
                    special = true
                }
            }
            """);

        var options = DecorationOptions.From(document);

        Assert.Empty(document.Warnings);
        Assert.False(options.NewOptimizations);
        Assert.True(options.BlurSpecial);
        Assert.True(DecorationOptions.Documented.NewOptimizations);
        Assert.False(DecorationOptions.Documented.BlurSpecial);
    }

    [Fact]
    public void ATiledTranslucentClientUsesTheWallpaperAndAFloatingOneUsesTheWindowsBehind()
    {
        var core = Desk("decoration:active_opacity = 0.8\ndecoration:inactive_opacity = 0.8\nwindowrulev2 = float, class:^(over)$\n");
        core.Handle(Appeared(1));
        var over = core.Handle(new WindowAppeared(new WindowId(2), Area) { Traits = WindowTraits.Normal with { Class = "over" } });

        var tiled = For(over, 1);
        var floating = For(over, 2);
        Assert.NotNull(tiled.Xray);
        Assert.Null(tiled.Behind);
        Assert.False(tiled.Blur);
        Assert.Null(floating.Xray);
        Assert.False(floating.Blur);
        Assert.Contains(new WindowId(1), Assert.IsType<BehindBlur>(floating.Behind).Sources);

        var optimizedOff = For(Desk("decoration:blur:new_optimizations = false\ndecoration:active_opacity = 0.8\n").Handle(Appeared(1)), 1);
        Assert.Null(optimizedOff.Xray);
        Assert.NotNull(optimizedOff.Behind);
        Assert.False(optimizedOff.Blur);

        var off = For(Desk("decoration:blur:enabled = false\ndecoration:active_opacity = 0.8\n").Handle(Appeared(1)), 1);
        Assert.Null(off.Xray);
        Assert.Null(off.Behind);
        Assert.False(off.Blur);
    }

    [Fact]
    public void AnOpenSpecialWorkspaceBlursTheClientsUnderIt()
    {
        var core = Desk("decoration:blur:special = true\ndecoration:active_opacity = 0.8\n");
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 800, 600, 1, true, Area)]));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        var opened = core.Handle(new Dispatch("movetoworkspace", "special:scratch"));

        var layer = Assert.Single(opened.SpecialBlurs);
        Assert.Equal(Area, layer.Area);
        Assert.Equal(new WindowId(2), layer.Anchor);
        Assert.Equal([new WindowId(1)], layer.Blur.Sources);
        Assert.Equal(255, layer.Blur.Alpha);
        Assert.Equal(0, layer.Blur.Rounding);
        Assert.NotNull(For(opened, 2).Behind);
        Assert.Null(For(opened, 2).Xray);

        var closed = core.Handle(new Dispatch("togglespecialworkspace", "scratch"));
        Assert.Empty(closed.SpecialBlurs);
    }

    private static Core Desk(string config)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(config)));
        return core;
    }

    private static WindowAppeared Appeared(long window) => new(new WindowId(window), Area);

    private static Decoration For(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        return Assert.Single(outcome.Decorations, decoration => decoration.Client == client.Id);
    }
}
