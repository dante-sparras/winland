using System.Collections.Concurrent;
using Winland;
using Winland.Lua;

namespace Winland.Lua.Tests;

public class LuaLayoutTests
{
    private static readonly string ConfigPath = Path.Combine(Path.GetTempPath(), "winland-lua-layouts", "winland.lua");

    private static readonly Rectangle Area = new(20, 20, 1880, 1040);

    private sealed class World : ILuaWorld
    {
        public ConcurrentQueue<string> Commands { get; } = new();

        public ConcurrentQueue<string> Logged { get; } = new();

        public string Command(string command)
        {
            Commands.Enqueue(command);
            return "ok";
        }

        public void Log(string message) => Logged.Enqueue(message);
    }

    private static (LuaRuntime Runtime, World World, LuaTranslation Translation) Start(string code)
    {
        var world = new World();
        var runtime = new LuaRuntime(world);
        var load = LuaConfig.Load(code, ConfigPath, _ => null);
        runtime.Adopt(load.Session);
        return (runtime, world, load.Translation);
    }

    private static LayoutRequest Request(string name, int count, long generation = 1, string? message = null) =>
        new(7, generation, name, Area, [.. Enumerable.Range(1, count).Select(i => new LayoutTarget(new WindowId(0x10 + i), new Rectangle(0, 0, 10, 10)))])
        {
            Message = message,
        };

    private static string[] Placements(World world) =>
        [.. world.Commands.Where(command => command.StartsWith(LayoutRequest.Verb, StringComparison.Ordinal))];

    private const string Columns = """
        hl.layout.register("columns", {
            recalculate = function(ctx)
                for i, target in ipairs(ctx.targets) do
                    target:place(ctx:column(i, #ctx.targets))
                end
            end,
        })
        """;

    [Fact]
    public void RegisteringALayoutMakesItsLineWithTheLuaPrefix()
    {
        var (runtime, _, translation) = Start(Columns + "\nhl.layout.register('lua:rows', { recalculate = function() end })\n");
        using var _ = runtime;

        Assert.Empty(translation.Warnings);
        Assert.Equal(["lualayout = lua:columns", "lualayout = lua:rows"], translation.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void ARegistrationThePinnedReleaseRefusesWarnsAndMakesNoLine()
    {
        var (runtime, _, translation) = Start(Columns + """

            hl.layout.register("columns", { recalculate = function() end })
            hl.layout.register("", { recalculate = function() end })
            hl.layout.register("lua:", { recalculate = function() end })
            hl.layout.register("norecalc", { layout_msg = function() end })
            hl.layout.register("notable", 5)
            hl.layout.register({}, {})
            """);
        using var _ = runtime;

        Assert.Equal(["lualayout = lua:columns"], translation.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(
            [
                "hl.layout.register: layout 'lua:columns' is already registered",
                "hl.layout.register: layout name cannot be empty",
                "hl.layout.register: layout name cannot be empty",
                "hl.layout.register: provider table must define recalculate(ctx)",
                "hl.layout.register: bad argument #2 (table expected, got number)",
                "layout.register: bad argument 1: expected string, got table",
            ],
            translation.Warnings.Select(warning => warning.Message));
    }

    [Fact]
    public void RecalculatePlacesTheTargetsAndTheAnswerGoesBackAsLayoutplace()
    {
        var (runtime, world, _) = Start(Columns);
        using var _runtime = runtime;

        runtime.Arrange(Request("lua:columns", 2));
        Assert.True(runtime.Settle(LuaRuntime.EvalWait));

        Assert.Equal(["layoutplace 7 1 0x11 20 20 940 1040 0x12 960 20 940 1040"], Placements(world));
        Assert.Empty(world.Logged);
    }

    [Fact]
    public void TheContextHelpersSplitTheAreaAsThePinnedReleaseDoes()
    {
        var (runtime, world, _) = Start("""
            hl.layout.register("helpers", {
                recalculate = function(ctx)
                    local t = ctx.targets
                    assert(#t == 4 and t[1].index == 1 and t[4].index == 4)
                    assert(tostring(t[2]) == "HL.LayoutTarget(2)")
                    assert(t[1].box.w == 10 and t[1].box.x == 0)
                    assert(ctx.area.x == 20 and ctx.area.w == 1880)
                    local cell = ctx:grid_cell(4, 2)
                    assert(cell.x == 960 and cell.y == 540 and cell.w == 940 and cell.h == 520)
                    local row = ctx:row(2, 4)
                    assert(row.y == 280 and row.h == 260 and row.w == 1880)
                    local three = ctx:grid_cell(1, 1, 4)
                    assert(three.h == 260)
                    t[1]:place(ctx:split(ctx.area, "left", 0.25))
                    t[2]:set_box(ctx:split(ctx.area, "bottom", 2))
                    t[3]:place({ x = 1, y = 2, w = -5, h = 4 })
                    assert(t[3].box.w == 0)
                end,
            })
            """);
        using var _runtime = runtime;

        runtime.Arrange(Request("lua:helpers", 4));
        Assert.True(runtime.Settle(LuaRuntime.EvalWait));

        Assert.Empty(world.Logged);
        Assert.Equal(["layoutplace 7 1 0x11 20 20 470 1040 0x12 20 20 1880 1040 0x13 1 2 0 4"], Placements(world));
    }

    [Fact]
    public void ATargetsWindowIsTheWindowObject()
    {
        var (runtime, world, _) = Start("""
            hl.layout.register("windows", {
                recalculate = function(ctx)
                    seen = getmetatable(ctx.targets[1].window).__name
                end,
            })
            """);
        using var _runtime = runtime;

        runtime.Arrange(Request("lua:windows", 1));
        Assert.True(runtime.Settle(LuaRuntime.EvalWait));

        Assert.Equal("HL.Window", runtime.Eval("seen", repl: true));
        Assert.Equal(["layoutplace 7 1"], Placements(world));
    }

    [Fact]
    public void ARecalculateThatFailsFallsBackToTheGridAndIsLogged()
    {
        var (runtime, world, _) = Start("hl.layout.register('broken', { recalculate = function() error('boom') end })\n");
        using var _runtime = runtime;

        runtime.Arrange(Request("lua:broken", 1));
        Assert.True(runtime.Settle(LuaRuntime.EvalWait));

        Assert.Equal(["layoutplace 7 1 failed"], Placements(world));
        Assert.Contains(world.Logged, line => line.Contains("lua layout lua:broken recalculate callback", StringComparison.Ordinal) && line.Contains("boom", StringComparison.Ordinal));
    }

    [Fact]
    public void ARecalculateThatRunsTooLongIsStopped()
    {
        var (runtime, world, _) = Start("hl.layout.register('slow', { recalculate = function() while true do end end })\n");
        using var _runtime = runtime;

        runtime.Arrange(Request("lua:slow", 1));
        Assert.True(runtime.Settle(LuaRuntime.EvalWait));

        Assert.Equal(["layoutplace 7 1 failed"], Placements(world));
    }

    [Fact]
    public void ALayoutMessageRunsLayoutMsgAndThenRecalculate()
    {
        var (runtime, world, _) = Start("""
            local ratio = 0.5
            hl.layout.register("split", {
                recalculate = function(ctx)
                    ctx.targets[1]:place(ctx:split(ctx.area, "left", ratio))
                end,
                layout_msg = function(ctx, msg)
                    if msg == "grow" then
                        ratio = 0.75
                        return true
                    elseif msg == "no" then
                        return false
                    end
                    return "split: expected grow"
                end,
            })
            """);
        using var _runtime = runtime;

        runtime.Arrange(Request("lua:split", 1, generation: 1));
        runtime.Arrange(Request("lua:split", 1, generation: 2, message: "grow"));
        runtime.Arrange(Request("lua:split", 1, generation: 3, message: "no"));
        runtime.Arrange(Request("lua:split", 1, generation: 4, message: "other"));
        Assert.True(runtime.Settle(LuaRuntime.EvalWait));

        Assert.Equal("layoutplace 7 4 0x11 20 20 1410 1040", Placements(world)[^1]);
        Assert.Equal(["lua: lua layout lua:split rejected layoutmsg", "lua: split: expected grow"], world.Logged);
    }

    [Fact]
    public void ALayoutThisConfigDoesNotHaveIsNotAnswered()
    {
        var (runtime, world, _) = Start(Columns);
        using var _runtime = runtime;

        runtime.Arrange(Request("lua:missing", 2));
        runtime.Arrange(Request("lua:columns", 0, generation: 2));
        Assert.True(runtime.Settle(LuaRuntime.EvalWait));

        Assert.Empty(Placements(world));
    }

    [Fact]
    public void ARegistrationWhileWinlandRunsSendsItsLine()
    {
        var (runtime, world, _) = Start("");
        using var _runtime = runtime;

        Assert.Equal("ok", runtime.Eval("hl.layout.register('late', { recalculate = function() end })", repl: false));

        Assert.Equal(["keyword lualayout lua:late"], world.Commands);
    }

    [Fact]
    public void ANewConfigLaysOutAgainWhatTheOldOneLaidOut()
    {
        var (runtime, world, _) = Start(Columns);
        using var _runtime = runtime;

        runtime.Arrange(Request("lua:columns", 1));
        Assert.True(runtime.Settle(LuaRuntime.EvalWait));
        runtime.Adopt(LuaConfig.Load(Columns.Replace("ctx:column(i, #ctx.targets)", "ctx:row(i, 2)", StringComparison.Ordinal), ConfigPath, _ => null).Session);
        Assert.True(runtime.Settle(LuaRuntime.EvalWait));

        Assert.Equal(["layoutplace 7 1 0x11 20 20 1880 1040", "layoutplace 7 1 0x11 20 20 1880 520"], Placements(world));
    }
}
