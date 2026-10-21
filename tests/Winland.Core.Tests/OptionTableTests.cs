namespace Winland.Tests;

public sealed class OptionTableTests
{
    [Fact]
    public void AValueAnOptionCannotReadWarnsAndTheEarlierValueStays()
    {
        var document = ConfigParser.Parse("""
            general:gaps_in = 6
            general:gaps_in = abc
            group:col.border_active = rgb(255,0,0)
            """);

        Assert.Equal(
            [
                new ConfigWarning(2, "Invalid value 'abc' for general:gaps_in."),
                new ConfigWarning(3, "Invalid value 'rgb(255,0,0)' for group:col.border_active."),
            ],
            document.Warnings);
        Assert.Equal(EdgeInsets.Uniform(6), LayoutOptions.From(document).GapsIn);
    }

    [Fact]
    public void KeywordAnswersTheWarningForAValueItCannotRead()
    {
        var core = Configured("general:gaps_in = 6\n");

        Assert.Equal("error: Invalid value 'abc' for general:gaps_in.", core.Execute("keyword general:gaps_in abc").Text);
        Assert.Equal("{\"option\":\"general:gaps_in\",\"css\":\"6 6 6 6\",\"set\":true}", core.Execute("getoption general:gaps_in").Text);
    }

    private static Core Configured(string text)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(text)));
        return core;
    }
}
