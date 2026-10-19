using System.Text.Json;

namespace Winland.Tests.Conformance;

/// <summary>The pinned release's group cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class GroupsConformance
{
    [Fact]
    public void GroupDimensions()
    {
        var desk = new Desk();
        desk.Dispatch("workspace", "name:groups");
        desk.Spawn("kittyA");

        Assert.Equal(1, CountAt(desk, "22,22"));
        Assert.Equal(1, CountSize(desk, "1876,1036"));
        Assert.Equal(1, desk.Clients().Count(client => Desk.Fullscreen(client) == 0));

        desk.Spawn("kittyB");
        desk.Ok("focuswindow", "class:kittyB");
        desk.Ok("togglegroup");
        desk.Ok("focuswindow", "class:kittyA");
        desk.Ok("togglegroup");

        Assert.Equal("931,1015", desk.ActivePlace().Size);
        Assert.Equal(0, Desk.Fullscreen(desk.ActiveWindow()));

        desk.Spawn("kittyC");
        Assert.Equal("931,1015", desk.ActivePlace().Size);
        Assert.Equal(0, Desk.Fullscreen(desk.ActiveWindow()));

        desk.Ok("movewindoworgroup", "r");
        Assert.Equal("931,1015", desk.ActivePlace().Size);
        Assert.Equal(0, Desk.Fullscreen(desk.ActiveWindow()));

        KillAll(desk);
        desk.Spawn("a");
        Assert.Single(desk.Clients());
        Assert.Equal(1, CountAt(desk, "22,22"));
        Assert.Equal(1, CountSize(desk, "1876,1036"));
        Assert.Equal(1, desk.Clients().Count(client => Desk.Fullscreen(client) == 0));

        desk.Ok("togglegroup");
        desk.Keyword("group:groupbar:enabled", "1");
        Assert.Equal(("22,43", "1876,1015"), desk.PlaceOf("a"));

        desk.Keyword("group:groupbar:enabled", "0");
        Assert.Equal(("22,22", "1876,1036"), desk.PlaceOf("a"));

        KillAll(desk);
        desk.Spawn("a");
        desk.Ok("togglegroup");
        Assert.Equal(("22,22", "1876,1036"), desk.PlaceOf("a"));
    }

    [Fact]
    public void GroupNext()
    {
        var desk = new Desk();
        desk.Spawn("a");
        desk.Ok("togglegroup");
        desk.Spawn("b");
        Assert.Equal(2, desk.Clients().Length);

        var last = desk.ActiveId();
        desk.Ok("changegroupactive", "f");
        Assert.NotEqual(last, desk.ActiveId());

        desk.Dispatch("changegroupactive", "f");
        Assert.Equal(last, desk.ActiveId());
    }

    [Fact]
    public void GroupMoveWindow()
    {
        var desk = new Desk();
        desk.Spawn("a");
        desk.Ok("togglegroup");
        desk.Spawn("b");
        Assert.Equal(2, desk.Clients().Length);

        var before = desk.ActiveId();
        desk.Ok("movegroupwindow", "f");
        Assert.Equal(before, desk.ActiveId());

        before = desk.ActiveId();
        desk.Ok("movegroupwindow", "b");
        Assert.Equal(before, desk.ActiveId());
    }

    [Fact]
    public void AutoGroupInsertAfterCurrent()
    {
        var desk = new Desk("group:groupbar:enabled = false\n");
        desk.Spawn("a");
        desk.Ok("togglegroup");
        desk.Spawn("b");
        Assert.Equal(2, desk.Clients().Length);

        desk.Keyword("group:auto_group", "false");
        desk.Keyword("dwindle:force_split", "2");
        desk.Spawn("c");
        Assert.Equal(3, desk.Clients().Length);
        Assert.Equal(2, CountAt(desk, "22,22"));

        desk.Keyword("dwindle:force_split", "0");
        desk.Ok("movefocus", "l");
        desk.Ok("changegroupactive", "1");
        desk.Keyword("group:auto_group", "true");
        desk.Keyword("group:insert_after_current", "false");
        var d = desk.Spawn("d");
        Assert.Equal(4, desk.Clients().Length);

        desk.Ok("changegroupactive", "3");
        Assert.Equal(d, desk.ActiveId());
    }

    [Fact]
    public void MoveWindowOrGroup()
    {
        var desk = new Desk("group:groupbar:enabled = false\n");
        desk.Spawn("e");
        desk.Ok("togglegroup");
        desk.Spawn("f");
        Assert.Equal(2, desk.Clients().Length);
        Assert.Equal(2, CountAt(desk, "22,22"));

        desk.Ok("movewindoworgroup", "r");
        Assert.Equal(1, CountAt(desk, "22,22"));

        desk.Ok("moveintogroup", "l");
        desk.Ok("movewindoworgroup", "d");
        Assert.Equal(1, CountAt(desk, "22,22"));
    }

    [Fact]
    public void GroupLock()
    {
        var desk = new Desk("group:groupbar:enabled = false\n");
        desk.Spawn("unlocked");
        desk.Ok("togglegroup");
        desk.Spawn("top");
        Assert.Equal(2, CountAt(desk, "22,22"));

        KillAll(desk);
        Assert.Empty(desk.Clients());

        desk.Spawn("locked");
        desk.Ok("togglegroup");
        desk.Ok("focuswindow", $"pid:{desk.Client("locked").GetProperty("pid").GetInt64()}");
        desk.Ok("lockactivegroup", "lock");
        desk.Spawn("top");
        Assert.Equal(1, CountAt(desk, "22,22"));

        desk.CloseActive();
        Assert.Single(desk.Clients());

        desk.Ok("lockactivegroup", "unlock");
        desk.Ok("lockactivegroup", "lock");
        desk.Spawn("top");
        Assert.Equal(1, CountAt(desk, "22,22"));

        KillAll(desk);
        Assert.Empty(desk.Clients());

        desk.Keyword("windowrulev2", "group set always lock invade, class:^locked|invade$");
        desk.Spawn("locked");
        desk.Spawn("invade");
        Assert.Equal(2, CountAt(desk, "22,22"));
    }

    [Fact]
    public void GroupsNoCrash()
    {
        var desk = new Desk();
        desk.Spawn("kittyA");
        desk.Ok("togglegroup");
        desk.Ok("togglegroup");

        Assert.Equal("kittyA", desk.ActiveClass());
    }

    // The group options the Lua group cases set first.
    private const string LuaGroupOptions = "eval hl.config({ group = { auto_group = false, insert_after_current = false, groupbar = { enabled = false } } })";

    [Fact]
    public void GroupsLuaApi()
    {
        using var desk = Desk.WithLua("");
        LuaOk(desk, "dispatch hl.dsp.focus({ workspace = 'name:groups-lua-api' })");
        LuaOk(desk, LuaGroupOptions);

        desk.Spawn("luaGroupA");
        desk.Spawn("luaGroupB");
        desk.Spawn("luaGroupC");
        desk.Spawn("luaGroupD");
        desk.Spawn("luaGroupE");
        Assert.Equal(5, desk.Clients().Length);

        LuaOk(desk, "dispatch hl.dsp.focus({ window = 'class:luaGroupA' })");
        LuaOk(desk, "dispatch hl.dsp.group.toggle()");

        LuaOk(desk, "eval local a=hl.get_window('class:luaGroupA') local b=hl.get_window('class:luaGroupB') local c=hl.get_window('class:luaGroupC') assert(a and b and c and "
            + "a.group) local g=a.group g:add(b) assert(b.group == g) assert(g.size == 2) g:add(c, 1) assert(c.group == g) assert(g.members[1] == c) assert(g.size == 3) "
            + "g:remove(1) assert(c.group == nil) assert(g.size == 2) g:remove(b) assert(b.group == nil) assert(g.size == 1)");

        LuaOk(desk, "dispatch hl.dsp.focus({ window = 'class:luaGroupD' })");
        LuaOk(desk, "dispatch hl.dsp.group.toggle()");

        LuaOk(desk, "eval local a=hl.get_window('class:luaGroupA') local d=hl.get_window('class:luaGroupD') local e=hl.get_window('class:luaGroupE') assert(a and d and e and "
            + "a.group and d.group) local g=a.group local source=d.group source:add(e) assert(source.size == 2) g:add(d, 1) assert(d.group == g) assert(e.group == g) "
            + "assert(g.size == 3) assert(g.members[1] == d) assert(g.members[2] == e) assert(g.members[3] == a)");

        desk.CloseAll();
        Assert.Empty(desk.Clients());
    }

    [Fact]
    public void GroupsLuaApiInvalidOps()
    {
        using var desk = Desk.WithLua("");
        LuaOk(desk, "dispatch hl.dsp.focus({ workspace = 'name:groups-lua-invalid' })");
        LuaOk(desk, LuaGroupOptions);

        desk.Spawn("luaInvalidA");
        desk.Spawn("luaInvalidB");
        desk.Spawn("luaInvalidC");
        Assert.Equal(3, desk.Clients().Length);

        LuaOk(desk, "dispatch hl.dsp.focus({ window = 'class:luaInvalidA' })");
        LuaOk(desk, "dispatch hl.dsp.group.toggle()");

        LuaOk(desk, "eval local a=hl.get_window('class:luaInvalidA') local b=hl.get_window('class:luaInvalidB') local c=hl.get_window('class:luaInvalidC') local g=a.group g:add(b) "
            + "assert(g.size == 2 and b.group == g and c.group == nil)");

        LuaNotOk(desk, "eval local a=hl.get_window('class:luaInvalidA') local c=hl.get_window('class:luaInvalidC') a.group:add(c, 0)");
        LuaNotOk(desk, "eval local a=hl.get_window('class:luaInvalidA') local c=hl.get_window('class:luaInvalidC') a.group:add(c, 4)");
        LuaNotOk(desk, "eval local a=hl.get_window('class:luaInvalidA') a.group:remove(0)");
        LuaNotOk(desk, "eval local a=hl.get_window('class:luaInvalidA') a.group:remove(3)");
        LuaNotOk(desk, "eval local a=hl.get_window('class:luaInvalidA') local c=hl.get_window('class:luaInvalidC') a.group:remove(c)");

        LuaOk(desk, "eval local a=hl.get_window('class:luaInvalidA') local b=hl.get_window('class:luaInvalidB') local c=hl.get_window('class:luaInvalidC') local g=a.group "
            + "assert(g.size == 2 and b.group == g and c.group == nil) local first, second = g.members[1], g.members[2] g:add(b, 1) "
            + "assert(g.size == 2 and g.members[1] == first and g.members[2] == second)");

        desk.CloseAll();
        Assert.Empty(desk.Clients());
    }

    [Fact]
    public void GroupsLuaApiCrossWorkspaceMonitor()
    {
        using var desk = Desk.WithLua("");
        LuaOk(desk, LuaGroupOptions);

        LuaOk(desk, "dispatch hl.dsp.focus({ workspace = 'name:groups-lua-ws-a' })");
        desk.Spawn("luaWorkspaceA");
        LuaOk(desk, "dispatch hl.dsp.group.toggle()");
        LuaOk(desk, "dispatch hl.dsp.focus({ workspace = 'name:groups-lua-ws-b' })");
        desk.Spawn("luaWorkspaceB");

        LuaOk(desk, "eval local a=hl.get_window('class:luaWorkspaceA') local b=hl.get_window('class:luaWorkspaceB') assert(a and b and a.group) local g=a.group g:add(b) "
            + "assert(b.group == g) assert(b.workspace == a.workspace) assert(b.monitor == a.monitor) assert(g.size == 2)");

        desk.CloseAll();
        Assert.Empty(desk.Clients());

        desk.AddMonitor("HEADLESS-LUA-GROUP", Desk.Second);
        LuaOk(desk, "eval hl.monitor({ output = 'HEADLESS-LUA-GROUP', disabled = false, mode = '1920x1080@60', position = '1920x0', scale = '1' })");
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-2' })");
        LuaOk(desk, "dispatch hl.dsp.focus({ workspace = 'name:groups-lua-monitor-a' })");
        desk.Spawn("luaMonitorA");
        LuaOk(desk, "dispatch hl.dsp.group.toggle()");
        LuaOk(desk, "dispatch hl.dsp.focus({ monitor = 'HEADLESS-LUA-GROUP' })");
        LuaOk(desk, "dispatch hl.dsp.focus({ workspace = 'name:groups-lua-monitor-b' })");
        desk.Spawn("luaMonitorB");

        LuaOk(desk, "eval local a=hl.get_window('class:luaMonitorA') local b=hl.get_window('class:luaMonitorB') assert(a and b and a.group) local g=a.group g:add(b) "
            + "assert(b.group == g) assert(b.workspace == a.workspace) assert(b.monitor == a.monitor) assert(g.size == 2)");

        desk.CloseAll();
        Assert.Empty(desk.Clients());
        desk.RemoveMonitor("HEADLESS-LUA-GROUP");
    }

    [Fact]
    public void GroupsLuaApiFullscreen()
    {
        using var desk = Desk.WithLua("");
        LuaOk(desk, "dispatch hl.dsp.focus({ workspace = 'name:groups-lua-fullscreen' })");
        LuaOk(desk, LuaGroupOptions);

        desk.Spawn("luaFullscreenGroupA");
        desk.Spawn("luaFullscreenSourceB");
        LuaOk(desk, "dispatch hl.dsp.focus({ window = 'class:luaFullscreenGroupA' })");
        LuaOk(desk, "dispatch hl.dsp.group.toggle()");
        LuaOk(desk, "dispatch hl.dsp.focus({ window = 'class:luaFullscreenSourceB' })");
        LuaOk(desk, "dispatch hl.dsp.window.fullscreen({ mode = 'fullscreen', action = 'set' })");

        LuaOk(desk, "eval local a=hl.get_window('class:luaFullscreenGroupA') local b=hl.get_window('class:luaFullscreenSourceB') assert(a and b and a.group and b.fullscreen == 2) "
            + "local g=a.group g:add(b) assert(b.group == g) assert(b.fullscreen == 0) assert(a.fullscreen == 0) assert(g.size == 2)");

        desk.CloseAll();
        Assert.Empty(desk.Clients());

        LuaOk(desk, "dispatch hl.dsp.focus({ workspace = 'name:groups-lua-fullscreen-target' })");
        desk.Spawn("luaFullscreenTargetC");
        desk.Spawn("luaFullscreenUnderD");
        LuaOk(desk, "dispatch hl.dsp.focus({ window = 'class:luaFullscreenTargetC' })");
        LuaOk(desk, "dispatch hl.dsp.group.toggle()");
        LuaOk(desk, "dispatch hl.dsp.window.fullscreen({ mode = 'fullscreen', action = 'set' })");

        LuaOk(desk, "eval local c=hl.get_window('class:luaFullscreenTargetC') local d=hl.get_window('class:luaFullscreenUnderD') assert(c and d and c.group and c.fullscreen == "
            + "2 and d.fullscreen == 0) "
            + "local g=c.group g:add(d) assert(d.group == g) assert(d.fullscreen == 2) assert(c.fullscreen == 0) assert(g.size == 2)");

        desk.CloseAll();
        Assert.Empty(desk.Clients());
    }

    private static void LuaOk(Desk desk, string line) => Assert.Equal("ok", desk.Send(line));

    private static void LuaNotOk(Desk desk, string line) => Assert.NotEqual("ok", desk.Send(line));

    [Fact]
    public void GroupsDisableWhenOnly()
    {
        var desk = new Desk();
        desk.Spawn("kittyA");
        Assert.Single(desk.Clients());
        desk.Ok("focuswindow", "class:kittyA");
        desk.ExpectActive("22,22", "1876,1036");

        desk.Ok("togglegroup");
        desk.Keyword("group:groupbar:disable_when_only", "true");
        desk.ExpectActive("22,22", "1876,1036");

        desk.Spawn("kittyB");
        Assert.Equal(2, desk.Clients().Length);
        desk.Ok("focuswindow", "class:kittyB");
        desk.ExpectActive("22,43", "1876,1015");

        desk.CloseActive();
        Assert.Single(desk.Clients());
        desk.Ok("focuswindow", "class:kittyA");
        desk.ExpectActive("22,22", "1876,1036");

        desk.Keyword("group:groupbar:disable_when_only", "false");
        desk.ExpectActive("22,43", "1876,1015");
    }

    [Fact]
    public void GroupbarDisableWhenOnlyDimensions()
    {
        var desk = new Desk("group:groupbar:disable_when_only = true\n");
        desk.Spawn("kittenA");
        desk.Ok("togglegroup");
        desk.ExpectActive("22,22", "1876,1036");

        // The first member gets its tab bar when a second one joins.
        desk.Spawn("kittenB");
        Assert.Equal(("22,43", "1876,1015"), desk.PlaceOf("kittenA"));

        // And loses it when the other member leaves.
        desk.Ok("moveoutofgroup");
        Assert.Equal(("22,22", "931,1036"), desk.PlaceOf("kittenA"));
    }

    [Fact]
    public void AutoGroupFloatedIntoTiled()
    {
        var desk = new Desk("""
            windowrulev2 = tile, class:kitty_tiled
            windowrulev2 = float, class:kitty_floated
            """);
        desk.Spawn("kitty_tiled");
        desk.Ok("togglegroup");
        desk.Spawn("kitty_floated");
        Assert.Equal(2, desk.Clients().Length);

        var floated = desk.Client("kitty_floated");
        Assert.True(Desk.Floating(floated));
        Assert.Equal(0, Grouped(floated));
    }

    [Fact]
    public void AutoGroupFloatedintoFloated()
    {
        var desk = new Desk("""
            windowrulev2 = float, class:kitty_floated_A
            windowrulev2 = float, class:kitty_floated_B
            """);
        desk.Spawn("kitty_floated_A");
        desk.Ok("togglegroup");
        desk.Spawn("kitty_floated_B");
        Assert.Equal(2, desk.Clients().Length);

        var b = desk.Client("kitty_floated_B");
        Assert.True(Desk.Floating(b));
        Assert.NotEqual(0, Grouped(b));
    }

    [Fact]
    public void AutoGroupTiledIntoFloated()
    {
        var desk = new Desk("""
            windowrulev2 = float, class:kitty_floated_A
            windowrulev2 = tile, class:kitty_tiled_B
            """);
        desk.Spawn("kitty_floated_A");
        Assert.True(Desk.Floating(desk.ActiveWindow()));

        desk.Ok("togglegroup");
        desk.Spawn("kitty_tiled_B");
        Assert.Equal(2, desk.Clients().Length);

        var b = desk.Client("kitty_tiled_B");
        Assert.True(Desk.Floating(b));
        Assert.NotEqual(0, Grouped(b));
    }

    [Fact]
    public void GroupedExitWindowRetainsFullscreen()
    {
        var desk = new Desk("misc:exit_window_retains_fullscreen = 3\n");
        desk.Spawn("kitty_A");
        desk.Ok("togglegroup");
        desk.Spawn("kitty_B");

        desk.Ok("fullscreen", "0");
        ExpectFullscreen(desk, 2, 2);

        desk.CloseActive();
        Assert.Single(desk.Clients());
        ExpectFullscreen(desk, 0, 0);

        desk.Spawn("kitty_B");
        desk.Ok("fullscreen", "0");
        desk.Keyword("misc:exit_window_retains_fullscreen", "2");

        desk.CloseActive();
        Assert.Single(desk.Clients());
        ExpectFullscreen(desk, 2, 2);
    }

    // How many clients are placed at x,y, as the reference counts "at: x,y" in its clients reply.
    private static int CountAt(Desk desk, string at) =>
        desk.Clients().Count(client => desk.PlaceOf(Desk.ClassOf(client)).At == at);

    private static int CountSize(Desk desk, string size) =>
        desk.Clients().Count(client => desk.PlaceOf(Desk.ClassOf(client)).Size == size);

    private static int Grouped(JsonElement client) => client.GetProperty("grouped").GetArrayLength();

    // Closes every window, as the reference harness kills every kitty.
    private static void KillAll(Desk desk)
    {
        foreach (var client in desk.Clients())
        {
            var address = client.GetProperty("address").GetString() ?? "";
            desk.Handle(new WindowClosed(new WindowId(Convert.ToInt64(address[2..], 16))));
        }
    }

    private static void ExpectFullscreen(Desk desk, int fullscreen, int fullscreenClient)
    {
        var active = desk.ActiveWindow();
        Assert.Equal(fullscreen, Desk.Fullscreen(active));
        Assert.Equal(fullscreenClient, Desk.FullscreenClient(active));
    }
}
