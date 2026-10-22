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

    private static WindowDecision Decide(string config) =>
        WindowRuleParser.Decide(App, ConfigParser.Parse(config).WindowRules);
}
