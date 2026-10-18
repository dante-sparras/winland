using Winland;

namespace Winland.Tests;

public class AnimationConfigTests
{
    [Fact]
    public void LinearEasesInStep()
    {
        foreach (var x in new[] { 0.1, 0.25, 0.5, 0.9 })
            Assert.Equal(x, Bezier.Linear.Ease(x), 6);
    }

    [Fact]
    public void ACurveStartsAtZeroAndLandsOnOne()
    {
        var curve = new Bezier(0.05, 0.9, 0.1, 1.05);

        Assert.Equal(0, curve.Ease(0));
        Assert.Equal(0, curve.Ease(-1));
        Assert.Equal(1, curve.Ease(1));
        Assert.Equal(1, curve.Ease(3));
    }

    [Fact]
    public void ACurveFollowsItsControlPoints()
    {
        // cubic-bezier(0.25, 0.1, 0.25, 1), the CSS "ease" curve, is 0.8024 halfway.
        var ease = new Bezier(0.25, 0.1, 0.25, 1);

        Assert.Equal(0.8024, ease.Ease(0.5), 3);
        Assert.Equal(0.0, Bezier.Default.Ease(0), 6);
        Assert.True(Bezier.Default.Ease(0.1) > 0.5);
    }

    [Fact]
    public void ACurveRisesSteadilyWhenItsPointsStayInside()
    {
        var previous = 0.0;
        for (var i = 1; i <= 100; i++)
        {
            var value = Bezier.Default.Ease(i / 100.0);
            Assert.True(value >= previous);
            previous = value;
        }
    }

    [Fact]
    public void AnOvershootingCurvePassesOneBeforeItLands()
    {
        var overshoot = new Bezier(0.5, 0.9, 0.1, 1.1);
        var highest = Enumerable.Range(1, 99).Max(i => overshoot.Ease(i / 100.0));

        Assert.True(highest > 1);
        Assert.Equal(1, overshoot.Ease(1));
    }

    [Fact]
    public void AnimationLinesAreKeptWithoutWarnings()
    {
        var document = ConfigParser.Parse("""
            animations {
                enabled = yes
                bezier = myBezier, 0.05, 0.9, 0.1, 1.05
                animation = windows, 1, 7, myBezier
                animation = windowsOut, 1, 7, default, popin 80%
                animation = border, 1, 10, default
                animation = fade, 1, 7, default
                animation = workspaces, 1, 6, default, slidefade 20%
            }
            """);

        Assert.Empty(document.Warnings);
        var options = AnimationOptions.From(document);
        Assert.True(options.Enabled);
        Assert.Equal(new Timing(new Bezier(0.05, 0.9, 0.1, 1.05), 700), options.TimingOf("windowsIn"));
        Assert.Equal("popin 80%", options.StyleOf("windowsOut"));
        Assert.Equal("slidefade 20%", options.StyleOf("workspacesIn"));
    }

    [Fact]
    public void AnUnsetNodeTakesItsNearestSetParent()
    {
        var options = AnimationOptions.From(ConfigParser.Parse("""
            animation = global, 1, 5, linear
            animation = windows, 1, 3, default, slide
            animation = windowsMove, 0
            """));

        Assert.Equal(new AnimationLeaf(true, 3, "default", "slide"), options.Leaf("windowsIn"));
        Assert.Equal(new AnimationLeaf(true, 5, "linear", ""), options.Leaf("fadeSwitch"));
        Assert.Equal(new AnimationLeaf(true, 5, "linear", ""), options.Leaf("specialWorkspaceIn"));
        Assert.Null(options.TimingOf("windowsMove"));
        Assert.Equal(new Timing(Bezier.Linear, 500), options.TimingOf("border"));
    }

    [Fact]
    public void TheDefaultTreeAnimatesEveryNodeForEightTenths()
    {
        var options = AnimationOptions.Documented;

        Assert.True(options.Enabled);
        Assert.False(options.Wraparound);
        Assert.Equal(new Timing(Bezier.Default, 800), options.TimingOf("windowsIn"));
        Assert.Equal(new Timing(Bezier.Default, 800), options.TimingOf("workspacesOut"));
        Assert.Null(options.TimingOf("borderangle"));
    }

    [Fact]
    public void AnimationsOffStopsEveryNode()
    {
        var options = AnimationOptions.From(ConfigParser.Parse("""
            animations:enabled = false
            animations:workspace_wraparound = true
            animation = windows, 1, 3, default
            """));

        Assert.False(options.Enabled);
        Assert.True(options.Wraparound);
        Assert.Null(options.TimingOf("windows"));
    }

    [Fact]
    public void BadAnimationLinesWarnWithTheirLineNumber()
    {
        var document = ConfigParser.Parse("""
            animation = windows, 1, 3, later
            bezier = later, 0, 0, 1, 1
            animation = wobble, 1, 3, default
            animation = windows, 1, 0, default
            animation = windows, 1, fast, default
            animation = workspaces, 1, 3, default, popin
            animation = fade, 1, 3, default, slide
            bezier = short, 0, 0, 1
            bezier = word, 0, a, 1, 1
            bezier = long, 0, 0, 1, 1, 2
            animation = layers, 1, 3, default
            animation = zoomFactor, 1, 3, default
            """);

        Assert.Equal(
            [
                new ConfigWarning(1, "Curve 'later' does not exist."),
                new ConfigWarning(3, "Animation 'wobble' does not exist."),
                new ConfigWarning(4, "Animation speed '0' is not valid."),
                new ConfigWarning(5, "Animation speed 'fast' is not valid."),
                new ConfigWarning(6, "Style 'popin' is not valid for animation 'workspaces'."),
                new ConfigWarning(7, "Animation 'fade' has no styles."),
                new ConfigWarning(8, "Bezier needs four points."),
                new ConfigWarning(9, "Bezier point 'a' is not a number."),
                new ConfigWarning(10, "Bezier has too many arguments."),
                new ConfigWarning(11, "Layer shell surfaces have no Windows meaning."),
                new ConfigWarning(12, "The cursor zoom changes factor at once and does not animate."),
            ],
            document.Warnings);
    }

    [Fact]
    public void AStyleThatWarnsIsStillKept()
    {
        var options = AnimationOptions.From(ConfigParser.Parse("animation = global, 1, 4, default, slide"));

        Assert.Equal("slide", options.StyleOf("windowsIn"));
    }

    [Fact]
    public void TheAnimationsRequestListsTheTreeAndTheCurves()
    {
        var core = new Core(ConfigParser.Parse("bezier = quick, 0.15, 0, 0.1, 1\nanimation = windows, 1, 3, quick, popin\n"));

        var text = core.Execute("animations").Text;
        Assert.StartsWith("animations:", text, StringComparison.Ordinal);
        Assert.Contains("name: windows\n\t\toverridden: 1\n\t\tbezier: quick\n\t\tenabled: true\n\t\tspeed: 3.00\n\t\tstyle: popin", text, StringComparison.Ordinal);

        using var json = System.Text.Json.JsonDocument.Parse(core.Execute("j/animations").Text);
        Assert.Contains(json.RootElement[1].EnumerateArray(), curve => curve.GetProperty("name").GetString() == "quick" && curve.GetProperty("X0").GetDouble() == 0.15);
    }
}
