using System.Collections.Concurrent;
using System.Diagnostics;
using Winland;
using Winland.Lua;

namespace Winland.Lua.Tests;

public class LuaNotificationTests
{
    private static readonly string ConfigPath = Path.Combine(Path.GetTempPath(), "winland-lua-notifications", "winland.lua");

    private sealed class World : ILuaWorld
    {
        public ConcurrentQueue<string> Commands { get; } = new();

        public Func<string, string> Reply { get; set; } = _ => "ok";

        public string Command(string command)
        {
            Commands.Enqueue(command);
            return Reply(command);
        }

        public void Log(string message)
        {
        }

        public bool? IsKeyDown(string key, int code) => false;
    }

    private static (LuaRuntime Runtime, Core Core) Start(string code)
    {
        var core = new Core();
        var world = new World { Reply = command => core.Execute(command).Text };
        var runtime = new LuaRuntime(world);
        runtime.Adopt(LuaConfig.Load(code, ConfigPath, _ => null).Session);
        return (runtime, core);
    }

    [Fact]
    public void ACreateThatIsNotATableWarnsAndReturnsNil()
    {
        var load = LuaConfig.Load("hl.notification.create(1)\n", ConfigPath, _ => null);
        Assert.Equal(
            new ConfigWarning(1, "hl.notification.create: expected a table { text, duration, icon?, color?, font_size? }"),
            Assert.Single(load.Translation.Warnings));
        load.Session.Dispose();
    }

    [Fact]
    public void CreateGetAndTheObjectRoundTrip()
    {
        var (runtime, _) = Start("");
        using var _runtime = runtime;

        Assert.Equal("HL.Notification(0x1)", runtime.Eval("return tostring(hl.notification.create({ text = 'hi', duration = 5000 }))", repl: true));
        Assert.Equal("true", runtime.Eval("a = hl.notification.create({ text = 'a', time = 5000 }) return a == hl.notification.get()[2]", repl: true));
        Assert.Equal(
            "next\t4294901760\t1\t20\t8000",
            runtime.Eval("""
                n = hl.notification.get()[1]
                n:set_text('next')
                n:set_color('#ff0000')
                n:set_icon('info')
                n:set_font_size(20)
                n:set_timeout(8000)
                return n:get_text(), n:get_color(), n:get_icon(), n:get_font_size(), n:get_timeout()
                """, repl: true));
        Assert.Equal("4294901760", runtime.Eval("n:set_color('rgb(255, 0, 0)') return n:get_color()", repl: true));
        Assert.Equal("ok", runtime.Eval("n:set_color(0) ", repl: false));
        Assert.Equal("0", runtime.Eval("return n:get_color()", repl: true));
    }

    [Fact]
    public void BadArgumentsWarnAndDoNotCreate()
    {
        var (runtime, _) = Start("");
        using var _runtime = runtime;

        Assert.Contains("invalid 'icon'", runtime.Eval("return hl.notification.create({ text = 'a', duration = 1, icon = 'nope' })", repl: false), StringComparison.Ordinal);
        Assert.Contains("'font_size' must be > 0", runtime.Eval("return hl.notification.create({ text = 'a', duration = 1, font_size = 0 })", repl: false), StringComparison.Ordinal);
        Assert.Contains("duration must be >= 0", runtime.Eval("return hl.notification.create({ text = 'a', duration = -1 })", repl: false), StringComparison.Ordinal);
        Assert.Contains("'duration'", runtime.Eval("return hl.notification.create({ text = 'a' })", repl: false), StringComparison.Ordinal);
        Assert.Contains("invalid 'color'", runtime.Eval("return hl.notification.create({ text = 'a', duration = 1, color = 'nope' })", repl: false), StringComparison.Ordinal);
        Assert.Equal("0", runtime.Eval("return #hl.notification.get()", repl: true));
        Assert.Contains("'text' is required", runtime.Eval("n = hl.notification.create({ duration = 1000 })", repl: false), StringComparison.Ordinal);
        Assert.Equal("", runtime.Eval("return n:get_text()", repl: true));
        Assert.Equal("aliased", runtime.Eval("return hl.notification.create({ text = 'aliased', duration = 'no', timeout = 5 }):get_text()", repl: true));
        Assert.Contains("timeout must be >= 0", runtime.Eval("n:set_timeout(-1)", repl: false), StringComparison.Ordinal);
        Assert.Contains("read-only", runtime.Eval("n.nope = 1", repl: false), StringComparison.Ordinal);
    }

    [Fact]
    public void PauseResumeAndTwoObjectsShareTheLock()
    {
        var (runtime, core) = Start("");
        using var _runtime = runtime;
        var now = Stopwatch.GetTimestamp();
        core.UseNotificationClock(() => now);

        Assert.Equal("ok", runtime.Eval("n = hl.notification.create({ text = 'p', duration = 60000 })", repl: false));
        now += 2 * Stopwatch.Frequency;
        Assert.Equal("2000", runtime.Eval("return n:get_elapsed()", repl: true));
        Assert.Equal("ok", runtime.Eval("n:pause()", repl: false));
        now += 3 * Stopwatch.Frequency;
        // a pause holds 0 and a resume starts the timeout again.
        Assert.Equal("0\t5000\ttrue", runtime.Eval("return n:get_elapsed(), n:get_elapsed_since_creation(), n:is_paused()", repl: true));
        Assert.Equal("ok", runtime.Eval("n:pause() n:resume()", repl: false));
        Assert.Equal("false", runtime.Eval("return n:is_paused()", repl: true));
        now += Stopwatch.Frequency;
        Assert.Equal("1000", runtime.Eval("return n:get_elapsed()", repl: true));

        Assert.Equal("true\ttrue", runtime.Eval("""
            other = hl.notification.get()[1]
            n:pause()
            other:pause()
            n:resume()
            return other:is_paused(), n:is_paused()
            """, repl: true));
        Assert.Equal("false", runtime.Eval("n:set_paused(false) other:set_paused(false) return n:is_paused()", repl: true));
    }

    [Fact]
    public void DismissExpiresTheObjectAndTwoGoneOnesAreEqual()
    {
        var (runtime, _) = Start("");
        using var _runtime = runtime;

        Assert.Equal("nil\tnil\tfalse\tHL.Notification(expired)", runtime.Eval("""
            n = hl.notification.create({ text = 'a', duration = 5000 })
            n:dismiss()
            return n:get_text(), n:is_paused(), n:is_alive(), tostring(n)
            """, repl: true));
        Assert.Equal("true\tfalse", runtime.Eval("""
            a = hl.notification.create({ text = 'a', duration = 5000 })
            b = hl.notification.create({ text = 'b', duration = 5000 })
            live = hl.notification.create({ text = 'c', duration = 5000 })
            a:dismiss()
            b:dismiss()
            return a == b, a == live
            """, repl: true));
    }

    [Fact]
    public void CollectingAPausedNotificationReleasesItsPause()
    {
        var (runtime, _) = Start("");
        using var _runtime = runtime;

        Assert.Equal("false", runtime.Eval("""
            local held = hl.notification.create({ text = 'g', duration = 60000 })
            local other = hl.notification.get()[1]
            held:pause()
            held = nil
            collectgarbage('collect')
            collectgarbage('collect')
            return other:is_paused()
            """, repl: true));
    }

    [Fact]
    public void ACreateWhileTheConfigLoadsIsShownOnceWinlandRuns()
    {
        var core = new Core();
        var world = new World { Reply = command => core.Execute(command).Text };
        var load = LuaConfig.Load("n = hl.notification.create({ text = 'soon', duration = 60000 })\n", ConfigPath, _ => null);
        Assert.Empty(load.Translation.Warnings);
        var runtime = new LuaRuntime(world);
        using var _runtime = runtime;
        runtime.Adopt(load.Session);

        Assert.Equal("soon", runtime.Eval("return n:get_text()", repl: true));
        Assert.Contains(world.Commands, command => command.StartsWith("notification ", StringComparison.Ordinal));
    }

    [Fact]
    public void NotifyIsListedAndDismissNotifyRemovesIt()
    {
        var (runtime, core) = Start("");
        using var _runtime = runtime;

        core.Execute("notify 1 60000 0 hello");
        Assert.Equal("hello", runtime.Eval("return hl.notification.get()[1]:get_text()", repl: true));
        core.Execute("dismissnotify");
        Assert.Equal("0", runtime.Eval("return #hl.notification.get()", repl: true));
    }

    [Fact]
    public void AReloadDoesNotSeeNotificationsWhileItIsReadAndReleasesAPause()
    {
        var core = new Core();
        var world = new World { Reply = command => core.Execute(command).Text };
        var runtime = new LuaRuntime(world);
        using var _runtime = runtime;
        runtime.Adopt(LuaConfig.Load("""
            n = hl.notification.create({ text = 'kept', duration = 60000 })
            n:pause()
            """, ConfigPath, _ => null).Session);
        Assert.Equal("true", runtime.Eval("return n:is_paused()", repl: true));

        var second = LuaConfig.Load("""
            seen = #hl.notification.get()
            fresh = hl.notification.create({ text = 'fresh', duration = 60000 })
            """, ConfigPath, _ => null);
        runtime.Adopt(second.Session);

        Assert.Equal("0", runtime.Eval("return seen", repl: true));
        Assert.Equal("fresh", runtime.Eval("return fresh:get_text()", repl: true));
        Assert.Equal("false", runtime.Eval("return hl.notification.get()[1]:is_paused()", repl: true));
        Assert.Equal("2", runtime.Eval("return #hl.notification.get()", repl: true));
    }
}
