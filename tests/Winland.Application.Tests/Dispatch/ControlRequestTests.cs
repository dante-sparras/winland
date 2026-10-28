using System.Text.Json;

namespace Winland.Application.Tests;

/// <summary>The control requests: <c>getprop</c>, JSON errors, option descriptions and the binds reply.</summary>
public class ControlRequestTests
{
    [Fact]
    public void GetpropReadsAndSetpropWrites()
    {
        var desk = new TestDesktop();
        desk.Spawn("kitty");

        ExpectProp(desk, "animation", "(unset)", """{"animation": ""}""");
        SetProp(desk, "animation", "teststyle");
        ExpectProp(desk, "animation", "teststyle", """{"animation": "teststyle"}""");

        ExpectProp(desk, "max_size", "inf inf", """{"max_size": [null,null]}""");
        SetProp(desk, "max_size", "200 150");
        ExpectProp(desk, "max_size", "200 150", """{"max_size": [200,150]}""");

        ExpectProp(desk, "min_size", "20 20", """{"min_size": [20,20]}""");
        SetProp(desk, "min_size", "100 50");
        ExpectProp(desk, "min_size", "100 50", """{"min_size": [100,50]}""");

        // The size expressions are read on a floating window, then it goes back to tiled.
        desk.Dispatch("togglefloating", "class:kitty");
        SetProp(desk, "max_size", "90+10 25*2");
        ExpectProp(desk, "max_size", "100 50", """{"max_size": [100,50]}""");
        SetProp(desk, "min_size", "window_w*0.5 window_h-10");
        ExpectProp(desk, "min_size", "50 40", """{"min_size": [50,40]}""");
        desk.Dispatch("togglefloating", "class:kitty");

        ExpectProp(desk, "opacity", "1", """{"opacity": 1}""");
        SetProp(desk, "opacity", "0.3");
        ExpectProp(desk, "opacity", "0.3", """{"opacity": 0.3}""");

        ExpectProp(desk, "opacity_inactive", "1", """{"opacity_inactive": 1}""");
        SetProp(desk, "opacity_inactive", "0.5");
        ExpectProp(desk, "opacity_inactive", "0.5", """{"opacity_inactive": 0.5}""");

        ExpectProp(desk, "active_border_color", "ee33ccff", """{"active_border_color": "ee33ccff"}""");
        SetProp(desk, "active_border_color", "rgb(abcdef)");
        ExpectProp(desk, "active_border_color", "ffabcdef", """{"active_border_color": "ffabcdef"}""");

        ExpectProp(desk, "rounding", "10", """{"rounding": 10}""");
        SetProp(desk, "rounding", "4");
        ExpectProp(desk, "rounding", "4", """{"rounding": 4}""");

        Assert.Equal("error: not enough args", desk.Send("getprop"));
        Assert.Equal("error: not enough args", desk.Send("getprop class:kitty"));
        Assert.Equal("error: window not found", desk.Send("getprop class:nonexistantclass animation"));
        Assert.Equal("error: prop not found", desk.Send("getprop class:kitty nonexistantprop"));
    }

    [Fact]
    public void JsonErrors()
    {
        var desk = new TestDesktop();

        Assert.Equal(JsonValueKind.Object, Json(desk.Send("getoption general:border_size")).ValueKind);
        Assert.Equal("error: no such option", desk.Send("getoption no:such"));
    }

    [Fact]
    public void DescriptionsShowCurrentValues()
    {
        var desk = new TestDesktop();
        ExpectConfigured(desk);

        desk.Keyword("general:border_size", "7");
        desk.Keyword("dwindle:preserve_split", "false");
        desk.Keyword("scrolling:column_width", "0.625");

        Assert.Equal("""{"option":"general:border_size","int":7,"set":true}""", desk.Send("getoption general:border_size"));
        Assert.Equal("""{"option":"dwindle:preserve_split","bool":false,"set":true}""", desk.Send("getoption dwindle:preserve_split"));
        Assert.Equal("""{"option":"scrolling:column_width","float":0.625,"set":true}""", desk.Send("getoption scrolling:column_width"));

        Assert.Equal("ok", desk.Send("reload"));
        ExpectConfigured(desk);
    }

    [Fact]
    public void BindsJson()
    {
        const string description = "hyprctl binds JSON regression";
        var desk = new TestDesktop();
        desk.Keyword("bindled", $"SUPER, F12, {description}, exec, true");

        var binds = Json(desk.Send("j/binds")).EnumerateArray().ToArray();
        Assert.Single(binds, bind => bind.GetProperty("key").GetString() == "F12");
        Assert.Contains(binds, bind =>
            bind.GetProperty("key").GetString() == "F12"
            && bind.GetProperty("has_description").GetBoolean()
            && bind.GetProperty("description").GetString() == description);

        Assert.Contains($"bindled = SUPER, F12, {description}, exec, true", desk.Send("binds").Split('\n'));

        Assert.Equal("ok", desk.Send("keyword unbind SUPER, F12"));
    }

    private static void SetProp(TestDesktop desk, string prop, string value) =>
        desk.Dispatch("setprop", $"class:kitty {prop} {value}");

    private static void ExpectProp(TestDesktop desk, string prop, string text, string json)
    {
        Assert.Equal(text, desk.Send($"getprop class:kitty {prop}"));
        Assert.Equal(Compact(json), Compact(desk.Send($"j/getprop class:kitty {prop}")));
    }

    private static void ExpectConfigured(TestDesktop desk)
    {
        Assert.Equal("""{"option":"general:border_size","int":2,"set":true}""", desk.Send("getoption general:border_size"));
        Assert.Equal("""{"option":"dwindle:preserve_split","bool":true,"set":true}""", desk.Send("getoption dwindle:preserve_split"));
        Assert.Equal("""{"option":"decoration:rounding","int":10,"set":true}""", desk.Send("getoption decoration:rounding"));
        Assert.Equal("""{"option":"scrolling:column_width","float":0.5,"set":true}""", desk.Send("getoption scrolling:column_width"));
    }

    private static void ExpectNormal(JsonElement workspace, int id, string name)
    {
        Assert.Equal(id, workspace.GetProperty("id").GetInt32());
        Assert.Equal(name, Text(workspace, "name"));
        Assert.Equal("normal", Text(workspace, "type"));
    }

    private static void ExpectSpecial(JsonElement workspace, string name)
    {
        Assert.False(workspace.TryGetProperty("id", out _));
        Assert.Equal(name, Text(workspace, "name"));
        Assert.Equal("special", Text(workspace, "type"));
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Compact(string json) => JsonSerializer.Serialize(Json(json));
}
