using Winland;

namespace Winland.Tests;

public class EffectPropTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void TheXrayPropertyWinsOverTheConfigEitherWay()
    {
        var core = Desk("decoration:active_opacity = 0.8\n", 1);

        core.Execute("setprop address:0x1 xray 1");
        Assert.NotNull(Single(core).Xray);

        var on = Desk("decoration:blur:xray = true\ndecoration:blur:new_optimizations = false\ndecoration:active_opacity = 0.8\n", 1);
        on.Execute("setprop address:0x1 xray 0");
        var decoration = Single(on);
        Assert.Null(decoration.Xray);
        Assert.NotNull(decoration.Behind);
        Assert.False(decoration.Blur);
    }

    [Fact]
    public void TheXrayPropertyNeedsBlurOn()
    {
        var core = Desk("decoration:blur:enabled = false\ndecoration:active_opacity = 0.8\n", 1);

        core.Execute("setprop address:0x1 xray 1");

        var decoration = Single(core);
        Assert.False(decoration.Blur);
        Assert.Null(decoration.Xray);
        Assert.Null(decoration.Behind);
    }

    [Fact]
    public void NoBlurTakesBothBackdropsOff()
    {
        var core = Desk("decoration:blur:xray = true\ndecoration:active_opacity = 0.8\n", 1);

        core.Execute("setprop address:0x1 no_blur 1");

        var decoration = Single(core);
        Assert.False(decoration.Blur);
        Assert.Null(decoration.Xray);
        Assert.Null(decoration.Behind);
    }

    [Fact]
    public void NoDimDropsTheInactiveDimOnly()
    {
        var core = Desk("decoration:dim_inactive = true\ndecoration:dim_strength = 0.5\ndecoration:dim_modal = false\n", 1, 2);
        Assert.Equal(128, Of(core, 1).Alpha);

        core.Execute("setprop address:0x1 no_dim 1");

        Assert.Equal(255, Of(core, 1).Alpha);
        Assert.Equal(255, DecorationAlpha.Compute(
            DecorationOptions.Documented with { DimInactive = true, DimStrength = 0.5f },
            new OpacityInput(Active: false, NoDim: true),
            null,
            false));
        Assert.Equal(128, DecorationAlpha.Compute(
            DecorationOptions.Documented with { DimInactive = true, DimStrength = 0.5f, DimModal = true },
            new OpacityInput(Active: false, ShadowedByModal: true, NoDim: true),
            null,
            false));
    }

    [Fact]
    public void AWindowWithNoAnimMovesAtOnceWhileTheOthersAnimate()
    {
        var core = Desk("", 1);
        core.Execute("setprop address:0x1 no_anim 1");

        var opened = core.Handle(new WindowAppeared(new WindowId(2), Area));

        Assert.Contains(opened.Placements, placement => placement.Client == ClientOf(opened, 1));
        Assert.DoesNotContain(opened.Motions, motion => motion.Window == new WindowId(1));
        Assert.Contains(opened.Motions, motion => motion.Window == new WindowId(2));
    }

    [Fact]
    public void AWindowWithNoAnimDrawsItsDecorationsAtRest()
    {
        var core = Desk("""
            general:col.active_border = rgba(ff0000ff) rgba(00ff00ff) 45deg
            animation = borderangle, 1, 10, default, loop
            decoration:shadow:enabled = true
            decoration:glow:enabled = true
            decoration:wobble:enabled = true
            """, 1);
        Assert.NotNull(Single(core).Ring?.Spin);

        core.Execute("setprop address:0x1 no_anim 1");

        var decoration = Single(core);
        Assert.Null(decoration.Ring?.Spin);
        Assert.Null(decoration.DrawnShadow?.Fade);
        Assert.Null(decoration.Glow?.Fade);
        Assert.Null(decoration.Motion);
    }

    private static Core Desk(string config, params long[] windows)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(config)));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));
        foreach (var window in windows)
            core.Handle(new WindowAppeared(new WindowId(window), Area));
        return core;
    }

    private static Decoration Single(Core core) => Assert.Single(core.Execute("dispatch no_op").Effect.Decorations);

    private static Decoration Of(Core core, long window)
    {
        var outcome = core.Execute("dispatch no_op").Effect;
        return Assert.Single(outcome.Decorations, decoration => decoration.Client == ClientOf(outcome, window));
    }

    private static ClientId ClientOf(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, client => client.Window == new WindowId(window)).Id;
}
