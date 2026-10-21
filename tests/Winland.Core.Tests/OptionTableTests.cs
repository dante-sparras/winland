using System.Text.Json;
using Winland.Core;

namespace Winland.Core.Tests;

public sealed class OptionTableTests
{
    public static TheoryData<string> Names() => [.. OptionTable.Names.Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Names))]
    public void GetOptionAnswersTheDefaultOfAnOptionNoLineSets(string name)
    {
        var core = Configured("");

        var reply = JsonDocument.Parse(core.Execute("getoption " + name).Text).RootElement;

        Assert.Equal(name, reply.GetProperty("option").GetString());
        Assert.False(reply.GetProperty("set").GetBoolean());
        Assert.Equal(3, reply.EnumerateObject().Count());
    }

    // The value getoption answers is one the config reads, so setting it again answers the same value.
    [Theory]
    [MemberData(nameof(Names))]
    public void TheValueGetOptionAnswersReadsBackAsTheSameValue(string name)
    {
        var core = Configured("");
        var unset = core.Execute("getoption " + name).Text;
        var text = ConfigText(JsonDocument.Parse(unset).RootElement.EnumerateObject().ElementAt(1).Value);
        if (text.Length == 0)
            return;

        Assert.Empty(ConfigParser.Parse($"{name} = {text}\n").Warnings);
        Assert.Equal("ok", core.Execute($"keyword {name} {text}").Text);
        Assert.Equal(unset.Replace("\"set\":false", "\"set\":true", StringComparison.Ordinal), core.Execute("getoption " + name).Text);
    }

    [Fact]
    public void GetOptionAnswersTheValueWinlandAppliedNotTheTextOfTheLine()
    {
        var core = Configured("""
            group:col.border_active = rgb(255,0,0)
            general:border_size = 50
            general:layout = Master
            """);

        Assert.Equal("{\"option\":\"group:col.border_active\",\"str\":\"0x66FFFF00\",\"set\":false}", core.Execute("getoption group:col.border_active").Text);
        Assert.Equal("{\"option\":\"general:border_size\",\"int\":20,\"set\":true}", core.Execute("getoption general:border_size").Text);
        Assert.Equal("{\"option\":\"general:layout\",\"str\":\"master\",\"set\":true}", core.Execute("getoption general:layout").Text);
    }

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

    private static string ConfigText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => string.Join(' ', value.EnumerateArray().Select(number => number.GetRawText())),
        JsonValueKind.String => value.GetString()!,
        _ => value.GetRawText(),
    };

    private static WindowManager Configured(string text)
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(text)));
        return core;
    }
}
