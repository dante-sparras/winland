using Winland;

namespace Winland.Tests;

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
    public void AGradientReadsBackInThePinnedReleasesForm()
    {
        Assert.True(BorderGradient.TryParse("rgba(33ccffee) rgba(00ff99ee) 45deg", out var gradient));

        Assert.Equal("ee33ccff ee00ff99 45deg", gradient.ToString());
        Assert.Equal("ffabcdef 0deg", WindowProps.ReadGradient("rgb(abcdef)").ToString());
        Assert.Equal("0deg", WindowProps.ReadGradient("-1").ToString());
        // Past one token the pinned release reads from the second on.
        Assert.Equal("ff00ff00 90deg", WindowProps.ReadGradient("rgb(ff0000) rgb(00ff00) 90deg").ToString());
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
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        Assert.Equal("ok", core.Execute("dispatch setprop address:0x1 no_focus 1").Text);
        core.Execute("dispatch movefocus l");

        Assert.Equal("true", core.Execute("getprop address:0x1 no_focus").Text);
        Assert.Contains("\"address\":\"0x2\"", core.Execute("activewindow").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ABorderColorAndRoundingSetByPropAreDrawn()
    {
        var core = new Core(ConfigParser.Parse("decoration:rounding = 0\n"));
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
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        var outcome = core.Execute("dispatch setprop active opacity 0.5").Effect;

        Assert.Equal(127, Assert.Single(outcome.Decorations).Alpha, tolerance: 1);
    }
}
