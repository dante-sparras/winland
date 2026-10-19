using Winland;
using Winland.Lua;

namespace Winland.Daemon.Tests;

public class LuaHostingTests
{
    private sealed class World : ILuaWorld
    {
        public List<string> Commands { get; } = [];

        public string Command(string command)
        {
            lock (Commands)
                Commands.Add(command);

            return "ok";
        }

        public void Log(string message)
        {
        }
    }

    [Fact]
    public void EvalReplAndLuaDispatchesGoToTheLuaAndTheRestToTheCore()
    {
        var world = new World();
        using var runtime = new LuaRuntime(world);
        Assert.Equal("eval is only supported with a Lua config", LuaHosting.Answer(runtime, "eval 1"));
        Assert.Null(LuaHosting.Answer(runtime, "dispatch workspace 2"));

        var path = Path.Combine(Path.GetTempPath(), "winland-hosting", "winland.lua");
        LuaHosting.Load("value = 6\n", path, _ => null);
        runtime.Adopt(LuaHosting.Take());
        Assert.Null(LuaHosting.Take());

        Assert.Equal("7", LuaHosting.Answer(runtime, "repl value + 1"));
        Assert.Equal("ok", LuaHosting.Answer(runtime, "j/eval value"));
        Assert.Equal("ok", LuaHosting.Answer(runtime, "dispatch hl.dsp.focus({ workspace = 2 })"));
        Assert.Null(LuaHosting.Answer(runtime, "dispatch workspace 2"));
        Assert.Null(LuaHosting.Answer(runtime, "clients"));

        lock (world.Commands)
            Assert.Equal(["dispatch workspace 2"], world.Commands);
    }

    [Fact]
    public void TheLuaHearsTheEventsTheCoreNotices()
    {
        using var runtime = new LuaRuntime(new World());
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("submap = resize\nbind = , H, killactive\nsubmap = reset\n")));
        LuaHosting.Listen(core, runtime);

        var path = Path.Combine(Path.GetTempPath(), "winland-hosting", "winland.lua");
        LuaHosting.Load("seen = {}\nhl.on('keybinds.submap', function(name) seen[#seen + 1] = '[' .. name .. ']' end)\n", path, _ => null);
        runtime.Adopt(LuaHosting.Take());

        Assert.Equal("ok", core.Execute("dispatch submap resize").Text);
        Assert.Equal("ok", core.Execute("dispatch submap reset").Text);

        Assert.Equal("[resize][]", runtime.Eval("table.concat(seen)", repl: true));
    }
}
