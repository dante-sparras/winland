using Winland;

namespace Winland.Tests;

public class FontNamesTests
{
    [Theory]
    [InlineData("Sans", "Segoe UI")]
    [InlineData("sans-serif", "Segoe UI")]
    [InlineData("", "Segoe UI")]
    [InlineData(null, "Segoe UI")]
    [InlineData("Monospace", "Consolas")]
    [InlineData("serif", "Cambria")]
    [InlineData(" Fira Code ", "Fira Code")]
    public void GenericNamesMapToWindowsFonts(string? family, string font) =>
        Assert.Equal(font, FontNames.Windows(family));

    [Fact]
    public void AnUnnamedTabBarUsesTheGlobalFont()
    {
        var core = new Core(ConfigParser.Parse("misc:font_family = Monospace\n"));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1040))]));
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));
        core.Execute("dispatch togglegroup");

        Assert.Equal("Consolas", Assert.Single(core.Execute("dispatch no_op").Effect.GroupBars).Style.FontFamily);

        core.Execute("keyword group:groupbar:font_family Fira Code");
        Assert.Equal("Fira Code", Assert.Single(core.Execute("dispatch no_op").Effect.GroupBars).Style.FontFamily);
    }
}
