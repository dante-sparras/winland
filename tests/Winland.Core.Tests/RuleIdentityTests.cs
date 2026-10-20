using System.Text.Json;
using Winland;

namespace Winland.Tests;

public class RuleIdentityTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);

    private static readonly Rectangle Area = new(0, 0, 800, 600);

    [Fact]
    public void AWindowRuleLineCanNameTheRuleAndTurnItOff()
    {
        var document = ConfigParser.Parse("windowrulev2 = opacity 0.5, class:^(kitty)$, ruleid:1, ruleon:0\n");

        Assert.Empty(document.Warnings);
        var rule = Assert.Single(document.WindowRules);
        Assert.Equal(WindowRuleEffect.Opacity, rule.Effect);
        Assert.Equal(1, rule.RuleId);
        Assert.False(rule.Active);
        Assert.Equal(WindowRuleEdit.None, rule.Edit);
    }

    [Theory]
    [InlineData("windowrulev2 = drop, ruleid:0")]
    [InlineData("windowrulev2 = ruleon:1")]
    [InlineData("windowrulev2 = drop")]
    [InlineData("windowrulev2 = drop, ruleon:0, ruleid:1")]
    [InlineData("windowrulev2 = float, class:^(kitty)$, ruleid:x")]
    public void ANamedWindowRuleThatIsNotARuleWarns(string line)
    {
        var document = ConfigParser.Parse(line + "\n");

        Assert.Empty(document.WindowRules);
        Assert.Equal(new ConfigWarning(1, "Malformed window rule."), Assert.Single(document.Warnings));
    }

    [Fact]
    public void TurningANamedOpacityRuleOffAndOnRematchesTheWindowsAlreadyOpen()
    {
        var core = Configured("windowrulev2 = opacity 0.5, class:^(kitty)$, ruleid:1, ruleon:1\n");
        core.Handle(Appeared(1, "kitty"));
        core.Handle(Appeared(2, "other"));

        Assert.Equal("0.5", core.Execute("getprop address:0x1 opacity").Text);
        Assert.Equal("1", core.Execute("getprop address:0x2 opacity").Text);

        Assert.Equal("ok", core.Execute("keyword windowrulev2 ruleon:0, ruleid:1").Text);
        Assert.Equal("1", core.Execute("getprop address:0x1 opacity").Text);

        Assert.Equal("ok", core.Execute("keyword windowrulev2 ruleon:1, ruleid:1").Text);
        Assert.Equal("0.5", core.Execute("getprop address:0x1 opacity").Text);
        Assert.Equal("1", core.Execute("getprop address:0x2 opacity").Text);
    }

    [Fact]
    public void AnOrdinaryWindowRuleKeywordDoesNotRematchWindowsAlreadyOpen()
    {
        var core = new Core();
        core.Handle(Appeared(1, "kitty"));

        Assert.Equal("ok", core.Execute("keyword windowrulev2 opacity 0.5, class:^(kitty)$").Text);
        Assert.Equal("1", core.Execute("getprop address:0x1 opacity").Text);

        core.Handle(Appeared(2, "kitty"));
        Assert.Equal("0.5", core.Execute("getprop address:0x2 opacity").Text);
    }

    [Fact]
    public void TurningAFloatRuleOnDoesNotFloatAWindowAlreadyOpen()
    {
        var core = Configured("windowrulev2 = float, class:^(kitty)$, ruleid:1, ruleon:0\n");
        var opened = core.Handle(Appeared(1, "kitty"));
        Assert.False(Client(opened, 1).Floating);

        var enabled = core.Execute("keyword windowrulev2 ruleon:1, ruleid:1").Effect;
        Assert.False(Client(enabled, 1).Floating);

        var next = core.Handle(Appeared(2, "kitty"));
        Assert.True(Client(next, 2).Floating);
        Assert.False(Client(next, 1).Floating);
    }

    [Fact]
    public void DroppingANamedRuleTakesItsEffectOffTheWindowsAlreadyOpen()
    {
        var core = Configured("windowrulev2 = opacity 0.5, class:^(kitty)$, ruleid:1, ruleon:1\n");
        core.Handle(Appeared(1, "kitty"));
        Assert.Equal("0.5", core.Execute("getprop address:0x1 opacity").Text);

        Assert.Equal("ok", core.Execute("keyword windowrulev2 drop, ruleid:1").Text);
        Assert.Equal("1", core.Execute("getprop address:0x1 opacity").Text);

        core.Handle(Appeared(2, "kitty"));
        Assert.Equal("1", core.Execute("getprop address:0x2 opacity").Text);
    }

    [Fact]
    public void TurningANamedSizeRuleOffAndOnChangesTheLimit()
    {
        var core = Configured("windowrulev2 = min_size 400 300, class:^(kitty)$, ruleid:1, ruleon:1\n");
        core.Handle(Appeared(1, "kitty"));
        Assert.Equal("400 300", core.Execute("getprop address:0x1 min_size").Text);

        Assert.Equal("ok", core.Execute("keyword windowrulev2 ruleon:0, ruleid:1").Text);
        Assert.Equal("20 20", core.Execute("getprop address:0x1 min_size").Text);

        Assert.Equal("ok", core.Execute("keyword windowrulev2 ruleon:1, ruleid:1").Text);
        Assert.Equal("400 300", core.Execute("getprop address:0x1 min_size").Text);
    }

    [Fact]
    public void ANamedWorkspaceRuleReplacesTheEarlierOneAndAnUnnamedOneMerges()
    {
        var replaced = ConfigParser.Parse("""
            workspace = 1, gapsout:5, ruleid:1
            workspace = 1, gapsin:3, ruleid:1
            """);
        var rule = Assert.Single(replaced.WorkspaceRules);
        Assert.Equal(1, rule.RuleId);
        Assert.Equal(EdgeInsets.Uniform(3), rule.GapsIn);
        Assert.Null(rule.GapsOut);

        var both = ConfigParser.Parse("""
            workspace = 1, gapsout:5, ruleid:1
            workspace = 1, gapsin:3
            """);
        Assert.Equal(2, both.WorkspaceRules.Count);
        Assert.Equal(1, both.WorkspaceRules[0].RuleId);
        Assert.Equal(0, both.WorkspaceRules[1].RuleId);
        Assert.Null(both.WorkspaceRules[1].GapsOut);

        var merged = ConfigParser.Parse("""
            workspace = 1, gapsout:5
            workspace = 1, gapsin:3
            """);
        var unnamed = Assert.Single(merged.WorkspaceRules);
        Assert.Equal(0, unnamed.RuleId);
        Assert.Equal(EdgeInsets.Uniform(5), unnamed.GapsOut);
        Assert.Equal(EdgeInsets.Uniform(3), unnamed.GapsIn);
    }

    [Fact]
    public void ANamedWorkspaceRuleIsStampedOntoTheWorkspaceAlreadyThere()
    {
        var core = Configured(Flat(""));
        core.Handle(Appeared(1, Wide));

        var inset = core.Execute("keyword workspace 1, gapsout:20, ruleid:1, ruleon:1").Effect;
        Assert.Equal(new Rectangle(20, 20, 1880, 1040), BoundsOf(inset, 1));

        var restored = core.Execute("keyword workspace 1, gapsout:20, ruleid:1, ruleon:0").Effect;
        Assert.Equal(Wide, BoundsOf(restored, 1));
    }

    [Fact]
    public void ADisabledPersistentRuleDoesNotCreateTheWorkspaceUntilItIsOn()
    {
        var core = Configured("workspace = 2, persistent:true, ruleid:1, ruleon:0\n");
        Assert.DoesNotContain(Ids(core), id => id == 2);

        Assert.Equal("ok", core.Execute("keyword workspace 2, persistent:true, ruleid:1, ruleon:1").Text);
        Assert.Contains(2, Ids(core));
    }

    [Fact]
    public void WorkspacerulesReportsWhetherANamedRuleIsOn()
    {
        var core = Configured("""
            workspace = 1, gapsout:5
            workspace = 2, persistent:true, ruleid:1, ruleon:0
            """);

        var rules = Json(core.Execute("workspacerules").Text).EnumerateArray().ToArray();
        Assert.True(rules[0].GetProperty("enabled").GetBoolean());
        Assert.False(rules[1].GetProperty("enabled").GetBoolean());
    }

    private static Core Configured(string text)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(text)));
        return core;
    }

    private static string Flat(string extra) =>
        "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + extra;

    private static WindowAppeared Appeared(long window, string windowClass) => new(new WindowId(window), Area)
    {
        Traits = WindowTraits.Normal with { Class = windowClass },
    };

    private static WindowAppeared Appeared(long window, Rectangle area) => new(new WindowId(window), area);

    private static Client Client(Outcome outcome, long window) =>
        Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));

    private static Rectangle BoundsOf(Outcome outcome, long window)
    {
        var client = Client(outcome, window);
        return Assert.Single(outcome.Placements, placement => placement.Client == client.Id).Bounds;
    }

    private static IEnumerable<int> Ids(Core core)
    {
        foreach (var item in Json(core.Execute("j/workspaces").Text).EnumerateArray())
            yield return item.GetProperty("id").GetInt32();
    }

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
