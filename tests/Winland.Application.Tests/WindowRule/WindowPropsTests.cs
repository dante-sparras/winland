namespace Winland.Application.Tests;

public class WindowPropsTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("Yes please", true)]
    [InlineData("on", true)]
    [InlineData("0", false)]
    [InlineData("off", false)]
    [InlineData("enabled", false)]
    public void TruthyReadsAsThePinnedReleaseDoes(string text, bool expected) =>
        Assert.Equal(expected, WindowProps.Truthy(text));

    [Fact]
    public void ABorderColorIsOneColorAndAGradientChangesNothing()
    {
        Assert.True(WindowProps.TryReadColor("rgb(abcdef)", out var color));
        Assert.Equal(new BorderColor(0xFF, 0xAB, 0xCD, 0xEF), color);
        Assert.False(WindowProps.TryReadColor("rgba(33ccffee) rgba(00ff99ee) 45deg", out _));
        Assert.False(WindowProps.TryReadColor("rgb(ff0000) 90deg", out _));
        Assert.False(WindowProps.TryReadColor("-1", out _));
        Assert.False(WindowProps.TryReadColor("rgb(ff0000) rgb(00ff00)", out _));
    }

    [Fact]
    public void ToggleFlipsWhatALayerHoldsOrFalse()
    {
        Assert.True(WindowProps.TryTrivial("decorate", PropKind.Bool, "toggle", null, true, out var unsetToggle));
        Assert.Equal(true, unsetToggle);

        Assert.True(WindowProps.TryTrivial("no_focus", PropKind.Bool, "toggle", true, true, out var heldToggle));
        Assert.Equal(false, heldToggle);
    }

    [Fact]
    public void RelativeAddsAndBorderSizeStaysAtOrAboveZero()
    {
        Assert.True(WindowProps.TryTrivial("rounding", PropKind.Int, "relative 4", null, 10L, out var grown));
        Assert.Equal(14L, grown);

        Assert.True(WindowProps.TryTrivial("border_size", PropKind.Int, "relative -9", null, 2L, out var clamped));
        Assert.Equal(0L, clamped);

        Assert.False(WindowProps.TryTrivial("rounding", PropKind.Int, "wide", null, 10L, out _));
    }

    [Fact]
    public void NoFocusSetByPropKeepsAWindowFromTakingFocus()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        Assert.Equal("ok", core.Execute("dispatch setprop address:0x1 no_focus 1").Text);
        core.Execute("dispatch movefocus l");

        Assert.Equal("true", core.Execute("getprop address:0x1 no_focus").Text);
        Assert.Contains("\"address\":\"0x2\"", core.Execute("j/activewindow").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ABorderColorAndRoundingSetByPropAreDrawn()
    {
        var core = new WindowManager(ConfigParser.Parse("decoration:rounding = 0\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        core.Execute("dispatch setprop active active_border_color rgb(ff0000)");
        var outcome = core.Execute("dispatch setprop active rounding 8").Effect;

        var decoration = Assert.Single(outcome.Decorations);
        Assert.Equal(new BorderColor(0xFF, 0xFF, 0x00, 0x00), decoration.Border);
        Assert.Equal(CornerPreference.Round, decoration.Corners);
    }

    [Fact]
    public void AnOpacitySetByPropIsDrawn()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Execute("dispatch setprop active opacity 0.5").Effect;

        Assert.Equal(127, Assert.Single(outcome.Decorations).Alpha, tolerance: 1);
    }

    [Fact]
    public void ABorderSizeRuleShrinksOnlyItsWindowsTile()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 2\nwindowrule = border_size 0, class:^(bare)$\n")));
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Class = "bare" } });
        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Area) { Traits = WindowTraits.Normal with { Class = "framed" } });

        Assert.Equal(new Rectangle(0, 0, 960, 1080), Assert.Single(outcome.Placements, placement => placement.Client == new ClientId(1)).Bounds);
        Assert.Equal(new Rectangle(962, 2, 956, 1076), Assert.Single(outcome.Placements, placement => placement.Client == new ClientId(2)).Bounds);
        Assert.Equal("0", core.Execute("getprop address:0x1 border_size").Text);
        Assert.Equal("2", core.Execute("getprop address:0x2 border_size").Text);
    }

    [Fact]
    public void SetpropBorderSizeLaysTheWorkspaceOutAgain()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 2\n")));
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Execute("dispatch setprop active border_size 10").Effect;

        Assert.Equal(new Rectangle(10, 10, 1900, 1060), Assert.Single(outcome.Placements).Bounds);
    }

    [Fact]
    public void BorderColorAndRoundingRulesAreDrawnAndReported()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = border_color rgb(ff0000) rgb(00ff00), class:.*\nwindowrule = rounding 8, class:.*\n"));
        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area));

        var decoration = Assert.Single(outcome.Decorations);
        Assert.Equal(new BorderColor(0xFF, 0xFF, 0x00, 0x00), decoration.Border);
        Assert.Equal(CornerPreference.Round, decoration.Corners);
        Assert.Equal("ff00ff00", core.Execute("getprop active inactive_border_color").Text);
        Assert.Equal("8", core.Execute("getprop active rounding").Text);
    }

    [Theory]
    [InlineData("windowrule = border_size wide, class:.*", "border_size rule \"wide\" failed with: bad input")]
    [InlineData("windowrule = border_color nothing, class:.*", "border_color rule \"nothing\" has invalid color \"nothing\"")]
    public void AMalformedPropRuleWarns(string line, string warning)
    {
        var document = ConfigParser.Parse(line + "\n");

        Assert.Empty(document.WindowRules);
        Assert.Equal(new ConfigWarning(1, warning), Assert.Single(document.Warnings));
    }

    [Fact]
    public void ABoolPropRuleIsReported()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = no_blur, class:.*\nwindowrule = decorate 0, class:.*\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        Assert.Equal("true", core.Execute("getprop active no_blur").Text);
        Assert.Equal("false", core.Execute("getprop active decorate").Text);
    }

    [Fact]
    public void AGradientSetByPropChangesNothing()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        var before = core.Execute("getprop active active_border_color").Text;

        Assert.Equal("ok", core.Execute("dispatch setprop active active_border_color rgba(33ccffee) rgba(00ff99ee) 45deg").Text);
        Assert.Equal(before, core.Execute("getprop active active_border_color").Text);
    }

}
