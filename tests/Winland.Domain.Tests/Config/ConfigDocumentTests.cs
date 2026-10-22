namespace Winland.Domain.Tests;

public class ConfigDocumentTests
{
    [Fact]
    public void AnOptionReplacesItsValueAndARepeatingKeywordAdds()
    {
        var current = ConfigParser.Parse("general:gaps_in = 4\nmonitor = DP-1, preferred, auto, 1\nbind = SUPER, Q, killactive\n");
        var live = ConfigParser.Parse("general:gaps_in = 8\nmonitor = DP-2, preferred, auto, 1\nbind = SUPER, W, killactive\n");

        var merged = current.Merge(live);

        Assert.Equal(["8"], merged.Settings.Where(setting => setting.Keyword == "general:gaps_in").Select(setting => setting.Value));
        Assert.Equal(2, merged.Settings.Count(setting => setting.Keyword == "monitor"));
        Assert.Equal(current.Binds.Count + live.Binds.Count, merged.Binds.Count);
    }

    [Fact]
    public void ARuleIsAddedAfterTheOnesRead()
    {
        var current = ConfigParser.Parse("windowrulev2 = float, class:^(a)$\n");
        var live = ConfigParser.Parse("windowrulev2 = float, class:^(b)$\n");

        var merged = current.Merge(live);

        Assert.Equal([.. current.WindowRules, .. live.WindowRules], merged.WindowRules);
    }

    [Fact]
    public void AWorkspaceRuleMergesIntoTheRuleForTheSameWorkspace()
    {
        var current = ConfigParser.Parse("workspace = 2, gapsin:4\n");
        var live = ConfigParser.Parse("workspace = 2, gapsout:9\nworkspace = 3, gapsin:1\n");

        var merged = current.Merge(live);

        Assert.Equal(2, merged.WorkspaceRules.Count);
    }

    [Fact]
    public void WhatTheLiveLineDoesNotChangeIsKept()
    {
        var current = ConfigParser.Parse("general:gaps_in = 4\n", path: @"C:\config\winland.conf") with { Sources = [@"C:\config\more.conf"] };

        var merged = current.Merge(ConfigParser.Parse("general:gaps_out = 9\n"));

        Assert.Equal(@"C:\config\winland.conf", merged.Path);
        Assert.Equal([@"C:\config\more.conf"], merged.Sources);
        Assert.Empty(merged.Warnings);
    }

    [Theory]
    [InlineData("exec", false)]
    [InlineData("exec-once", false)]
    [InlineData("source", false)]
    [InlineData("general:gaps_in", true)]
    [InlineData("windowrulev2", true)]
    public void OnlyALineThatLaunchesOrLoadsWaitsForTheConfigToBeRead(string keyword, bool live)
    {
        Assert.Equal(live, ConfigParser.AppliesLive(keyword));
    }
}
