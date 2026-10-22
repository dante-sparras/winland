namespace Winland.Domain.Tests;

public class ClientPropsTests
{
    private static readonly WindowId Window = new(1);
    private static readonly WindowTraits App = WindowTraits.Normal with { Class = "app" };

    [Fact]
    public void APropIsSetpropsElseTheWindowRulesElseTheWorkspaceRules()
    {
        var props = new ClientProps();
        var workspace = ConfigParser.Parse("workspace = 1, border_size:5, decorate:false\n").WorkspaceRules[0];
        props.Opened(Window, App, [], Decide("windowrulev2 = border_size 3, class:^(app)$\n"));

        Assert.Equal(3L, props.Value(Window, "border_size", workspace));
        Assert.Equal(false, props.Value(Window, "decorate", workspace));
        Assert.Null(props.Value(Window, "decorate", null));

        props.Set(Window, "border_size", 7L);
        Assert.Equal(7L, props.Value(Window, "border_size", workspace));

        props.Set(Window, "border_size", null);
        Assert.Equal(3L, props.Value(Window, "border_size", workspace));
    }

    [Fact]
    public void ARematchReplacesTheRulePropsAndKeepsWhatOnlyAppliesAtOpen()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrulev2 = no_initial_focus, class:^(app)$\nwindowrulev2 = border_size 3, class:^(app)$\n"));

        props.Rematched(Window, Decide("windowrulev2 = no_focus, class:^(app)$\n"), new SortedSet<string>(StringComparer.Ordinal));

        Assert.Null(props.RuleProp(Window, "border_size"));
        Assert.True(props.NoFocus(Window));
        Assert.True(props.FocusRuleOf(Window)?.NoInitialFocus);
    }

    [Fact]
    public void SetpropWinsOverTheFocusRule()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrulev2 = no_focus, class:^(app)$\n"));

        props.Set(Window, "no_focus", false);

        Assert.False(props.NoFocus(Window));
        Assert.Equal(true, props.Ruled(Window, "no_focus", null));
    }

    [Fact]
    public void ATagIsKeptUntilTheRulesAreMatchedAgainAndClearingTakesThemAll()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide(""));

        Assert.True(props.Tag(Window, "work"));
        Assert.Equal(["work"], props.Tags(Window));

        var matched = props.StaticTags(Window);
        matched.Add("ruled*");
        props.Rematched(Window, Decide(""), matched);
        Assert.Equal(["ruled*", "work"], props.Tags(Window));

        props.ClearTags(Window);
        Assert.Empty(props.Tags(Window));
    }

    [Fact]
    public void SetpropsOpacityIsLaidOverTheRules()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrulev2 = opacity 0.5, class:^(app)$\n"));

        props.Set(Window, "opacity_inactive", new PropAlpha(0.25f, false));

        var choice = props.Opacity(Window);
        Assert.Equal(0.5f, choice.Rule?.Active.Value);
        Assert.Equal(0.25f, choice.Rule?.Inactive.Value);
    }

    [Fact]
    public void AForgottenWindowHasNoProps()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrulev2 = border_size 3, class:^(app)$\n"));
        props.SetSizes(Window, default, null, null);

        props.Forget(Window);

        Assert.Null(props.Value(Window, "border_size", null));
        Assert.Null(props.Sizes(Window));
        Assert.Empty(props.Sized);
    }

    [Fact]
    public void SetpropWinsOverAWindowRuleForTheClientsOwnProps()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrulev2 = border_size 3, class:^(app)$\n"));

        props.Set(Window, "border_size", 9L);

        Assert.Equal(9L, props.Own(Window, "border_size"));
        Assert.Equal(3L, props.RuleProp(Window, "border_size"));
    }

    [Theory]
    [InlineData("windowrulev2 = stay_focused, class:^(app)$\n", "stay_focused")]
    [InlineData("windowrulev2 = opaque, class:^(app)$\n", "opaque")]
    [InlineData("windowrulev2 = no_max_size, class:^(app)$\n", "no_max_size")]
    public void ARuleFlagIsTheValueOfItsProp(string rule, string key)
    {
        var props = new ClientProps();
        var decision = Decide(rule);
        props.Opened(Window, App, [], decision);
        props.SetSizes(Window, decision.Sizes, null, null);

        Assert.Equal(true, props.Ruled(Window, key, null));
        Assert.Null(props.Ruled(new WindowId(2), key, null));
    }

    [Fact]
    public void EachOpacityOfARuleIsItsOwnProp()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrulev2 = opacity 0.9 0.6 0.3, class:^(app)$\n"));

        Assert.Equal(0.9f, ((PropAlpha)props.Ruled(Window, "opacity", null)!).Value);
        Assert.Equal(0.6f, ((PropAlpha)props.Ruled(Window, "opacity_inactive", null)!).Value);
        Assert.Equal(0.3f, ((PropAlpha)props.Ruled(Window, "opacity_fullscreen", null)!).Value);
    }

    [Fact]
    public void SetpropWinsOverStayFocusedAndTheSizeRules()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrulev2 = stay_focused, class:^(app)$\n"));
        var rule = new SizeRule(new Size(100, 100), new Size(500, 500), NoMax: false);

        props.Set(Window, "stay_focused", false);
        props.Set(Window, "min_size", new PropSize(200, 150));
        props.Set(Window, "no_max_size", true);

        Assert.False(props.StaysFocused(Window));
        var sizes = props.EffectiveSizes(Window, rule);
        Assert.Equal(new Size(200, 150), sizes.Min);
        Assert.Equal(new Size(500, 500), sizes.Max);
        Assert.True(sizes.NoMax);
        Assert.Equal(rule, props.EffectiveSizes(new WindowId(2), rule));
    }

    [Fact]
    public void AnOpacitySetpropThatChangesNothingLeavesNoRule()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide(""));

        props.Set(Window, "opacity", new PropAlpha(1f, false));
        Assert.Null(props.Opacity(Window).Rule);

        props.Set(Window, "opaque", true);
        Assert.True(props.Opacity(Window).Opaque);
    }

    [Fact]
    public void AFullscreenStateRuleTurnsSyncFullscreenOffAtTheRuleLayer()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrulev2 = border_size 2, class:^(app)$\n") with { StatedFullscreen = true });

        Assert.Equal(false, props.RuleProp(Window, "sync_fullscreen"));
        Assert.Equal(2L, props.RuleProp(Window, "border_size"));

        props.Rematched(Window, Decide("") with { StatedFullscreen = true }, new SortedSet<string>(StringComparer.Ordinal));
        Assert.Equal(false, props.RuleProp(Window, "sync_fullscreen"));

        props.Rematched(Window, Decide(""), new SortedSet<string>(StringComparer.Ordinal));
        Assert.Null(props.RuleProp(Window, "sync_fullscreen"));
    }

    [Fact]
    public void TheMatchedTagsAreTheRulesTagsElseTagwindowsElseNone()
    {
        var props = new ClientProps();
        Assert.Null(props.MatchedTags(Window));

        props.Opened(Window, App, [], Decide(""));
        Assert.Null(props.MatchedTags(Window));

        props.Tag(Window, "static");
        Assert.Equal(["static"], props.MatchedTags(Window)!);

        props.Rematched(Window, Decide(""), new SortedSet<string>(["ruled*"], StringComparer.Ordinal));
        Assert.Equal(["ruled*"], props.MatchedTags(Window)!);
    }

    private static WindowDecision Decide(string config) =>
        WindowRuleParser.Decide(App, ConfigParser.Parse(config).WindowRules);
}
