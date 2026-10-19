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
    public void TheParserTakesTheFunctionDispatcher()
    {
        _ = Dispatchers.TryBind(LuaConfig.FunctionDispatcher, _ => new DispatcherResult(null, null), out _);
        var translation = LuaConfig.Translate("hl.bind('SUPER + X', function() end)\n", ConfigPath, _ => null);

        var document = translation.Remap(ConfigParser.Parse(translation.Text));

        Assert.Empty(document.Warnings);
        Assert.Single(document.Binds);
    }
}
