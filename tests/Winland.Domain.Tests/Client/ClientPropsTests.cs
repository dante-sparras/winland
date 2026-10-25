namespace Winland.Domain.Tests;

public class ClientPropsTests
{
    private static readonly WindowId Window = new(1);
    private static readonly WindowTraits App = WindowTraits.Normal with { Class = "app" };

    [Fact]
    public void APropIsSetpropsElseTheWindowRules()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrule = border_size 3, class:^(app)$\n"));

        Assert.Equal(3L, props.Value(Window, "border_size"));
        Assert.Null(props.Value(Window, "decorate"));

        props.Set(Window, "border_size", 7L);
        Assert.Equal(7L, props.Value(Window, "border_size"));

        props.Set(Window, "border_size", null);
        Assert.Equal(3L, props.Value(Window, "border_size"));
    }

    [Fact]
    public void ARematchReplacesTheRulePropsAndKeepsWhatOnlyAppliesAtOpen()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrule = no_initial_focus, class:^(app)$\nwindowrule = border_size 3, class:^(app)$\n"));

        props.Rematched(Window, Decide("windowrule = no_focus, class:^(app)$\n"));

        Assert.Null(props.RuleProp(Window, "border_size"));
        Assert.True(props.NoFocus(Window));
        Assert.True(props.FocusRuleOf(Window)?.NoInitialFocus);
    }

    [Fact]
    public void SetpropWinsOverTheFocusRule()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrule = no_focus, class:^(app)$\n"));

        props.Set(Window, "no_focus", false);

        Assert.False(props.NoFocus(Window));
        Assert.Equal(true, props.Ruled(Window, "no_focus"));
    }

    [Fact]
    public void SetpropsOpacityIsLaidOverTheRules()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrule = opacity 0.5, class:^(app)$\n"));

        props.Set(Window, "opacity_inactive", new PropAlpha(0.25f));

        var choice = props.Opacity(Window);
        Assert.Equal(0.5f, choice.Rule?.Active);
        Assert.Equal(0.25f, choice.Rule?.Inactive);
    }

    [Fact]
    public void AForgottenWindowHasNoProps()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrule = border_size 3, class:^(app)$\n"));
        props.SetSizes(Window, default, null, null);

        props.Forget(Window);

        Assert.Null(props.Value(Window, "border_size"));
        Assert.Null(props.Sizes(Window));
    }

    [Fact]
    public void SetpropWinsOverAWindowRuleForTheClientsOwnProps()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrule = border_size 3, class:^(app)$\n"));

        props.Set(Window, "border_size", 9L);

        Assert.Equal(9L, props.Own(Window, "border_size"));
        Assert.Equal(3L, props.RuleProp(Window, "border_size"));
    }

    [Fact]
    public void AnOpaqueRuleIsTheValueOfItsProp()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrule = opaque, class:^(app)$\n"));

        Assert.Equal(true, props.Ruled(Window, "opaque"));
        Assert.Null(props.Ruled(new WindowId(2), "opaque"));
    }

    [Fact]
    public void EachOpacityOfARuleIsItsOwnProp()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide("windowrule = opacity 0.9 0.6, class:^(app)$\n"));

        Assert.Equal(0.9f, ((PropAlpha)props.Ruled(Window, "opacity")!).Value);
        Assert.Equal(0.6f, ((PropAlpha)props.Ruled(Window, "opacity_inactive")!).Value);
    }

    [Fact]
    public void SetpropWinsOverTheSizeRules()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide(""));
        var rule = new SizeRule(new Size(100, 100), new Size(500, 500));

        props.Set(Window, "min_size", new PropSize(200, 150));

        var sizes = props.EffectiveSizes(Window, rule);
        Assert.Equal(new Size(200, 150), sizes.Min);
        Assert.Equal(new Size(500, 500), sizes.Max);
        Assert.Equal(rule, props.EffectiveSizes(new WindowId(2), rule));
    }

    [Fact]
    public void AnOpacitySetpropThatChangesNothingLeavesNoRule()
    {
        var props = new ClientProps();
        props.Opened(Window, App, [], Decide(""));

        props.Set(Window, "opacity", new PropAlpha(1f));
        Assert.Null(props.Opacity(Window).Rule);

        props.Set(Window, "opaque", true);
        Assert.True(props.Opacity(Window).Opaque);
    }

    private static WindowDecision Decide(string config) =>
        WindowRuleParser.Decide(App, ConfigParser.Parse(config).WindowRules);
}
