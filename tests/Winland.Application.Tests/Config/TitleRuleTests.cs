namespace Winland.Application.Tests;

public class TitleRuleTests
{
    [Theory]
    [InlineData("windowrule = float, title:Settings", true)]
    [InlineData("windowrule = float, class:^(kitty)$, title:negative:Settings", true)]
    [InlineData("windowrule = float, initialTitle:Settings", false)]
    [InlineData("windowrule = float, class:^(kitty)$", false)]
    [InlineData("general:gaps_in = 0", false)]
    public void OnlyARuleOnTitleReadsTitles(string line, bool expected)
    {
        var core = new WindowManager(ConfigParser.Parse(line + "\n"));

        Assert.Equal(expected, core.RulesMatchTitles);
    }

    [Fact]
    public void ReloadingTheConfigChangesWhatTheRulesReadAndTheirRevision()
    {
        var core = new WindowManager();
        Assert.False(core.RulesMatchTitles);
        var before = core.RuleRevision;

        core.Handle(new Configure(ConfigParser.Parse("windowrule = ignore, title:Player\n")));
        var reloaded = core.RuleRevision;

        Assert.True(core.RulesMatchTitles);
        Assert.NotEqual(before, reloaded);

        core.Handle(new Configure(ConfigParser.Parse("windowrule = ignore, class:^(kitty)$\n")));

        Assert.False(core.RulesMatchTitles);
        Assert.NotEqual(reloaded, core.RuleRevision);
    }
}
