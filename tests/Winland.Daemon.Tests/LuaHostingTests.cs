using Winland;
using Winland.Lua;

namespace Winland.Daemon.Tests;

public class LuaHostingTests
{
    private sealed class World : ILuaWorld
    {
        public string Command(string command) => "ok";

        public void Log(string message)
        {
        }
    }

    [Fact]
    public void TheLuaOfTheConfigReadLastIsTakenOnce()
    {
        using var runtime = new LuaRuntime(new World());
        var path = Path.Combine(Path.GetTempPath(), "winland-hosting", "winland.lua");
        LuaHosting.Load("value = 5\n", path, _ => null);
        LuaHosting.Load("value = 6\n", path, _ => null);
        runtime.Adopt(LuaHosting.Take());
        Assert.Null(LuaHosting.Take());

        Assert.Equal("7", runtime.Answer("repl value + 1", command => command));
    }

    [Fact]
    public void AConfigThatIsNotLuaLetsTheLuaGo()
    {
        var path = Path.Combine(Path.GetTempPath(), "winland-hosting", "winland.lua");
        LuaHosting.Load("value = 6\n", path, _ => null);
        LuaHosting.Clear();

        Assert.Null(LuaHosting.Take());
    }
}
