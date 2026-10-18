using System.Text.Json;

namespace Winland.Tests.Conformance;

/// <summary>
/// The pinned release's cases for its control requests. Each test is named after its case, and sends the same
/// requests. A text reply there and a JSON reply here that carry the same value are the same check.
/// </summary>
[Trait("Category", "Conformance")]
public class ControlConformance
{
    [Fact(Skip = "input:kb_layout is a Gap, and there is no switchxkblayout or devices request.")]
    public void HyprctlDevicesActiveLayoutIndex()
    {
        var desk = new Desk();
        Assert.Equal("ok", desk.Send("r/keyword input:kb_layout us,pl,ua"));

        for (var i = 0; i < 3; i++)
        {
            desk.Send($"switchxkblayout all {i}");
            var keyboards = Json(desk.Send("j/devices")).GetProperty("keyboards").EnumerateArray();
            Assert.Contains(keyboards, keyboard => keyboard.GetProperty("active_layout_index").GetInt32() == i);
        }
    }

    [Fact]
    public void HyprctlGetprop()
    {
        var desk = new Desk();
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
        desk.Dispatch("setfloating", "class:kitty");
        SetProp(desk, "max_size", "90+10 25*2");
        ExpectProp(desk, "max_size", "100 50", """{"max_size": [100,50]}""");
        SetProp(desk, "min_size", "window_w*0.5 window_h-10");
        ExpectProp(desk, "min_size", "50 40", """{"min_size": [50,40]}""");
        desk.Dispatch("settiled", "class:kitty");

        ExpectProp(desk, "opacity", "1", """{"opacity": 1}""");
        SetProp(desk, "opacity", "0.3");
        ExpectProp(desk, "opacity", "0.3", """{"opacity": 0.3}""");

        ExpectProp(desk, "opacity_inactive", "1", """{"opacity_inactive": 1}""");
        SetProp(desk, "opacity_inactive", "0.5");
        ExpectProp(desk, "opacity_inactive", "0.5", """{"opacity_inactive": 0.5}""");

        ExpectProp(desk, "opacity_fullscreen", "1", """{"opacity_fullscreen": 1}""");
        SetProp(desk, "opacity_fullscreen", "0.75");
        ExpectProp(desk, "opacity_fullscreen", "0.75", """{"opacity_fullscreen": 0.75}""");

        foreach (var name in new[] { "opacity_override", "opacity_inactive_override", "opacity_fullscreen_override", "allows_input" })
        {
            ExpectProp(desk, name, "false", $$"""{"{{name}}": false}""");
            SetProp(desk, name, "true");
            ExpectProp(desk, name, "true", $$"""{"{{name}}": true}""");
        }

        ExpectProp(desk, "active_border_color", "ee33ccff ee00ff99 45deg", """{"active_border_color": "ee33ccff ee00ff99 45deg"}""");
        SetProp(desk, "active_border_color", "rgb(abcdef)");
        ExpectProp(desk, "active_border_color", "ffabcdef 0deg", """{"active_border_color": "ffabcdef 0deg"}""");

        ExpectProp(desk, "rounding", "10", """{"rounding": 10}""");
        SetProp(desk, "rounding", "4");
        ExpectProp(desk, "rounding", "4", """{"rounding": 4}""");

        ExpectProp(desk, "rounding_power", "2", """{"rounding_power": 2}""");
        SetProp(desk, "rounding_power", "1.25");
        ExpectProp(desk, "rounding_power", "1.25", """{"rounding_power": 1.25}""");

        Assert.Equal("not enough args", desk.Send("getprop"));
        Assert.Equal("not enough args", desk.Send("getprop class:kitty"));
        Assert.Equal("window not found", desk.Send("getprop class:nonexistantclass animation"));
        Assert.Equal("prop not found", desk.Send("getprop class:kitty nonexistantprop"));
    }

    [Fact]
    public void HyprctlSubmap()
    {
        var desk = new Desk();

        Assert.Equal("default", desk.Send("submap").TrimEnd('\n'));
        Assert.Equal("default", Json(desk.Send("j/submap")).GetString());
    }

    [Fact(Skip = "There is no descriptions request.")]
    public void HyprctlJsonErrors()
    {
        var desk = new Desk();

        Assert.Equal(JsonValueKind.Array, Json(desk.Send("descriptions")).ValueKind);
    }

    [Fact(Skip = "There is no descriptions request.")]
    public void HyprctlDescriptionsCurrentValues()
    {
        var desk = new Desk();
        ExpectConfigured(desk);

        desk.Keyword("general:border_size", "7");
        desk.Keyword("general:snap:enabled", "false");
        desk.Keyword("master:new_status", "inherit");
        desk.Keyword("scrolling:follow_min_visible", "0.625");

        var descriptions = Descriptions(desk);
        ExpectDescription(descriptions, "general:border_size", "1", "7");
        ExpectDescription(descriptions, "general:snap:enabled", "false", "false");
        ExpectDescription(descriptions, "master:new_status", "\"slave\"", "\"inherit\"");
        ExpectDescription(descriptions, "scrolling:follow_min_visible", "0.4", "0.625");

        Assert.Equal("ok", desk.Send("reload"));
        ExpectConfigured(desk);
    }

    [Fact]
    public void HyprctlBindsJson()
    {
        const string description = "hyprctl binds JSON regression";
        var desk = new Desk();
        desk.Keyword("bindled", $"SUPER, F12, {description}, exec, true");

        var binds = Json(desk.Send("j/binds")).EnumerateArray().ToArray();
        Assert.Single(binds, bind => bind.GetProperty("key").GetString() == "F12");
        Assert.Contains(binds, bind =>
            bind.GetProperty("key").GetString() == "F12"
            && bind.GetProperty("has_description").GetBoolean()
            && bind.GetProperty("description").GetString() == description
            && bind.GetProperty("allow_input_capture").ValueKind == JsonValueKind.False);

        // The reference reads the flags of that bind from the text reply as "locked, repeat".
        var described = Json(desk.Send("binds")).EnumerateArray()
            .Single(bind => bind.GetProperty("description").GetString() == description);
        Assert.True(described.GetProperty("locked").GetBoolean());
        Assert.True(described.GetProperty("repeat").GetBoolean());

        Assert.Equal("ok", desk.Send("keyword unbind SUPER, F12"));
    }

    [Fact]
    public void HyprctlWorkspaceJson()
    {
        const string name = "hyprctl_workspace_json";
        const string special = "special:hyprctl_workspace_json_special";
        var desk = new Desk();

        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Ok("workspace", "4201");
        desk.Ok("renameworkspace", $"4201 {name}");
        desk.Ok("workspace", special);

        var monitor = Json(desk.Send("j/monitors")).EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "HEADLESS-2");
        ExpectNormal(monitor.GetProperty("activeWorkspace"), 4201, name);
        ExpectSpecial(monitor.GetProperty("specialWorkspace"), special);

        var workspaces = desk.Workspaces();
        Assert.Contains(workspaces, workspace =>
            workspace.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 4201
            && Text(workspace, "name") == name && Text(workspace, "type") == "normal");
        Assert.Contains(workspaces, workspace =>
            !workspace.TryGetProperty("id", out _) && Text(workspace, "name") == special && Text(workspace, "type") == "special");
    }

    private static void SetProp(Desk desk, string prop, string value) =>
        desk.Dispatch("setprop", $"class:kitty {prop} {value}");

    private static void ExpectProp(Desk desk, string prop, string text, string json)
    {
        Assert.Equal(text, desk.Send($"getprop class:kitty {prop}"));
        Assert.Equal(Compact(json), Compact(desk.Send($"j/getprop class:kitty {prop}")));
    }

    private static void ExpectConfigured(Desk desk)
    {
        var descriptions = Descriptions(desk);
        ExpectDescription(descriptions, "general:border_size", "1", "2");
        ExpectDescription(descriptions, "general:snap:enabled", "false", "true");
        ExpectDescription(descriptions, "decoration:rounding", "0", "10");
        ExpectDescription(descriptions, "master:new_status", "\"slave\"", "\"master\"");
        ExpectDescription(descriptions, "scrolling:follow_min_visible", "0.4", "1");
    }

    private static JsonElement[] Descriptions(Desk desk) => [.. Json(desk.Send("descriptions")).EnumerateArray()];

    // The option is described once, with this default and this current value, each written as JSON.
    private static void ExpectDescription(JsonElement[] descriptions, string name, string defaultValue, string current)
    {
        var option = Assert.Single(descriptions, item => Text(item, "name") == name);
        Assert.Equal(defaultValue, Compact(option.GetProperty("default").GetRawText()));
        Assert.Equal(current, Compact(option.GetProperty("current").GetRawText()));
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

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
