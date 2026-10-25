namespace Winland.Application.Tests;

public class OpacityTests
{
    private static readonly Rectangle Area = new(0, 0, 800, 600);

    [Fact]
    public void OpacityFractionsClampAndABadValueKeepsThePreviousOne()
    {
        var options = DecorationOptions.From(ConfigParser.Parse("""
            decoration:active_opacity = 0.8
            decoration:active_opacity = 2
            decoration:inactive_opacity = -0.4
            decoration:fullscreen_opacity = 0.9
            decoration:fullscreen_opacity = no
            """));

        Assert.Equal(1f, options.ActiveOpacity);
        Assert.Equal(0f, options.InactiveOpacity);
        Assert.Equal(0.9f, options.FullscreenOpacity);
    }

    [Fact]
    public void OpacityKeysAreHonored()
    {
        var document = ConfigParser.Parse("""
            decoration:active_opacity = 0.8
            decoration:inactive_opacity = 0.4
            decoration:fullscreen_opacity = 0.9
            decoration:blur:enabled = true
            """);

        Assert.Empty(document.Warnings);
        Assert.Equal(4, document.Settings.Count);
    }

    [Fact]
    public void OpacityRuleParsesOneOrTwoFractions()
    {
        var document = ConfigParser.Parse("""
            windowrule = opacity 0.8, class:^(one)$
            windowrule = opacity 0.8 0.6, class:^(two)$
            """);

        Assert.Empty(document.Warnings);
        Assert.Equal(2, document.WindowRules.Count);
        Assert.Equal(new OpacityRule(0.8f, 0.8f, 0.8f), document.WindowRules[0].Opacity);
        Assert.Equal(new OpacityRule(0.8f, 0.6f, 1f), document.WindowRules[1].Opacity);
    }

    [Theory]
    [InlineData("opacity 0.8 0.6 0.9")]
    [InlineData("opacity 0.8 override")]
    [InlineData("opacity nope")]
    [InlineData("opacity 0.8 extra")]
    [InlineData("opaque maybe")]
    public void MalformedOpacityRuleWarns(string effect)
    {
        var document = ConfigParser.Parse($"windowrule = {effect}, class:^(kitty)$\n");

        Assert.Empty(document.WindowRules);
        Assert.Equal(new ConfigWarning(1, "Malformed window rule."), Assert.Single(document.Warnings));
    }

    [Fact]
    public void OpaqueParsesOnAndOff()
    {
        var document = ConfigParser.Parse("""
            windowrule = opaque, class:^(on)$
            windowrule = opaque 0, class:^(off)$
            """);

        Assert.Empty(document.Warnings);
        Assert.Equal(WindowRuleEffect.Opaque, document.WindowRules[0].Effect);
        Assert.True(document.WindowRules[0].Enabled);
        Assert.False(document.WindowRules[1].Enabled);
    }

    [Fact]
    public void ActiveAndInactiveClientsUseTheirFractions()
    {
        var core = CoreWith("""
            decoration:active_opacity = 0.8
            decoration:inactive_opacity = 0.4
            """);

        var first = core.Handle(Appeared(1));
        Assert.Equal(204, Alpha(first, 1));

        var second = core.Handle(Appeared(2));
        Assert.Equal(102, Alpha(second, 1));
        Assert.Equal(204, Alpha(second, 2));

        var focused = core.Handle(new WindowFocused(new WindowId(1)));
        Assert.Equal(204, Alpha(focused, 1));
        Assert.Equal(102, Alpha(focused, 2));
    }

    [Fact]
    public void RuleOpacityMultipliesAndTheLastRuleWins()
    {
        var multiplied = CoreWith("""
            decoration:active_opacity = 0.5
            windowrule = opacity 0.5, class:^(kitty)$
            windowrule = opacity 0.8, class:^(kitty)$
            """);
        var both = multiplied.Handle(Appeared(1, "kitty"));
        Assert.Equal(102, Alpha(both, 1));

        var replaced = multiplied.Handle(new Configure(ConfigParser.Parse("""
            decoration:active_opacity = 0.8
            windowrule = opacity 0.8, class:^(kitty)$
            """)));
        Assert.Equal(163, Alpha(replaced, 1));
    }

    [Fact]
    public void AClosedClientsOpacityRuleDoesNotStickToTheNextWindow()
    {
        var core = CoreWith("""
            decoration:active_opacity = 1
            windowrule = opacity 0.8, class:^(kitty)$
            """);
        var first = core.Handle(Appeared(1, "kitty"));
        Assert.Equal(204, Alpha(first, 1));
        core.Handle(new WindowClosed(new WindowId(1)));
        core.Handle(new Configure(ConfigParser.Parse("decoration:active_opacity = 0.5\n")));

        var appeared = core.Handle(Appeared(1, "kitty"));

        Assert.Equal(128, Alpha(appeared, 1));
    }

    [Fact]
    public void OpaqueForcesTheFractionToOne()
    {
        var core = CoreWith("""
            decoration:inactive_opacity = 0.5
            windowrule = opacity 0.5, class:^(kitty)$
            windowrule = opaque, class:^(kitty)$
            """);
        core.Handle(Appeared(1, "kitty"));
        var outcome = core.Handle(Appeared(2, "other"));

        Assert.Equal(255, Alpha(outcome, 1));
        Assert.Equal(255, Alpha(outcome, 2));
    }

    [Fact]
    public void OpacityDoesNotAdoptAWindowThatStaysUnmanaged()
    {
        var core = CoreWith("windowrule = opacity 0.5, class:.*\n");

        var outcome = core.Handle(new WindowAppeared(new WindowId(9), Area)
        {
            Traits = WindowTraits.Normal with { Taskbar = true },
        });

        Assert.Empty(outcome.State.Clients);
        Assert.Empty(outcome.Decorations);
    }

    [Fact]
    public void FullscreenSelectsItsFraction()
    {
        var options = DecorationOptions.Documented with
        {
            ActiveOpacity = 0.8f,
            InactiveOpacity = 0.4f,
            FullscreenOpacity = 0.9f,
        };

        Assert.Equal(230, DecorationAlpha.Compute(options, new OpacityInput(Active: true, Fullscreen: true), null, false));
        Assert.Equal(230, DecorationAlpha.Compute(options, new OpacityInput(Active: false, Fullscreen: true), null, false));
        Assert.Equal(204, DecorationAlpha.Compute(options, new OpacityInput(Active: true), null, false));
    }

    [Fact]
    public void OneOpacityValueIsCopiedBeforeItMultipliesFullscreen()
    {
        var rule = new OpacityRule(0.5f, 0.5f, 0.5f);
        var options = DecorationOptions.Documented with { FullscreenOpacity = 0.5f };

        Assert.Equal(64, DecorationAlpha.Compute(options, new OpacityInput(Active: true, Fullscreen: true), rule, false));
    }

    [Fact]
    public void LayeredFlagRoundTripsAndAnOlderSnapshotIsNotLayered()
    {
        var snapshot = new RecoverySnapshot(
        [
            new RecoveredClient(new WindowId(5), WorkspaceId.Initial, MonitorId.Primary, false, "Notepad", "Notes", 10, true),
        ]);

        Assert.Contains("\"layered\":true", snapshot.ToJson(), StringComparison.Ordinal);
        Assert.True(RecoverySnapshot.TryParse(snapshot.ToJson(), out var parsed));
        Assert.True(Assert.Single(parsed.Clients).Layered);

        var older = """{"clients":[{"window":5,"workspace":"1","monitor":"\\\\.\\DISPLAY1","hidden":false,"class":"Notepad","title":"Notes","process":10}]}""";
        Assert.True(RecoverySnapshot.TryParse(older, out var legacy));
        Assert.False(Assert.Single(legacy.Clients).Layered);
    }


    private static WindowManager CoreWith(string text)
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(text)));
        return core;
    }

    private static WindowAppeared Appeared(long window, string? windowClass = null) => new(new WindowId(window), Area)
    {
        Traits = windowClass is null ? WindowTraits.Normal : WindowTraits.Normal with { Class = windowClass },
    };

    private static byte Alpha(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        return Assert.Single(outcome.Decorations, decoration => decoration.Client == client.Id).Alpha;
    }
}
