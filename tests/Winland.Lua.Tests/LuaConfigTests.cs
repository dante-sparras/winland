using Winland;
using Winland.Lua;

namespace Winland.Lua.Tests;

public class LuaConfigTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "winland-lua-tests");
    private static readonly string ConfigPath = Path.Combine(Folder, "winland.lua");

    private static LuaTranslation Run(string code, Func<string, string?>? read = null, double budget = LuaConfig.Budget) =>
        LuaConfig.Translate(code, ConfigPath, read ?? (_ => null), budget);

    private static string[] Lines(LuaTranslation translation) =>
        translation.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void ConfigTablesBecomeOptionLines()
    {
        var translation = Run("""
            hl.config({
                general = {
                    gaps_in = 5,
                    border_size = 2,
                    resize_on_border = false,
                    col = {
                        active_border = { colors = { "rgba(33ccffee)", "rgba(00ff99ee)" }, angle = 45 },
                        inactive_border = "rgba(595959aa)",
                    },
                    gaps_out = { top = 10, left = 4 },
                },
                decoration = { active_opacity = 0.85, shadow = { color = 0xee1a1a1a } },
                input = { touchpad = { tap_to_click = true } },
            })
            """);

        Assert.Empty(translation.Warnings);
        Assert.Equal(
            [
                "decoration:active_opacity = 0.85",
                "decoration:shadow:color = 0xEE1A1A1A",
                "general:border_size = 2",
                "general:col.active_border = rgba(33ccffee) rgba(00ff99ee) 45deg",
                "general:col.inactive_border = rgba(595959aa)",
                "general:gaps_in = 5",
                "general:gaps_out = 10 0 0 4",
                "general:resize_on_border = false",
                "input:touchpad:tap-to-click = true",
            ],
            Lines(translation).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheParserReadsTheLinesAndItsWarningsPointAtTheLuaLines()
    {
        var translation = Run("""
            hl.config({ general = { gaps_in = 7 } })

            hl.config({ general = { no_such_option = 1 } })
            """);

        var document = translation.Remap(ConfigParser.Parse(translation.Text));

        Assert.Equal(7, LayoutOptions.From(document).GapsIn.Top);
        var warning = Assert.Single(document.Warnings);
        Assert.Equal(3, warning.Line);
        Assert.Null(warning.Source);
    }

    [Fact]
    public void CommandsRunOnLoadAtStartOrAtExit()
    {
        var translation = Run("""
            hl.env("XCURSOR_SIZE", 24)
            hl.exec_cmd("notepad")
            hl.on("winland.start", function()
                hl.exec_cmd("explorer", { workspace = 2, float = true, pin = false })
            end)
            hl.on("winland.shutdown", function() hl.exec_cmd("cleanup") end)
            """);

        Assert.Empty(translation.Warnings);
        Assert.Equal(
            [
                "env = XCURSOR_SIZE,24",
                "exec = notepad",
                "exec-once = [float; workspace 2] explorer",
                "exec-shutdown = cleanup",
            ],
            Lines(translation).Select(line => line.Replace("[workspace 2; float]", "[float; workspace 2]", StringComparison.Ordinal)));
        Assert.Equal([1, 2, 4, 6], translation.Origins.Select(origin => origin.Line));
    }

    [Fact]
    public void EventsThatDoNotRunYetAndUnknownOnesWarn()
    {
        var translation = Run("""
            hl.on("window.open", function() end)
            hl.on("nosuch.event", function() end)
            """);

        Assert.Equal(2, translation.Warnings.Count);
        Assert.Equal(1, translation.Warnings[0].Line);
        Assert.Contains("do not run yet", translation.Warnings[0].Message, StringComparison.Ordinal);
        Assert.StartsWith("hl.on: unknown event \"nosuch.event\"", translation.Warnings[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CallsOfLaterPhasesWarnOnceAndTheConfigRunsOn()
    {
        var translation = Run("""
            hl.get_windows()
            hl.get_windows()
            hl.config({ general = { gaps_in = 3 } })
            """);

        var warning = Assert.Single(translation.Warnings);
        Assert.Equal(new ConfigWarning(1, "hl.get_windows is not honored yet"), warning);
        Assert.Equal(["general:gaps_in = 3"], Lines(translation));
    }

    [Fact]
    public void TheSandboxHasNoWayToTheSystem()
    {
        var translation = Run("""
            assert(io == nil and debug == nil and dofile == nil and loadfile == nil)
            assert(os.execute == nil and os.remove == nil and os.exit == nil and os.time ~= nil)
            assert(package.loadlib == nil and package.loaded.io == nil)
            assert(load(string.dump(function() end)) == nil)
            hl.config({ general = { gaps_in = 1 } })
            """);

        Assert.Empty(translation.Warnings);
        Assert.Equal(["general:gaps_in = 1"], Lines(translation));
    }

    [Fact]
    public void RequireReadsLuaFilesFromTheConfigsFolderOnly()
    {
        var module = Path.Combine(Folder, "parts", "gaps.lua");
        string? Read(string path) => path == module ? "hl.config({ general = { gaps_in = 9 } })\nhl.config(5)\n" : null;

        var translation = Run("""
            require("parts.gaps")
            local ok = pcall(require, "..secret")
            assert(not ok)
            """, Read);

        Assert.Equal(["general:gaps_in = 9"], Lines(translation));
        Assert.Equal([module], translation.Sources);
        Assert.Equal(new LuaOrigin(1, module), translation.Origins[0]);
        Assert.Equal(new ConfigWarning(2, "hl.config: argument must be a table", module), Assert.Single(translation.Warnings));
    }

    [Fact]
    public void AnErrorStopsTheConfigWhereItIsAndSaysWhere()
    {
        var translation = Run("""
            hl.config({ general = { gaps_in = 2 } })
            local broken = nil
            broken.call()
            hl.config({ general = { gaps_in = 4 } })
            """);

        Assert.Equal(["general:gaps_in = 2"], Lines(translation));
        var warning = Assert.Single(translation.Warnings);
        Assert.Equal(3, warning.Line);
        Assert.Contains("broken", warning.Message, StringComparison.Ordinal);

        var syntax = Run("hl.config({\n");
        Assert.Equal(2, Assert.Single(syntax.Warnings).Line);
    }

    [Fact]
    public void AConfigThatRunsTooLongIsStopped()
    {
        var translation = Run("hl.config({ general = { gaps_in = 1 } })\nwhile true do end\n", budget: 0.2);

        Assert.Equal(["general:gaps_in = 1"], Lines(translation));
        Assert.Contains("ran longer than", Assert.Single(translation.Warnings).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("general.col.active_border", "general:col.active_border")]
    [InlineData("group.groupbar.col.locked_active", "group:groupbar:col.locked_active")]
    [InlineData("input.touchpad.tap_and_drag", "input:touchpad:tap-and-drag")]
    [InlineData("misc.vfr", "misc:vfr")]
    public void LuaPathsNameTheirOptions(string path, string option) =>
        Assert.Equal(option, LuaRun.OptionName(path));
}
