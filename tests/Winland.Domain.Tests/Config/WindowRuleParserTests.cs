namespace Winland.Domain.Tests;

public class WindowRuleParserTests
{
    [Theory]
    [InlineData("float")]
    [InlineData("tile")]
    [InlineData("ignore")]
    public void AdoptionEffectsAcceptAnExplicitBoolean(string effect)
    {
        Assert.True(WindowRuleParser.TryParse($"{effect} on, class:kitty", out var enabled, out _));
        Assert.True(enabled!.Enabled);
        Assert.True(WindowRuleParser.TryParse($"{effect} off, class:kitty", out var disabled, out _));
        Assert.False(disabled!.Enabled);
        Assert.Equal(
            WindowRuleParser.Decide(WindowTraits.Normal with { Class = "kitty" }, []),
            WindowRuleParser.Decide(WindowTraits.Normal with { Class = "kitty" }, [disabled]));
    }

    [Theory]
    [InlineData("match:class kitty, float on")]
    [InlineData("float on, match:class kitty")]
    public void MatchPrefixRulesWarnWithTheWinlandSyntax(string value)
    {
        var document = ConfigParser.Parse($"windowrule = {value}\n");

        Assert.Contains("windowrule = float on, class:kitty", Assert.Single(document.Warnings).Message);
        Assert.Empty(document.WindowRules);
    }

    [Fact]
    public void Windowrulev2IsAnAliasForWindowrule()
    {
        var document = ConfigParser.Parse("windowrulev2 = float on, class:kitty\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(WindowRuleEffect.Float, Assert.Single(document.WindowRules).Effect);
        Assert.Equal("windowrule", Assert.Single(document.Settings).Keyword);
    }

    [Theory]
    [InlineData("notepad.exe", "Notepad.exe", true)]
    [InlineData("negative:notepad.exe", "Notepad.exe", false)]
    [InlineData("negative:notepad.exe", "other.exe", true)]
    public void ExePatternsIgnoreCase(string pattern, string exe, bool floating)
    {
        var document = ConfigParser.Parse($"windowrule = float, exe:{pattern}\n");

        Assert.Equal(floating, WindowRuleParser.Decide(WindowTraits.Normal with { Exe = exe }, document.WindowRules).Floating);
    }

    [Fact]
    public void ACommaInsideAPatternStaysInThePattern()
    {
        Assert.True(WindowRuleParser.TryParse("float, title:a{1,2}, class:kitty", out var rule, out _));

        Assert.Equal("a{1,2}", rule!.TitlePattern);
        Assert.Equal("kitty", rule.ClassPattern);
    }

    [Fact]
    public void ATrailingOrDoubledCommaIsDropped()
    {
        Assert.True(WindowRuleParser.TryParse("float, class:foo,, title:bar,", out var rule, out _));

        Assert.Equal("foo", rule!.ClassPattern);
        Assert.Equal("bar", rule.TitlePattern);
    }

    [Fact]
    public void APatternThatClosesAGroupItNeverOpenedIsRejected()
    {
        Assert.False(WindowRuleParser.TryParse("float, class:a)|(b", out _, out var error));
        Assert.Equal("Invalid window rule pattern.", error);
    }

    [Theory]
    [InlineData("no_blur banana, class:x")]
    [InlineData("opacity, class:x")]
    public void AnArgumentThatDoesNotReadDropsTheRule(string text)
    {
        Assert.False(WindowRuleParser.TryParse(text, out _, out var error));
        Assert.Equal("Malformed window rule.", error);
    }
}
