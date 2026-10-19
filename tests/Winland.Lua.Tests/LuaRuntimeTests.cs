using System.Collections.Concurrent;
using Winland;
using Winland.Lua;

namespace Winland.Lua.Tests;

public class LuaRuntimeTests
{
    private static readonly string ConfigPath = Path.Combine(Path.GetTempPath(), "winland-lua-runtime", "winland.lua");

    private sealed class World : ILuaWorld
    {
        public ConcurrentQueue<string> Commands { get; } = new();

        public ConcurrentQueue<string> Logged { get; } = new();

        public Func<string, string> Reply { get; set; } = _ => "ok";

        public string Command(string command)
        {
            Commands.Enqueue(command);
            return Reply(command);
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

    // An eval runs after everything queued before it, so its reply says the queue got that far.
    private static void Settle(LuaRuntime runtime) => Assert.Equal("ok", runtime.Eval("nil", repl: false));

    [Fact]
    public void ABindThatCallsAFunctionRunsItThroughWinlandsDispatcher()
    {
        var (runtime, world, translation) = Start("""
            hl.bind("SUPER + X", function()
                local result = hl.dispatch(hl.dsp.window.close())
                assert(result.ok and result.pass_event == false)
            end)
            """);
        using var _ = runtime;

        Assert.Equal(["bind = SUPER, X, luafunction, 1"], translation.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        runtime.Call("1");
        Settle(runtime);

        Assert.Equal(["dispatch killactive"], world.Commands);
        Assert.Empty(world.Logged);
    }

    [Fact]
    public void ADispatchWhileTheConfigLoadsRunsOnceWinlandRuns()
    {
        var (runtime, world, translation) = Start("hl.dispatch(hl.dsp.focus({ workspace = 3 }))\n");
        using var _ = runtime;

        Assert.Empty(translation.Warnings);
        Settle(runtime);
        Assert.Equal(["dispatch workspace 3"], world.Commands);
    }

    [Fact]
    public void EvalRunsExpressionsAndChunksAndReportsErrors()
    {
        var (runtime, world, _) = Start("answer = 41\n");
        using var _runtime = runtime;

        Assert.Equal("42", runtime.Eval("answer + 1", repl: true));
        Assert.Equal("ok", runtime.Eval("answer + 1", repl: false));
        Assert.Equal("a\tb\n1", runtime.Eval("print('a', 'b') return 1", repl: true));
        Assert.StartsWith("error: ", runtime.Eval("nosuch.call()", repl: false), StringComparison.Ordinal);
        Assert.StartsWith("error: ", runtime.Eval("this is not lua", repl: false), StringComparison.Ordinal);

        Assert.Equal("ok", runtime.Eval("hl.config({ general = { gaps_in = 9 } })", repl: false));
        Assert.Equal(["keyword general:gaps_in 9"], world.Commands);
    }

    [Fact]
    public void EvalAnswersAsThePinnedReleaseDoes()
    {
        var (runtime, _, _) = Start("");
        using var _runtime = runtime;

        // A print with nothing to print adds no line, and a chunk that does not load says the first load's status.
        Assert.Equal("1", runtime.Eval("print() return 1", repl: true));
        Assert.StartsWith("error: 3 eval:1:", runtime.Eval("this is not lua", repl: false), StringComparison.Ordinal);
    }

    [Fact]
    public void ReloadConfigReloadsAndAFunctionIsDispatched()
    {
        var (runtime, world, _) = Start("");
        using var _runtime = runtime;

        Assert.Equal("ok", runtime.Eval("hl.dispatch(hl.dsp.reload_config())", repl: false));
        Assert.Equal("ok", runtime.Eval("hl.dispatch(function() hl.dispatch(hl.dsp.exit()) end)", repl: false));

        Assert.Equal(["reload", "dispatch exit"], world.Commands);
    }

    [Fact]
    public void ATimerCanBeStoppedStartedAndGivenANewTimeout()
    {
        var (runtime, world, _) = Start("""
            ticks = 0
            t = hl.timer(function() ticks = ticks + 1 hl.dispatch(hl.dsp.no_op()) end, { timeout = 60000, type = "oneshot" })
            """);
        using var _ = runtime;

        Assert.Equal("true", runtime.Eval("t:is_enabled()", repl: true));
        Assert.Equal("false", runtime.Eval("t:set_enabled(false) return t:is_enabled()", repl: true));
        Assert.Equal("ok", runtime.Eval("t:set_timeout(10)", repl: false));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!world.Commands.Contains("dispatch no_op") && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        Settle(runtime);
        Assert.Equal("1\tnil\tHL.Timer(expired)", runtime.Eval("return ticks, t:is_enabled(), tostring(t)", repl: true));
    }

    [Fact]
    public void TheConfigsUnloadHandlersRunBeforeItIsReplaced()
    {
        var (runtime, world, _) = Start("hl.on('config.unload', function() hl.dispatch(hl.dsp.no_op()) end)\n");
        using var _ = runtime;

        runtime.Adopt(LuaConfig.Load("", ConfigPath, _ => null).Session);
        Settle(runtime);

        Assert.Equal(["dispatch no_op"], world.Commands);
    }

    [Fact]
    public void AFailedDispatchIsAnErrorOfTheEval()
    {
        var (runtime, world, _) = Start("");
        using var _runtime = runtime;
        world.Reply = _ => "error: no such window";

        var reply = runtime.Eval("hl.dispatch(hl.dsp.focus({ window = 'class:nosuch' }))", repl: false);

        Assert.Equal("error: line 1: hl.dispatch: error: no such window", reply);
    }

    [Fact]
    public void TimersRunUntilTheConfigIsReplaced()
    {
        var (runtime, world, _) = Start("""
            hl.timer(function() hl.dispatch(hl.dsp.exit()) end, { timeout = 20, type = "oneshot" })
            hl.timer(function() hl.dispatch(hl.dsp.no_op()) end, { timeout = 15, type = "repeat" })
            """);
        using var _ = runtime;

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((!world.Commands.Contains("dispatch exit") || world.Commands.Count(command => command == "dispatch no_op") < 3) && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        Assert.Single(world.Commands, command => command == "dispatch exit");
        runtime.Adopt(LuaConfig.Load("", ConfigPath, _ => null).Session);
        Settle(runtime);
        var count = world.Commands.Count;
        Thread.Sleep(100);
        Settle(runtime);
        Assert.Equal(count, world.Commands.Count);
    }

    [Fact]
    public void AFunctionThatRunsTooLongIsStoppedAndTheRuntimeGoesOn()
    {
        var (runtime, world, _) = Start("""
            hl.bind("SUPER + L", function() while true do end end)
            hl.bind("SUPER + K", function() hl.dispatch(hl.dsp.no_op()) end)
            """);
        using var _ = runtime;

        runtime.Call("1");
        runtime.Call("2");
        Settle(runtime);

        Assert.Contains(world.Logged, line => line.Contains("keybind callback ran longer than", StringComparison.Ordinal));
        Assert.Equal(["dispatch no_op"], world.Commands);
    }

    [Fact]
    public void WithoutALuaConfigEvalSaysSo()
    {
        using var runtime = new LuaRuntime(new World());

        Assert.False(runtime.Running);
        Assert.Equal("error: lua state not initialized", runtime.Eval("1", repl: true));
    }

    [Fact]
    public void EvalReplAndLuaDispatchesGoToTheLuaAndTheRestToTheCore()
    {
        var world = new World();
        using var runtime = new LuaRuntime(world);
        Assert.Equal("eval is only supported with a Lua config", runtime.Answer("eval 1", Execute));
        Assert.Null(runtime.Answer("dispatch workspace 2", Execute));

        runtime.Adopt(LuaConfig.Load("value = 6\n", ConfigPath, _ => null).Session);

        Assert.Equal("7", runtime.Answer("repl value + 1", Execute));
        Assert.Equal("ok", runtime.Answer("j/eval value", Execute));
        Assert.Equal("ok", runtime.Answer("dispatch hl.dsp.focus({ workspace = 2 })", Execute));
        Assert.Null(runtime.Answer("dispatch workspace 2", Execute));
        Assert.Null(runtime.Answer("clients", Execute));

        Assert.Equal(["dispatch workspace 2"], world.Commands);
    }

    // What the core would answer, for the commands a batch leaves to it.
    private static string Execute(string command) => "core " + command;

    [Fact]
    public void ABatchWithLuaInItRunsEachCommandWhereItBelongs()
    {
        using var runtime = new LuaRuntime(new World());
        runtime.Adopt(LuaConfig.Load("value = 6\n", ConfigPath, _ => null).Session);

        Assert.Null(runtime.Answer("[[BATCH]]clients;workspaces", Execute));
        Assert.Equal("core clients\n\n\n7\n\n\ncore workspaces", runtime.Answer("[[BATCH]]clients; repl value + 1; workspaces", Execute));
    }

    [Fact]
    public void TheLuaHearsTheEventsTheCoreNotices()
    {
        using var runtime = new LuaRuntime(new World());
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("submap = resize\nbind = , H, killactive\nsubmap = reset\n")));
        runtime.Listen(core);

        runtime.Adopt(LuaConfig.Load("seen = {}\nhl.on('keybinds.submap', function(name) seen[#seen + 1] = '[' .. name .. ']' end)\n", ConfigPath, _ => null).Session);

        Assert.Equal("ok", core.Execute("dispatch submap resize").Text);
        Assert.Equal("ok", core.Execute("dispatch submap reset").Text);

        Assert.Equal("[resize][]", runtime.Eval("table.concat(seen)", repl: true));
    }

    [Fact]
    public void AGestureHandsItsFunctionsEachPhaseOfTheSwipe()
    {
        using var runtime = new LuaRuntime(new World());
        var load = LuaConfig.Load("""
            seen = {}
            hl.gesture({ fingers = 3, direction = "horizontal", action = {
                start = function(e) seen[#seen + 1] = table.concat({ e.phase, e.type, e.direction, e.fingers, e.delta.x, e.delta.y, e.time_ms }, ":") end,
                update = function(e) seen[#seen + 1] = table.concat({ e.phase, e.direction, e.delta.x }, ":") end,
                finish = function(e) seen[#seen + 1] = table.concat({ e.phase, e.direction, tostring(e.cancelled), tostring(e.fingers), tostring(e.delta) }, ":") end,
            } })
            hl.gesture({ fingers = 4, direction = "up", action = function(...) seen[#seen + 1] = "legacy:" .. select("#", ...) end })
            """, ConfigPath, _ => null);
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(load.Translation.Text)));
        runtime.Listen(core);
        runtime.Adopt(load.Session);

        core.Handle(new SwipeBegan(3));
        core.Handle(new SwipeUpdated(3, -3, 0) { TimeMs = 5 });
        core.Handle(new SwipeUpdated(3, -4, 1) { TimeMs = 7 });
        core.Handle(new SwipeUpdated(3, -10, 0) { TimeMs = 9 });
        core.Handle(new SwipeEnded(Cancelled: true) { TimeMs = 11 });
        core.Handle(new SwipeBegan(4));
        core.Handle(new SwipeUpdated(4, 0, -10));
        core.Handle(new SwipeEnded());

        // The update that picks the gesture starts it and is its first update. The end has the configured direction.
        Assert.Equal(
            "start:swipe:LEFT:3:-4.0:1.0:7,update:LEFT:-4.0,update:LEFT:-10.0,end:HORIZONTAL:true:nil:nil,legacy:0",
            runtime.Eval("table.concat(seen, ',')", repl: true));
    }

    [Fact]
    public void AGestureFunctionThatRunsTooLongIsStopped()
    {
        var world = new World();
        using var runtime = new LuaRuntime(world);
        var load = LuaConfig.Load("""
            hl.gesture({ fingers = 3, direction = "left", action = { start = function() while true do end end } })
            """, ConfigPath, _ => null);
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(load.Translation.Text)));
        runtime.Listen(core);
        runtime.Adopt(load.Session);

        core.Handle(new SwipeBegan(3));
        core.Handle(new SwipeUpdated(3, -10, 0));
        Settle(runtime);

        Assert.Contains(world.Logged, line => line.StartsWith("lua: gesture start callback:", StringComparison.Ordinal) && line.Contains("0.05", StringComparison.Ordinal));
    }

    [Fact]
    public void TheParserTakesTheFunctionDispatcher()
    {
        _ = Dispatchers.TryBind(LuaConfig.FunctionDispatcher, _ => new DispatcherResult(null, null), out _);
        var translation = LuaConfig.Translate("hl.bind('SUPER + X', function() end)\n", ConfigPath, _ => null);

        var document = translation.Remap(ConfigParser.Parse(translation.Text));

        Assert.Empty(document.Warnings);
        Assert.Single(document.Binds);
    }
}
