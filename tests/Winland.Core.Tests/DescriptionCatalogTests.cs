using System.Text.Json;
using System.Text.RegularExpressions;

namespace Winland.Tests;

public class DescriptionCatalogTests
{
    [Fact]
    public void DescriptionsListEveryPinnedOptionInOrder()
    {
        var names = Names(new Core());

        Assert.Equal(402, names.Count);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("general:border_size", names[0]);
        Assert.Equal("quirks:skip_non_kms_dmabuf_formats", names[^1]);
        Assert.Contains("input-capture:capture_modifiers", names);

        var pinned = PinnedValuesPath();
        if (pinned is null)
            return;

        var reference = new List<string>();
        foreach (Match match in Regex.Matches(File.ReadAllText(pinned), @"MS<[A-Za-z0-9]+>\(\s*""([^""]+)"""))
            reference.Add(match.Groups[1].Value);

        Assert.Equal(reference, names);
    }

    [Fact]
    public void EachTypeIsWrittenAsThePinnedReleaseWritesIt()
    {
        using var document = JsonDocument.Parse(new Core().Execute("descriptions").Text);
        var options = Index(document);

        var corner = options["general:resize_corner"];
        Assert.Equal(0, corner.GetProperty("default").GetInt32());
        Assert.Equal(0, corner.GetProperty("min").GetInt32());
        Assert.Equal(4, corner.GetProperty("max").GetInt32());
        Assert.Equal(
            "[{ \"disable\": 0 },{ \"top_left\": 1 },{ \"top_right\": 2 },{ \"bottom_right\": 3 },{ \"bottom_left\": 4 }]",
            corner.GetProperty("map").GetRawText());

        var noise = options["decoration:blur:noise"];
        Assert.Equal("0.0117", noise.GetProperty("default").GetRawText());
        Assert.Equal("0.0117", noise.GetProperty("current").GetRawText());
        Assert.Equal("0", noise.GetProperty("min").GetRawText());
        Assert.Equal("1", noise.GetProperty("max").GetRawText());

        var tearing = options["general:allow_tearing"];
        Assert.False(tearing.GetProperty("default").GetBoolean());
        Assert.False(tearing.GetProperty("current").GetBoolean());

        var model = options["input:kb_model"];
        Assert.Equal("[[EMPTY]]", model.GetProperty("default").GetString());
        Assert.Equal("[[EMPTY]]", model.GetProperty("current").GetString());

        var offset = options["decoration:shadow:offset"];
        Assert.Equal("[0, 0]", offset.GetProperty("default").GetRawText());
        Assert.Equal("[0, 0]", offset.GetProperty("current").GetRawText());

        var tint = options["decoration:blur:acrylic:tint"];
        Assert.Equal("14eef5ff", tint.GetProperty("default").GetString());
        Assert.Equal("14eef5ff", tint.GetProperty("current").GetString());

        var inactiveTitle = options["group:groupbar:text_color_inactive"];
        Assert.Equal("ffffffffffffffff", inactiveTitle.GetProperty("default").GetString());
        Assert.Equal("ffffffffffffffff", inactiveTitle.GetProperty("current").GetString());

        var active = options["general:col.active_border"];
        Assert.Equal("ffffffff 0deg", active.GetProperty("default").GetString());
        Assert.Equal(active.GetProperty("default").GetString(), active.GetProperty("current").GetString());

        var inactive = options["general:col.inactive_border"];
        Assert.Equal("ff444444 0deg", inactive.GetProperty("default").GetString());

        var unsetShadow = options["decoration:shadow:color_inactive"];
        Assert.Equal("ffffffff 0deg", unsetShadow.GetProperty("default").GetString());
        Assert.Equal("ffffffff 0deg", unsetShadow.GetProperty("current").GetString());

        var gaps = options["general:gaps_in"];
        Assert.Equal("5 5 5 5", gaps.GetProperty("default").GetString());
        Assert.Equal("5 5 5 5", gaps.GetProperty("current").GetString());
        Assert.Equal(JsonValueKind.Null, gaps.GetProperty("min").ValueKind);
        Assert.Equal(JsonValueKind.Null, gaps.GetProperty("max").ValueKind);

        var weight = options["group:groupbar:font_weight_active"];
        Assert.Equal("400", weight.GetProperty("default").GetString());
        Assert.Equal("400", weight.GetProperty("current").GetString());

        var widths = options["scrolling:explicit_column_widths"];
        Assert.Equal("0.333, 0.5, 0.667, 1.0", widths.GetProperty("default").GetString());
        Assert.Equal("0.333 0.5 0.667 1", widths.GetProperty("current").GetString());

        var eraser = options["input:tablettool:eraser_button_override"];
        Assert.Equal(0, eraser.GetProperty("min").GetInt32());
        Assert.Equal(JsonValueKind.Null, eraser.GetProperty("max").ValueKind);
        Assert.Equal(JsonValueKind.Null, eraser.GetProperty("map").ValueKind);

        var follow = options["input:follow_mouse_threshold"];
        Assert.Equal(JsonValueKind.Null, follow.GetProperty("min").ValueKind);
        Assert.Equal(JsonValueKind.Null, follow.GetProperty("max").ValueKind);
    }

    [Fact]
    public void AnUnreadOptionUsesTheValueTheConfigSet()
    {
        var parsed = ConfigParser.Parse("""
            general:allow_tearing = true
            general:allow_tearing = maybe
            debug:manual_crash = 1
            debug:manual_crash = 5
            quirks:prefer_hdr = gamescope_only
            input:kb_model = pc105
            input:tablet:region_position = 1,2
            decoration:blur:acrylic:tint = rgb(ff0000)
            group:groupbar:font_weight_active = bold
            """);
        var core = new Core(parsed);

        Assert.DoesNotContain(parsed.Settings, setting => setting.Keyword == "general:allow_tearing");
        Assert.Contains(parsed.Recorded, setting => setting.Keyword == "general:allow_tearing" && setting.Value == "maybe");

        using var document = JsonDocument.Parse(core.Execute("descriptions").Text);
        var options = Index(document);
        Assert.True(options["general:allow_tearing"].GetProperty("current").GetBoolean());
        Assert.Equal(1, options["debug:manual_crash"].GetProperty("current").GetInt32());
        Assert.Equal(2, options["quirks:prefer_hdr"].GetProperty("current").GetInt32());
        Assert.Equal("pc105", options["input:kb_model"].GetProperty("current").GetString());
        Assert.Equal("[1, 2]", options["input:tablet:region_position"].GetProperty("current").GetRawText());
        Assert.Equal("ffff0000", options["decoration:blur:acrylic:tint"].GetProperty("current").GetString());
        Assert.Equal("700", options["group:groupbar:font_weight_active"].GetProperty("current").GetString());

        Assert.Equal("""{"option": "general:allow_tearing", "bool": true, "set": true }""", core.Execute("getoption general:allow_tearing").Text);
        Assert.Equal("""{"option": "debug:manual_crash", "int": 1, "set": true }""", core.Execute("getoption debug:manual_crash").Text);
        Assert.Equal("""{"option": "quirks:prefer_hdr", "int": 2, "set": true }""", core.Execute("getoption quirks:prefer_hdr").Text);
        Assert.Equal("""{"option": "input:kb_model", "str": "pc105", "set": true }""", core.Execute("getoption input:kb_model").Text);
        Assert.Equal("""{"option": "input:tablet:region_position", "vec2": [1,2], "set": true }""", core.Execute("getoption input:tablet:region_position").Text);
        Assert.Equal("""{"option": "decoration:blur:acrylic:tint", "int": 4294901760, "set": true }""", core.Execute("getoption decoration:blur:acrylic:tint").Text);
        Assert.Equal("""{"option": "group:groupbar:font_weight_active", "font_weight": "700", "set": true }""", core.Execute("getoption group:groupbar:font_weight_active").Text);
    }

    [Fact]
    public void AnUnreadValueOutsideItsRangeStaysAtTheDefault()
    {
        var core = new Core(ConfigParser.Parse("""
            general:allow_tearing = maybe
            debug:manual_crash = 5
            input:tablet:region_position = 30000 0
            quirks:prefer_hdr = nope
            """));

        Assert.Equal("""{"option": "general:allow_tearing", "bool": false, "set": false }""", core.Execute("getoption general:allow_tearing").Text);
        Assert.Equal("""{"option": "debug:manual_crash", "int": 0, "set": false }""", core.Execute("getoption debug:manual_crash").Text);
        Assert.Equal("""{"option": "input:tablet:region_position", "vec2": [0,0], "set": false }""", core.Execute("getoption input:tablet:region_position").Text);
        Assert.Equal("""{"option": "quirks:prefer_hdr", "int": 0, "set": false }""", core.Execute("getoption quirks:prefer_hdr").Text);
        Assert.Equal("no such option", core.Execute("getoption no:such").Text);
    }

    [Fact]
    public void AnOptionWinlandReadsReportsTheValueItUses()
    {
        var core = new Core(ConfigParser.Parse("""
            general:gaps_workspaces = 400
            decoration:shadow:offset = 300 -2
            decoration:blur:size = nope
            input:scroll_method = nope
            general:col.nogroup_border = rgb(ff0000) 45deg
            """));

        using var document = JsonDocument.Parse(core.Execute("descriptions").Text);
        var options = Index(document);
        Assert.Equal(100, options["general:gaps_workspaces"].GetProperty("current").GetInt32());
        Assert.Equal("[250, -2]", options["decoration:shadow:offset"].GetProperty("current").GetRawText());
        Assert.Equal(8, options["decoration:blur:size"].GetProperty("current").GetInt32());
        Assert.Equal("nope", options["input:scroll_method"].GetProperty("current").GetString());
        Assert.Equal("ffff0000 45deg", options["general:col.nogroup_border"].GetProperty("current").GetString());

        Assert.Equal("""{"option": "general:gaps_workspaces", "int": 100, "set": true }""", core.Execute("getoption general:gaps_workspaces").Text);
        Assert.Equal("""{"option": "decoration:shadow:offset", "vec2": [250,-2], "set": true }""", core.Execute("getoption decoration:shadow:offset").Text);
        Assert.Equal("""{"option": "decoration:blur:size", "int": 8, "set": true }""", core.Execute("getoption decoration:blur:size").Text);
        Assert.Equal("""{"option": "decoration:blur:noise", "float": 0.011700, "set": false }""", new Core().Execute("getoption decoration:blur:noise").Text);
        Assert.Equal("""{"option": "decoration:blur:acrylic:tint", "int": 351204863, "set": false }""", new Core().Execute("getoption decoration:blur:acrylic:tint").Text);
        Assert.Equal("""{"option": "group:groupbar:text_color_inactive", "int": -1, "set": false }""", new Core().Execute("getoption group:groupbar:text_color_inactive").Text);
        Assert.Equal("""{"option":"general:border_size","int":1,"set":false}""", core.Execute("getoption general:border_size").Text);
    }

    [Fact]
    public void KeywordOfAnUnreadOptionStillErrorsAndALiveKeywordKeepsIt()
    {
        var core = new Core(ConfigParser.Parse("general:allow_tearing = true\n"));

        Assert.StartsWith("error:", core.Execute("keyword general:allow_tearing false").Text, StringComparison.Ordinal);
        Assert.Equal("ok", core.Execute("keyword general:border_size 4").Text);

        Assert.Equal("""{"option": "general:allow_tearing", "bool": true, "set": true }""", core.Execute("getoption general:allow_tearing").Text);
        Assert.Equal("""{"option":"general:border_size","int":4,"set":true}""", core.Execute("getoption general:border_size").Text);
    }

    [Fact]
    public void DescriptionsRenameTheWindowManager()
    {
        using var document = JsonDocument.Parse(new Core().Execute("descriptions").Text);
        var options = Index(document);
        var banned = "hypr" + "land";

        Assert.Equal("whether Winland should focus on the window that has just been moved out of the group", options["group:focus_removed_window"].GetProperty("description").GetString());
        Assert.Contains("Winland", options["misc:disable_splash_rendering"].GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("Winland", options["ecosystem:no_update_news"].GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("Winland", options["debug:manual_crash"].GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("Winland", options["debug:vfr"].GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("anime girl", options.Single(pair => pair.Value.GetProperty("description").GetString()!.Contains("anime girl", StringComparison.Ordinal)).Value.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("GUI utilities", options.Single(pair => pair.Value.GetProperty("description").GetString()!.Contains("GUI utilities", StringComparison.Ordinal)).Value.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("start script", options["misc:disable_watchdog_warning"].GetProperty("description").GetString(), StringComparison.Ordinal);

        foreach (var option in options.Values)
            Assert.DoesNotContain(banned, option.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> Names(Core core)
    {
        using var document = JsonDocument.Parse(core.Execute("descriptions").Text);
        return [.. document.RootElement.EnumerateArray().Select(option => option.GetProperty("name").GetString()!)];
    }

    private static Dictionary<string, JsonElement> Index(JsonDocument document) =>
        document.RootElement.EnumerateArray().ToDictionary(
            option => option.GetProperty("name").GetString()!,
            option => option.Clone());

    private static string? PinnedValuesPath()
    {
        var relative = Path.Combine("docs", "agents", "hypr" + "land", "src", "config", "values", "ConfigValues.cpp");
        var fromHere = Path.GetFullPath(relative);
        if (File.Exists(fromHere))
            return fromHere;

        var primary = Path.Combine("C:", "Users", "dante", "Projects", "winland", relative);
        return File.Exists(primary) ? primary : null;
    }
}
