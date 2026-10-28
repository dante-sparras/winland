namespace Winland.Domain.Tests;

public class WindowRuleParserTests
{
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
