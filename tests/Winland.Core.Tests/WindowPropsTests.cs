using Winland.Core;

namespace Winland.Core.Tests;

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
        core.Handle(new Configure(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 2\nwindowrulev2 = border_size 0, class:^(bare)$\n")));
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
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = border_color rgb(ff0000) rgb(00ff00), class:.*\nwindowrulev2 = rounding 8, class:.*\n"));
        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area));

        var decoration = Assert.Single(outcome.Decorations);
        Assert.Equal(new BorderColor(0xFF, 0xFF, 0x00, 0x00), decoration.Border);
        Assert.Equal(CornerPreference.Round, decoration.Corners);
        Assert.Equal("ff00ff00", core.Execute("getprop active inactive_border_color").Text);
        Assert.Equal("8", core.Execute("getprop active rounding").Text);
    }

    [Theory]
    [InlineData("windowrulev2 = border_size wide, class:.*", "border_size rule \"wide\" failed with: bad input")]
    [InlineData("windowrulev2 = border_color nothing, class:.*", "border_color rule \"nothing\" has invalid color \"nothing\"")]
    public void AMalformedPropRuleWarns(string line, string warning)
    {
        var document = ConfigParser.Parse(line + "\n");

        Assert.Empty(document.WindowRules);
        Assert.Equal(new ConfigWarning(1, warning), Assert.Single(document.Warnings));
    }

    [Fact]
    public void ABoolPropRuleIsReported()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = no_blur, class:.*\nwindowrulev2 = decorate 0, class:.*\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        Assert.Equal("true", core.Execute("getprop active no_blur").Text);
        Assert.Equal("false", core.Execute("getprop active decorate").Text);
    }

    [Fact]
    public void AStateMatchFollowsTheWindowAsItsWorkspaceFills()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 2\nwindowrulev2 = border_size 0, float:0, workspace:w[tv1]\n")));
        var alone = core.Handle(new WindowAppeared(new WindowId(1), Area));
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), Assert.Single(alone.Placements).Bounds);
        Assert.Equal("0", core.Execute("getprop address:0x1 border_size").Text);

        var shared = core.Handle(new WindowAppeared(new WindowId(2), Area));
        Assert.Equal(new Rectangle(2, 2, 956, 1076), Assert.Single(shared.Placements, placement => placement.Client == new ClientId(1)).Bounds);
        Assert.Equal("2", core.Execute("getprop address:0x1 border_size").Text);

        core.Handle(new WindowClosed(new WindowId(2)));
        Assert.Equal("0", core.Execute("getprop address:0x1 border_size").Text);
    }

    [Fact]
    public void AFocusMatchFollowsTheFocus()
    {
        var core = new WindowManager(ConfigParser.Parse("decoration:rounding = 0\nwindowrulev2 = rounding 8, focus:1\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        Assert.Equal("8", core.Execute("getprop address:0x2 rounding").Text);
        Assert.NotEqual("8", core.Execute("getprop address:0x1 rounding").Text);

        core.Execute("dispatch focuswindow address:0x1");
        Assert.Equal("8", core.Execute("getprop address:0x1 rounding").Text);
        Assert.NotEqual("8", core.Execute("getprop address:0x2 rounding").Text);
    }

    [Fact]
    public void AFocusedBorderColorRuleWithOneColorColorsTheInactiveBorder()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = border_color rgb(ff0000), focus:0\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        Assert.Equal("ffff0000", core.Execute("getprop address:0x1 inactive_border_color").Text);
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

    [Fact]
    public void TagRequestsSetClearAndToggleAsThePinnedReleaseDoes()
    {
        var tags = new SortedSet<string>(StringComparer.Ordinal);

        Assert.True(WindowTags.Apply(tags, "+work", dynamic: false));
        Assert.False(WindowTags.Apply(tags, "+work", dynamic: false));
        Assert.True(WindowTags.Apply(tags, "work", dynamic: true));
        Assert.Equal(["work", "work*"], tags);
        Assert.True(WindowTags.IsTagged(tags, "work"));
        Assert.False(WindowTags.IsTagged(tags, "negative:work"));

        Assert.True(WindowTags.Apply(tags, "work", dynamic: false));
        Assert.True(WindowTags.Apply(tags, "-work*", dynamic: false));
        Assert.Empty(tags);
    }

    [Fact]
    public void ATagRuleFollowsTheStateItMatches()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = tag +big, fullscreen:1\nwindowrulev2 = rounding 8, tag:big\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Execute("dispatch tagwindow +mine");

        core.Execute("dispatch fullscreen 0");
        Assert.Contains("\"big*\"", core.Execute("j/clients").Text, StringComparison.Ordinal);
        Assert.Equal("8", core.Execute("getprop active rounding").Text);
        Assert.Equal(new WindowId(1), Find(core, "tag:mi.e"));

        core.Execute("dispatch fullscreen 0");
        Assert.DoesNotContain("big", core.Execute("j/clients").Text, StringComparison.Ordinal);
        Assert.Contains("\"mine\"", core.Execute("j/clients").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearTagsTakesTheTagsOffAndTheRulesGiveTheirsBack()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = tag +ruled, class:.*\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Execute("dispatch tagwindow +mine");
        core.Execute("dispatch tagwindow +other address:0x1");
        Assert.Contains("\"mine\"", core.Execute("j/clients").Text, StringComparison.Ordinal);

        Assert.Equal("ok", core.Execute("dispatch cleartags address:0x1").Text);

        var clients = core.Execute("j/clients").Text;
        Assert.DoesNotContain("mine", clients, StringComparison.Ordinal);
        Assert.DoesNotContain("other", clients, StringComparison.Ordinal);
        Assert.Contains("\"ruled*\"", clients, StringComparison.Ordinal);
    }

    private static WindowId? Find(WindowManager core, string selector) =>
        core.Execute($"dispatch focuswindow {selector}").Text == "ok" ? new WindowId(1) : null;

    [Theory]
    [InlineData("game", 3, "game")]
    [InlineData("2", 2, "video")]
    [InlineData("7", 0, "none")]
    [InlineData("cinema", 0, "none")]
    public void AContentTypeReadsANumberOrAName(string text, int type, string name)
    {
        Assert.Equal(type, ContentTypes.FromString(text));
        Assert.Equal(name, ContentTypes.Name(type));
    }

    [Fact]
    public void AContentRuleIsMatchedByNameAndNumber()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = content photo, class:.*\nwindowrulev2 = opacity 0.5, content:1\nwindowrulev2 = rounding 3, content:photo\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));

        Assert.Contains("\"contentType\":\"photo\"", core.Execute("j/clients").Text, StringComparison.Ordinal);
        Assert.Equal("0.5", core.Execute("getprop active opacity").Text);
        Assert.Equal("3", core.Execute("getprop active rounding").Text);
    }

    [Fact]
    public void ARuleOnATagAnotherRuleGivesAppliesWhenTheWindowOpens()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = float, tag:dialog\nwindowrulev2 = tag +dialog, class:^(picker)$\n"));

        var outcome = core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Class = "picker" } });

        Assert.True(Assert.Single(outcome.State.Clients).Floating);
    }

    [Fact]
    public void ARuleOnTheWorkspaceAWindowOpensOnAppliesWhenItOpens()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = float, workspace:2\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Execute("dispatch workspace 2");

        var outcome = core.Handle(new WindowAppeared(new WindowId(2), Area));

        Assert.False(Assert.Single(outcome.State.Clients, client => client.Window == new WindowId(1)).Floating);
        Assert.True(Assert.Single(outcome.State.Clients, client => client.Window == new WindowId(2)).Floating);
    }
}
