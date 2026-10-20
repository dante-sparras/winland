using System.Collections.Concurrent;
using System.Text.Json;
using Winland;
using Winland.Lua;

namespace Winland.Lua.Tests;

public class LuaEventTests
{
    private static readonly string ConfigPath = Path.Combine(Path.GetTempPath(), "winland-lua-events", "winland.lua");

    private static readonly WindowId Term = new(0x1a);
    private static readonly WindowId Browser = new(0x2b);
    private static readonly WindowId Editor = new(0x3c);

    // A desktop as Winland's query replies describe it: two monitors, workspaces 1, "web" and the special "scratch",
    // a group of a terminal and a browser on 1, a fullscreen editor on "web" and a minimized note on "scratch".
    private const string Clients = """
        [
          {"address":"0x1a","mapped":true,"hidden":false,"visible":true,"acceptsInput":true,"at":[10,20],"size":[800,600],
           "workspace":{"address":"1","id":1,"type":"normal","name":"1"},"floating":false,"monitor":0,"class":"kitty",
           "title":"term","initialClass":"kitty","initialTitle":"shell","pid":100,"xwayland":false,"pinned":false,
           "pinFullscreened":false,"fullscreen":0,"fullscreenClient":0,"fullscreenHandler":"default",
           "allowedOverFullscreen":false,"grouped":["0x1a","0x2b"],"tags":["dev"],"swallowing":"0x0","focusHistoryID":0,
           "inhibitingIdle":false,"xdgTag":"","xdgDescription":"","contentType":"none","tearingHint":false,"stableId":"1f"},
          {"address":"0x2b","mapped":false,"hidden":true,"visible":false,"acceptsInput":false,"at":[10,20],"size":[800,600],
           "workspace":{"address":"1","id":1,"type":"normal","name":"1"},"floating":false,"monitor":0,"class":"firefox",
           "title":"Mozilla","initialClass":"firefox","initialTitle":"Mozilla","pid":200,"xwayland":false,"pinned":false,
           "pinFullscreened":false,"fullscreen":0,"fullscreenClient":0,"fullscreenHandler":"default",
           "allowedOverFullscreen":false,"grouped":["0x1a","0x2b"],"tags":[],"swallowing":"0x0","focusHistoryID":0,
           "inhibitingIdle":false,"xdgTag":"","xdgDescription":"","contentType":"none","tearingHint":false,"stableId":"20"},
          {"address":"0x3c","mapped":true,"hidden":false,"visible":true,"acceptsInput":true,"at":[1920,0],"size":[2560,1440],
           "workspace":{"address":"web","type":"normal","name":"web"},"floating":true,"monitor":1,"class":"code",
           "title":"editor","initialClass":"code","initialTitle":"editor","pid":300,"xwayland":false,"pinned":true,
           "pinFullscreened":false,"fullscreen":1,"fullscreenClient":1,"fullscreenHandler":"default",
           "allowedOverFullscreen":false,"grouped":[],"tags":["dev","work"],"swallowing":"0x1a","focusHistoryID":0,
           "inhibitingIdle":true,"xdgTag":"tag","xdgDescription":"","contentType":"video","tearingHint":false,"stableId":"21"},
          {"address":"0x4d","mapped":false,"hidden":true,"visible":false,"acceptsInput":false,"at":[0,0],"size":[300,200],
           "workspace":{"address":"special:scratch","type":"special","name":"special:scratch"},"floating":false,"monitor":0,
           "class":"notes","title":"todo","initialClass":"notes","initialTitle":"todo","pid":400,"xwayland":false,
           "pinned":false,"pinFullscreened":false,"fullscreen":0,"fullscreenClient":0,"fullscreenHandler":"default",
           "allowedOverFullscreen":false,"grouped":[],"tags":[],"swallowing":"0x0","focusHistoryID":0,"inhibitingIdle":false,
           "xdgTag":"","xdgDescription":"","contentType":"none","tearingHint":false,"stableId":"22"}
        ]
        """;

    private const string Workspaces = """
        [
          {"address":"special:scratch","type":"special","name":"special:scratch","monitor":"DP-1","monitorID":0,"windows":1,
           "hasfullscreen":false,"lastwindow":"0x0","lastwindowtitle":"","ispersistent":false,"tiledLayout":"dwindle"},
          {"address":"web","type":"normal","name":"web","monitor":"HDMI-A-1","monitorID":1,"windows":1,"hasfullscreen":true,
           "lastwindow":"0x3c","lastwindowtitle":"editor","ispersistent":true,"tiledLayout":"master"},
          {"address":"1","id":1,"type":"normal","name":"1","monitor":"DP-1","monitorID":0,"windows":2,"hasfullscreen":false,
           "lastwindow":"0x1a","lastwindowtitle":"term","ispersistent":false,"tiledLayout":"dwindle"}
        ]
        """;

    private const string Monitors = """
        [
          {"id":0,"name":"DP-1","description":"Dell U2720Q","make":"Dell","model":"U2720Q","serial":"ABC","width":1920,
           "height":1080,"physicalWidth":600,"physicalHeight":340,"refreshRate":60,"x":0,"y":0,
           "activeWorkspace":{"address":"1","id":1,"type":"normal","name":"1"},
           "specialWorkspace":{"address":"special:scratch","type":"special","name":"special:scratch"},"reserved":[0,0,0,48],
           "scale":1,"transform":0,"focused":true,"dpmsStatus":true,"vrr":false,"solitary":"0","solitaryBlockedBy":null,
           "activelyTearing":false,"tearingBlockedBy":null,"directScanoutTo":"0","directScanoutBlockedBy":null,
           "disabled":false,"currentFormat":"","mirrorOf":"none","availableModes":["1920x1080@60.00Hz"],
           "colorManagementPreset":"srgb","sdrBrightness":0,"sdrSaturation":0,"sdrMinLuminance":0,"sdrMaxLuminance":0,
           "hardwareCursorsInUse":false},
          {"id":1,"name":"HDMI-A-1","description":"LG 27GL850","make":"LG","model":"27GL850","serial":"XYZ","width":2560,
           "height":1440,"physicalWidth":600,"physicalHeight":340,"refreshRate":143.998,"x":1920,"y":0,
           "activeWorkspace":{"address":"web","type":"normal","name":"web"},
           "specialWorkspace":{"address":"","type":"","name":""},"reserved":[0,0,0,0],"scale":1.25,"transform":0,
           "focused":false,"dpmsStatus":true,"vrr":true,"solitary":"0","solitaryBlockedBy":null,"activelyTearing":false,
           "tearingBlockedBy":null,"directScanoutTo":"0","directScanoutBlockedBy":null,"disabled":false,"currentFormat":"",
           "mirrorOf":"none","availableModes":[],"colorManagementPreset":"srgb","sdrBrightness":0,"sdrSaturation":0,
           "sdrMinLuminance":0,"sdrMaxLuminance":0,"hardwareCursorsInUse":false}
        ]
        """;

    // A world that answers the queries with the desktop above, records every command and runs what a dispatch does.
    private sealed class World : ILuaWorld
    {
        public ConcurrentQueue<string> Commands { get; } = new();

        public ConcurrentQueue<string> Logged { get; } = new();

        public Dictionary<string, string> Replies { get; } = new(StringComparer.Ordinal)
        {
            ["ja/clients"] = Clients,
            ["j/workspaces"] = Workspaces,
            ["j/monitors"] = Monitors,
            ["j/activewindow"] = JsonDocument.Parse(Clients).RootElement[0].GetRawText(),
            ["j/cursorpos"] = """{"x":2000,"y":10}""",
            ["j/submap"] = "\"resize\"\n",
            ["getoption general:gaps_in"] = """{"option":"general:gaps_in","css":"5 5 5 5","set":true}""",
            ["getoption general:border_size"] = """{"option":"general:border_size","int":2,"set":false}""",
            ["getoption decoration:active_opacity"] = """{"option":"decoration:active_opacity","float":0.9,"set":true}""",
            ["plugin list"] = """[{"name":"demo","version":3,"state":"loaded","failures":0}]""",
        };

        public Action<string>? OnDispatch { get; set; }

        public string Command(string command)
        {
            Commands.Enqueue(command);
            if (command.StartsWith("dispatch ", StringComparison.Ordinal))
            {
                OnDispatch?.Invoke(command);
                return "ok";
            }

            if (command.StartsWith("j/resolveworkspace ", StringComparison.Ordinal))
                return Resolve(command.Split(' ', 3)[2]);

            lock (Replies)
                return Replies.TryGetValue(command, out var reply) ? reply : command.StartsWith("getoption ", StringComparison.Ordinal) ? "no such option" : "ok";
        }

        // resolveworkspace as the core answers it for the workspaces above: the address of the one the selector names.
        private static string Resolve(string selector)
        {
            var address = selector switch
            {
                "special" => "special:special",
                _ when selector.StartsWith("name:", StringComparison.Ordinal) => selector[5..],
                _ => selector,
            };
            return address is "1" or "web" or "special:scratch" ? JsonSerializer.Serialize(address) : "null";
        }

        public void Log(string message) => Logged.Enqueue(message);
    }

    private static (LuaRuntime Runtime, World World) Start(string code)
    {
        var world = new World();
        var runtime = new LuaRuntime(world);
        var load = LuaConfig.Load(code, ConfigPath, _ => null);
        Assert.Empty(load.Translation.Warnings);
        runtime.Adopt(load.Session);
        return (runtime, world);
    }

    // What an expression gives, once all that is queued before it has run.
    private static string Repl(LuaRuntime runtime, string code) => runtime.Eval(code, repl: true);

    private static void Check(LuaRuntime runtime, string code) => Assert.Equal("ok", runtime.Eval(code, repl: false));

    // An event a handler raises is queued behind what was queued when it ran, so this waits for what follows from it.
    private static string Eventually(LuaRuntime runtime, string code, string expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var value = Repl(runtime, code);
        while (value != expected && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
            value = Repl(runtime, code);
        }

        return value;
    }

    [Fact]
    public void AnEventCallsItsHandlersInOrderWithItsArguments()
    {
        var (runtime, _) = Start("""
            seen = {}
            hl.on("window.open", function(w) seen[#seen + 1] = "first " .. w.class .. " " .. w.title end)
            hl.on("window.open", function(w) seen[#seen + 1] = "second " .. w.address end)
            hl.on("window.move_to_workspace", function(w, ws) seen[#seen + 1] = w.class .. " to " .. ws.name end)
            hl.on("window.active", function(w, reason) seen[#seen + 1] = "active " .. tostring(w and w.title) .. " " .. reason end)
            hl.on("workspace.special_active", function(ws, m) seen[#seen + 1] = tostring(ws and ws.name) .. " on " .. m.name end)
            hl.on("keybinds.submap", function(name) seen[#seen + 1] = "submap '" .. name .. "'" end)
            hl.on("config.reloaded", function(...) seen[#seen + 1] = "reloaded " .. select("#", ...) end)
            """);
        using var _ = runtime;

        runtime.Deliver([
            new ClientOpened(Term, WorkspaceId.FromNumber(1), "kitty", "term"),
            new ClientMoved(Editor, new WorkspaceId("web")),
            new FocusChanged(null),
            new ActiveSpecial(null, "DP-1"),
            new SubmapChanged(""),
            new ConfigReloaded(),
        ]);

        Assert.Equal(
            "first kitty term,second 0x1a,code to web,active nil 0,nil on DP-1,submap '',reloaded 0",
            Repl(runtime, "table.concat(seen, ',')"));
    }

    [Fact]
    public void WindowActiveGetsTheFocusReason()
    {
        var (runtime, _) = Start("""
            seen = ""
            hl.on("window.active", function(w, reason) seen = tostring(w) .. " " .. reason end)
            """);
        using var _ = runtime;

        runtime.Deliver([new FocusChanged(Editor) { Reason = FocusReason.FocusWindow }]);

        Assert.Equal("HL.Window(0x3c) 4", Repl(runtime, "seen"));
    }

    [Fact]
    public void ANewTitleRunsTheWindowTitleHandler()
    {
        var (runtime, _) = Start("""
            seen = ""
            hl.on("window.title", function(w) seen = tostring(w) .. " " .. w.title end)
            """);
        using var _ = runtime;

        runtime.Deliver([new ClientRetitled(Term, "new", true)]);

        Assert.Equal("HL.Window(0x1a) term", Repl(runtime, "seen"));
    }

    [Fact]
    public void ANewClassRunsTheWindowClassHandler()
    {
        var (runtime, _) = Start("""
            seen = ""
            hl.on("window.class", function(w) seen = tostring(w) .. " " .. w.class end)
            """);
        using var _ = runtime;

        runtime.Deliver([new ClientReclassed(Term, "foot", true)]);

        Assert.Equal("HL.Window(0x1a) kitty", Repl(runtime, "seen"));
    }

    [Fact]
    public void ARulesUpdateRunsTheWindowUpdateRulesHandler()
    {
        var (runtime, _) = Start("""
            seen = ""
            hl.on("window.update_rules", function(w) seen = tostring(w) end)
            """);
        using var _ = runtime;

        runtime.Deliver([new ClientRulesUpdated(Term)]);

        Assert.Equal("HL.Window(0x1a)", Repl(runtime, "seen"));
    }

    [Fact]
    public void WinlandsEventsAreThePinnedReleasesEvents()
    {
        var (runtime, _) = Start("""
            seen = {}
            for _, name in ipairs({ "window.open", "window.close", "window.destroy", "window.kill", "window.active",
                "window.urgent", "window.pin", "window.fullscreen", "window.move_to_workspace", "monitor.added",
                "monitor.removed", "monitor.focused", "monitor.layout_changed", "workspace.active",
                "workspace.special_active", "workspace.created", "workspace.removed", "workspace.move_to_monitor",
                "config.reloaded", "keybinds.submap", "window.title", "window.class", "window.open_early",
                "window.update_rules", "layer.opened", "layer.closed", "config.props_refreshed", "screenshare.state",
                "input.keyboard.key", "window.minimize", "window.bell", "config.unload" }) do
                hl.on(name, function(...)
                    local parts = { name }
                    for i = 1, select("#", ...) do
                        local value = select(i, ...)
                        parts[#parts + 1] = type(value) == "userdata" and tostring(value) or tostring(value)
                    end
                    seen[#seen + 1] = table.concat(parts, " ")
                end)
            end
            """);
        using var _ = runtime;

        runtime.Deliver([
            new ClientOpened(Term, WorkspaceId.FromNumber(1), "kitty", "term"),
            new ClientClosed(new WindowId(0x99)),
            new ClientKilled(Term),
            new FocusChanged(Editor),
            new ClientUrgent(Term),
            new ClientPinned(Editor, true),
            new ClientMinimized(Term, true),
            new FullscreenChanged(true, Editor),
            new MonitorAdded("DP-1"),
            new MonitorAddedV2(0, "DP-1", "Dell"),
            new MonitorRemovedV2(5, "DP-9", "Gone"),
            new MonitorLayoutChanged(),
            new MonitorFocused("HDMI-A-1", new WorkspaceId("web")),
            new WorkspaceActivated(WorkspaceId.FromNumber(1)),
            new ActiveSpecial(new WorkspaceId("special:scratch"), "DP-1"),
            new WorkspaceCreated(new WorkspaceId("web")),
            new WorkspaceDestroyed(WorkspaceId.FromNumber(7), 7, "7"),
            new WorkspaceMoved(new WorkspaceId("web"), "HDMI-A-1"),
            new WorkspaceRenamed(WorkspaceId.FromNumber(1), "main"),
            new FloatingChanged(Term, true),
            new GroupsLocked(true),
        ]);

        Assert.Equal(
            string.Join('\n',
                "window.open_early HL.Window(0x1a)",
                "window.open HL.Window(0x1a)",
                "window.close HL.Window(expired)",
                "window.destroy HL.Window(expired)",
                "window.kill HL.Window(0x1a)",
                "window.active HL.Window(0x3c) 0",
                "window.urgent HL.Window(0x1a)",
                "window.pin HL.Window(0x3c)",
                "window.minimize HL.Window(0x1a) true",
                "window.fullscreen HL.Window(0x3c)",
                "monitor.added HL.Monitor(0:DP-1)",
                "monitor.removed HL.Monitor(expired)",
                "monitor.layout_changed",
                "monitor.focused HL.Monitor(1:HDMI-A-1)",
                "workspace.active HL.Workspace(1:1)",
                "workspace.special_active HL.Workspace(special:scratch:special:scratch) HL.Monitor(0:DP-1)",
                "workspace.created HL.Workspace(web:web)",
                "workspace.removed HL.Workspace(expired)",
                "workspace.move_to_monitor HL.Workspace(web:web) HL.Monitor(1:HDMI-A-1)"),
            Repl(runtime, "table.concat(seen, '\\n')"));
    }

    [Fact]
    public void AKeyRunsTheKeyboardKeyHandlerWithItsCodeTimeAndState()
    {
        var (runtime, _) = Start("""
            seen = {}
            hl.on("input.keyboard.key", function(code, time, state) seen[#seen + 1] = code .. " " .. time .. " " .. state end)
            """);
        using var _ = runtime;

        runtime.Key(38, 1000, pressed: true);
        runtime.Key(38, 1090, pressed: false);

        Assert.Equal("38 1000 1,38 1090 0", Repl(runtime, "table.concat(seen, ',')"));
    }

    [Fact]
    public void AReloadRefreshesThePropsAfterItIsRead()
    {
        var (runtime, _) = Start("""
            seen = {}
            hl.on("config.reloaded", function() seen[#seen + 1] = "reloaded" end)
            hl.on("config.props_refreshed", function(scheduled) seen[#seen + 1] = "refreshed " .. tostring(scheduled) end)
            """);
        using var _ = runtime;

        runtime.Deliver([new ConfigReloaded()]);

        Assert.Equal("reloaded,refreshed true", Repl(runtime, "table.concat(seen, ',')"));
    }

    [Fact]
    public void AChangeFromLuaRefreshesThePropsOnceWhenTheCallReturns()
    {
        var (runtime, world) = Start("""
            seen = {}
            hl.on("config.props_refreshed", function(scheduled)
                seen[#seen + 1] = "refreshed " .. tostring(scheduled)
                hl.config({ general = { gaps_out = 1 } })
            end)
            function change(now)
                hl.config({ general = { gaps_in = 1 } })
                hl.config({ general = { gaps_in = 2 } })
                seen[#seen + 1] = "changed"
                if now then
                    hl.exec_scheduled_prop_refresh_immediately()
                    seen[#seen + 1] = "after"
                end
            end
            """);
        using var _ = runtime;

        Check(runtime, "change(false)");
        Assert.Equal("changed,refreshed true", Repl(runtime, "table.concat(seen, ',')"));

        Check(runtime, "seen = {} change(true)");
        Assert.Equal("changed,refreshed false,after", Repl(runtime, "table.concat(seen, ',')"));
        Assert.Contains("keyword general:gaps_in 2", world.Commands);
    }

    [Fact]
    public void AHandlerThatFailsOrRunsTooLongIsLoggedAndTheOthersRun()
    {
        var (runtime, world) = Start("""
            count = 0
            hl.on("window.urgent", function() error("boom") end)
            hl.on("window.urgent", function() while true do end end)
            hl.on("window.urgent", function() count = count + 1 end)
            """);
        using var _ = runtime;

        runtime.Deliver([new ClientUrgent(Term)]);

        Assert.Equal("1", Repl(runtime, "count"));
        Assert.Contains(world.Logged, line => line.StartsWith("lua: hl.on(\"window.urgent\") callback: ", StringComparison.Ordinal) && line.EndsWith("boom", StringComparison.Ordinal));
        Assert.Contains(world.Logged, line => line.Contains("hl.on(\"window.urgent\") callback ran longer than 0.05 seconds", StringComparison.Ordinal));
    }

    [Fact]
    public void ARemovedSubscriptionIsNotCalled()
    {
        var (runtime, _) = Start("""
            count = 0
            subscription = hl.on("window.pin", function() count = count + 1 end)
            """);
        using var _ = runtime;

        runtime.Deliver([new ClientPinned(Term, true)]);
        Assert.Equal("1\tHL.EventSubscription(1,active)\ttrue", Repl(runtime, "count, tostring(subscription), subscription:is_active()"));

        Check(runtime, "subscription:remove() subscription:remove()");
        runtime.Deliver([new ClientPinned(Term, false)]);

        Assert.Equal("1\tHL.EventSubscription(1,inactive)\tfalse", Repl(runtime, "count, tostring(subscription), subscription:is_active()"));
    }

    [Fact]
    public void AnEventAHandlersDispatchRaisesRunsAfterItAndNotInItAgain()
    {
        var (runtime, world) = Start("""
            seen = {}
            hl.on("window.active", function(w)
                seen[#seen + 1] = "a " .. w.address
                hl.dispatch(hl.dsp.focus({ window = hl.get_window("class:code") }))
                seen[#seen + 1] = "a done"
            end)
            hl.on("window.active", function(w) seen[#seen + 1] = "b " .. w.address end)
            """);
        using var _ = runtime;
        world.OnDispatch = command => runtime.Deliver([new FocusChanged(Editor)]);

        runtime.Deliver([new FocusChanged(Term)]);

        Assert.Equal("a 0x1a,a done,b 0x1a,b 0x3c", Eventually(runtime, "table.concat(seen, ',')", "a 0x1a,a done,b 0x1a,b 0x3c"));
        Assert.Contains("dispatch focuswindow address:0x3c", world.Commands);
        Assert.Single(world.Logged, line => line == "lua: suppressed recursive hl.on(\"window.active\") callback invocation");
    }

    [Fact]
    public void HandlersThatRaiseEachOthersEventsStopAtTheDepthLimit()
    {
        var (runtime, world) = Start("""
            reached = 0
            for k = 1, 40 do
                hl.on("keybinds.submap", function(name)
                    if name == "s" .. k then
                        reached = k
                        hl.dispatch(hl.dsp.submap("s" .. (k + 1)))
                    end
                end)
            end
            """);
        using var _ = runtime;
        world.OnDispatch = command => runtime.Deliver([new SubmapChanged(command["dispatch submap ".Length..])]);

        runtime.Deliver([new SubmapChanged("s1")]);

        Assert.Equal("32", Eventually(runtime, "reached", "32"));
        Assert.Equal("32", Repl(runtime, "reached"));
        Assert.Contains("lua: max dispatch depth (32) reached while handling 'keybinds.submap'", world.Logged);
    }

    [Fact]
    public void WithoutALuaConfigEventsGoNowhere()
    {
        var world = new World();
        using var runtime = new LuaRuntime(world);

        runtime.Deliver([new ClientUrgent(Term)]);

        Assert.Equal("error: lua state not initialized", runtime.Eval("1", repl: true));
        Assert.Empty(world.Commands);
    }

    [Fact]
    public void AWindowReadsItsFieldsFromTheClientsReply()
    {
        var (runtime, _) = Start("");
        using var _r = runtime;

        Check(runtime, """
            local w = hl.get_window("address:0x1a")
            assert(type(w) == "userdata" and tostring(w) == "HL.Window(0x1a)")
            assert(w.address == "0x1a" and w.mapped == true and w.hidden == false and w.visible == true and w.accepts_input)
            assert(w.at.x == 10 and w.at.y == 20 and w.size.x == 800 and w.size.y == 600)
            assert(w.workspace == hl.get_workspace(1) and w.monitor.name == "DP-1" and w.floating == false)
            assert(w.class == "kitty" and w.title == "term" and w.initial_class == "kitty" and w.initial_title == "shell")
            assert(w.pid == 100 and w.xwayland == false and w.pinned == false and w.pin_fullscreened == false)
            assert(w.fullscreen == 0 and w.fullscreen_client == 0 and w.allowed_over_fullscreen == false)
            assert(w.fullscreen_handler == "default" and w.tags[1] == "dev" and w.swallowing == nil)
            assert(w.inhibiting_idle == false and w.xdg_tag == nil and w.xdg_description == nil and w.content_type == "none")
            assert(w.stable_id == "1f" and w.layout.name == "dwindle")
            assert(w.active == true and w.tearing_hint == false and w.focus_history_id == -1 and w.nosuch == nil)
            local e = hl.get_window("class:code")
            assert(e.swallowing == w and e.layout == nil and e.xdg_tag == "tag" and e.fullscreen == 1 and e.active == false)
            assert(e.monitor.name == "HDMI-A-1" and e.workspace.name == "web")
            """);
    }

    [Fact]
    public void AGroupReadsItsMembers()
    {
        var (runtime, _) = Start("");
        using var _r = runtime;

        Check(runtime, """
            local g = hl.get_window("address:0x2b").group
            assert(g.size == 2 and g.current_index == 1 and g.current.class == "kitty")
            assert(g.members[1].address == "0x1a" and g.members[2].address == "0x2b")
            assert(g.locked == nil and g.denied == nil and hl.get_window("class:code").group == nil)
            assert(#hl.get_workspace(1):get_groups() == 1 and hl.get_workspace(1).groups == 1)
            assert(hl.get_window("address:0x1a").group == g)
            """);
    }

    [Fact]
    public void AGroupAddsAndRemovesThroughThePipe()
    {
        var (runtime, world) = Start("");
        using var _r = runtime;

        Check(runtime, """
            local g = hl.get_window("address:0x2b").group
            g:add(hl.get_window("class:code"))
            g:add(hl.get_window("class:code"), 3)
            g:remove(2)
            g:remove(hl.get_window("address:0x1a"))
            """);

        Assert.Equal(
            ["groupadd address:0x2b address:0x3c", "groupadd address:0x2b address:0x3c 3", "groupremove address:0x2b", "groupremove address:0x1a"],
            world.Commands.Where(command => command.StartsWith("group", StringComparison.Ordinal)));
        Assert.Equal("error: line 1: HL.Group:add: index out of range", runtime.Eval("local _ = hl.get_active_window().group:add(hl.get_window('class:code'), 4)", repl: false));
        Assert.Equal("error: line 1: HL.Group:add: index must be an integer", runtime.Eval("local _ = hl.get_active_window().group:add(hl.get_window('class:code'), 1.5)", repl: false));
        Assert.Equal("error: line 1: HL.Group:add: expected a window object", runtime.Eval("local _ = hl.get_active_window().group:add('class:code')", repl: false));
        Assert.Equal("error: line 1: HL.Group:remove: index out of range", runtime.Eval("local _ = hl.get_active_window().group:remove(0)", repl: false));
        Assert.Equal("error: line 1: HL.Group:remove: window is not a group member", runtime.Eval("local _ = hl.get_active_window().group:remove(hl.get_window('class:code'))", repl: false));

        lock (world.Replies)
            world.Replies["groupadd address:0x1a address:0x3c"] = "error: target group is denied";

        Assert.Equal("error: line 1: HL.Group:add: target group is denied", runtime.Eval("local _ = hl.get_active_window().group:add(hl.get_window('class:code'))", repl: false));
    }

    [Fact]
    public void AWorkspaceReadsItsFieldsFromTheWorkspacesReply()
    {
        var (runtime, _) = Start("");
        using var _r = runtime;

        Check(runtime, """
            local one = hl.get_workspace(1)
            assert(one.id == 1 and one.name == "1" and one.monitor.name == "DP-1" and one.windows == 2)
            assert(one.visible and one.active and not one.special and one.has_urgent == false)
            assert(one.fullscreen_mode == 0 and one.has_fullscreen == false and one.is_persistent == false)
            assert(one.is_empty == false and one.config_name == nil and one.tiled_layout == "dwindle")
            assert(one.last_window.address == "0x1a" and one.fullscreen_window == nil and #one:get_windows() == 2)
            assert(one.addressable_name == "1" and math.type(one.id) == "integer" and tostring(one) == "HL.Workspace(1:1)")
            local web = hl.get_workspace("name:web")
            assert(web.id == nil and web.addressable_name == "web" and web.fullscreen_mode == 1 and web.has_fullscreen)
            assert(web.fullscreen_window.class == "code" and web.is_persistent and web.tiled_layout == "master")
            assert(hl.get_workspace("web") == web)
            local scratch = hl.get_workspace("special:scratch")
            assert(scratch.id == nil and scratch.special and scratch.visible and scratch.addressable_name == "special:scratch")
            assert(tostring(scratch) == "HL.Workspace(special:scratch:special:scratch)")
            assert(hl.get_workspace("nosuch") == nil and hl.get_workspace(9) == nil)
            """);
    }

    [Fact]
    public void AMonitorReadsItsFieldsFromTheMonitorsReply()
    {
        var (runtime, _) = Start("");
        using var _r = runtime;

        Check(runtime, """
            local m = hl.get_monitor("DP-1")
            assert(m.id == 0 and m.name == "DP-1" and m.description == "Dell U2720Q" and m.serial == "ABC")
            assert(m.width == 1920 and m.height == 1080 and m.physical_width == 600 and m.physical_height == 340)
            assert(m.refresh_rate == 60 and math.type(m.refresh_rate) == "float" and m.x == 0 and m.y == 0)
            assert(m.active_workspace.name == "1" and m.active_special_workspace.name == "special:scratch")
            assert(m.position.x == 0 and m.size.width == 1920 and m.size.height == 1080 and m.scale == 1.0)
            assert(m.transform == 0 and m.dpms_status and m.vrr_active == false and m.is_mirror == false and #m.mirrors == 0)
            assert(m.available_modes[1].width == 1920 and m.available_modes[1].refresh_rate == 60.0)
            assert(m.focused and m.cm == "srgb" and m.reserved.bottom == 48 and m.reserved.top == 0)
            local h = hl.get_monitor(1)
            assert(h.name == "HDMI-A-1" and h.active_special_workspace == nil and h.vrr_active and h.scale == 1.25)
            assert(hl.get_monitor("desc:LG") == h and tostring(h) == "HL.Monitor(1:HDMI-A-1)" and h ~= m)
            assert(h.mode.width == 2560 and h.mode.refresh_rate == 143.998 and h.enabled and h.hardware_details.hdr == false)
            assert(#hl.get_monitors() == 2)
            """);
        Assert.Equal("error: HLMonitor.set_workspace: expected a workspace object or selector", runtime.Eval("hl.get_monitor(1):set_workspace({ workspace = 1 })", repl: false));
    }

    [Fact]
    public void AMonitorListsTheMonitorsThatMirrorIt()
    {
        var (runtime, world) = Start("");
        using var _r = runtime;
        var mirror = JsonDocument.Parse(Monitors).RootElement[1].GetRawText()
            .Replace("\"id\":1", "\"id\":2", StringComparison.Ordinal)
            .Replace("HDMI-A-1", "DP-2", StringComparison.Ordinal)
            .Replace("\"mirrorOf\":\"none\"", "\"mirrorOf\":\"0\"", StringComparison.Ordinal);
        world.Replies["j/monitors all"] = "[" + Monitors.Trim()[1..^1] + "," + mirror + "]";

        Check(runtime, """
            local m = hl.get_monitor("DP-1")
            assert(#m.mirrors == 1 and m.mirrors[1].name == "DP-2" and m.mirrors[1].is_mirror and m.is_mirror == false)
            assert(#hl.get_monitor(1).mirrors == 0)
            """);
    }

    [Fact]
    public void AMonitorShowsAWorkspaceThroughThePipe()
    {
        var (runtime, world) = Start("");
        using var _r = runtime;

        Check(runtime, "hl.get_monitor('HDMI-A-1'):set_workspace(1)");
        Check(runtime, "hl.get_monitor('DP-1'):set_workspace('name:new')");
        Check(runtime, "hl.get_monitor('DP-1'):set_workspace(hl.get_workspace('web'))");
        Check(runtime, "hl.get_monitor('DP-1'):set_special_workspace(nil)");
        Check(runtime, "hl.get_monitor('HDMI-A-1'):set_special_workspace('')");
        Check(runtime, "hl.get_monitor('HDMI-A-1'):set_special_workspace(hl.get_workspace('special:scratch'))");
        Assert.Equal(
            [
                "setworkspace HDMI-A-1 1",
                "setworkspace DP-1 name:new",
                "setworkspace DP-1 name:web",
                "setspecialworkspace DP-1",
                "setspecialworkspace HDMI-A-1 special:special",
                "setspecialworkspace HDMI-A-1 special:scratch",
            ],
            world.Commands.Where(command => command.StartsWith("set", StringComparison.Ordinal)));
        Assert.DoesNotContain(world.Commands, command => command.StartsWith("dispatch ", StringComparison.Ordinal));

        lock (world.Replies)
            world.Replies["setworkspace DP-1 7"] = "error: invalid workspace";

        Assert.Equal("error: line 1: HLMonitor.set_workspace: invalid workspace", runtime.Eval("local _ = hl.get_monitor('DP-1'):set_workspace(7)", repl: false));
    }

    [Fact]
    public void ObjectsAreReadOnlyAndAGoneOneReadsNil()
    {
        var (runtime, world) = Start("");
        using var _r = runtime;

        Assert.Equal("error: line 1: attempt to modify read-only hl object", runtime.Eval("hl.get_active_window().title = 'x'", repl: false));

        Check(runtime, "gone = hl.get_window('class:firefox')");
        lock (world.Replies)
            world.Replies["ja/clients"] = "[]";

        Assert.Equal("nil\tnil\tHL.Window(expired)", Repl(runtime, "gone.title, gone.address, tostring(gone)"));
    }

    [Fact]
    public void TheQueriesAnswerFromTheReplies()
    {
        var (runtime, _) = Start("");
        using var _r = runtime;

        Check(runtime, """
            assert(#hl.get_windows() == 2 and hl.get_windows()[1].class == "kitty")
            assert(#hl.get_windows({ mapped = false }) == 2 and #hl.get_windows({ floating = true }) == 1)
            assert(#hl.get_windows({ class = "kitty" }) == 1 and #hl.get_windows({ title = "editor" }) == 1)
            assert(#hl.get_windows({ tag = "work" }) == 1 and #hl.get_windows({ workspace = "name:web" }) == 1)
            assert(#hl.get_windows({ monitor = "HDMI-A-1" }) == 1 and #hl.get_windows({ monitor = "nosuch" }) == 0)
            assert(hl.get_window("kit.*").class == "kitty" and hl.get_window("title:edi.*").class == "code")
            assert(hl.get_window("initialtitle:shell").class == "kitty" and hl.get_window("pid:300").class == "code")
            assert(hl.get_window("tag:w.*").class == "code" and hl.get_window("stableid:22").class == "notes")
            assert(hl.get_window("floating") == nil and hl.get_window("tiled").class == "kitty")
            assert(hl.get_window("nosuch") == nil and hl.get_window(hl.get_active_window()).class == "kitty")
            assert(hl.get_active_window().class == "kitty" and hl.get_urgent_window() == nil)
            assert(#hl.get_workspaces() == 3 and hl.get_active_workspace().name == "1")
            assert(hl.get_active_workspace("HDMI-A-1").name == "web")
            assert(hl.get_active_special_workspace().name == "special:scratch" and hl.get_active_special_workspace(1) == nil)
            assert(#hl.get_monitors() == 2 and hl.get_active_monitor().name == "DP-1")
            assert(hl.get_monitor_at(2000, 10).name == "HDMI-A-1" and hl.get_monitor_at({ x = 5, y = 5 }).name == "DP-1")
            assert(hl.get_monitor_at(-5, -5) == nil and hl.get_monitor_at_cursor().name == "HDMI-A-1")
            local cursor = hl.get_cursor_pos()
            assert(cursor.x == 2000 and cursor.y == 10 and math.type(cursor.x) == "float")
            assert(#hl.get_workspace_windows(1) == 1 and #hl.get_workspace_windows("nosuch") == 0)
            assert(hl.get_current_submap() == "resize" and #hl.get_layers() == 0)
            local gaps = hl.get_config("general.gaps_in")
            assert(gaps.top == 5 and gaps.left == 5 and hl.get_config("general:border_size") == 2)
            assert(hl.get_config("decoration.active_opacity") == 0.9)
            local value, why = hl.get_config("general.nosuch")
            assert(value == nil and why == "unknown config key 'general.nosuch'")
            assert(type(hl.version()) == "string")
            local plugin = hl.get_loaded_plugins()[1]
            assert(plugin.name == "demo" and plugin.version == "3" and plugin.author == "" and plugin.description == "")
            """);
    }

    [Fact]
    public void TheHistoryQueriesFollowTheEvents()
    {
        var (runtime, _) = Start("");
        using var _r = runtime;

        runtime.Deliver([
            new FocusChanged(Editor),
            new FocusChanged(Term),
            new ClientUrgent(Editor),
            new WorkspaceActivated(new WorkspaceId("web")),
            new WorkspaceActivated(WorkspaceId.FromNumber(1)),
        ]);

        Check(runtime, """
            assert(hl.get_last_window().class == "code" and hl.get_urgent_window().class == "code")
            assert(hl.get_workspace("name:web").has_urgent and hl.get_active_window().focus_history_id == 0)
            assert(hl.get_window("class:code").focus_history_id == 1)
            assert(hl.get_last_workspace().name == "web" and hl.get_last_workspace("DP-1") == nil)
            """);

        runtime.Deliver([new FocusChanged(Editor)]);
        Check(runtime, "assert(hl.get_urgent_window() == nil)");
    }

    [Fact]
    public void AReplyIsReadOnceUntilADispatchRuns()
    {
        var (runtime, world) = Start("");
        using var _r = runtime;

        Check(runtime, """
            local w = hl.get_active_window()
            local _ = w.title, w.class, w.pid
            hl.dispatch(hl.dsp.window.close({ window = w }))
            local _ = w.title
            """);

        Assert.Equal(2, world.Commands.Count(command => command == "ja/clients"));
        Assert.Contains("dispatch closewindow address:0x1a", world.Commands);
    }

    [Fact]
    public void ADispatcherTakesObjectsForItsSelectors()
    {
        var (runtime, world) = Start("");
        using var _r = runtime;

        Check(runtime, """
            hl.dispatch(hl.dsp.focus({ workspace = hl.get_workspace("name:web") }))
            hl.dispatch(hl.dsp.focus({ workspace = hl.get_workspace(1) }))
            hl.dispatch(hl.dsp.focus({ monitor = hl.get_monitor(1) }))
            hl.dispatch(hl.dsp.window.move({ workspace = hl.get_workspace("special:scratch"), window = hl.get_window("class:code") }))
            """);

        Assert.Equal(
            ["dispatch workspace name:web", "dispatch workspace 1", "dispatch focusmonitor HDMI-A-1", "dispatch movetoworkspace special:scratch,address:0x3c"],
            world.Commands.Where(command => command.StartsWith("dispatch", StringComparison.Ordinal)));
    }
}
