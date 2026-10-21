using Winland;

namespace Winland.Tests;

public class HideTaskbarOptionTests
{
    [Fact]
    public void TheTaskbarStaysVisibleUnlessTheConfigHidesIt()
    {
        var core = new Core();

        Assert.False(core.HideTaskbar);
        Assert.Equal(
            "{\"option\":\"general:hide_taskbar\",\"bool\":false,\"set\":false}",
            core.Execute("getoption general:hide_taskbar").Text);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("on")]
    [InlineData("1")]
    public void HideTaskbarIsHonored(string value)
    {
        var text = $"general:hide_taskbar = {value}\n";
        var document = ConfigParser.Parse(text);
        var core = new Core(document);

        Assert.Empty(document.Warnings);
        Assert.True(core.HideTaskbar);
        Assert.Equal(
            "{\"option\":\"general:hide_taskbar\",\"bool\":true,\"set\":true}",
            core.Execute("getoption general:hide_taskbar").Text);
    }

    [Fact]
    public void KeywordAndReloadApplyTheOption()
    {
        var text = "general:hide_taskbar = false\n";
        var core = new Core(ConfigParser.Parse("general:hide_taskbar = true\n"))
        {
            ReadConfig = () => new ConfigRead(ConfigParser.Parse(text)),
        };

        Assert.Equal("ok", core.Execute("keyword general:hide_taskbar false").Text);
        Assert.False(core.HideTaskbar);
        Assert.Equal("ok", core.Execute("keyword general:hide_taskbar true").Text);
        Assert.True(core.HideTaskbar);

        Assert.Equal("ok", core.Execute("reload").Text);
        Assert.False(core.HideTaskbar);
        Assert.Equal(
            "{\"option\":\"general:hide_taskbar\",\"bool\":false,\"set\":true}",
            core.Execute("getoption general:hide_taskbar").Text);
    }
}
