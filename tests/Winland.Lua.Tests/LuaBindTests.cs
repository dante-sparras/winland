using Winland;
using Winland.Lua;

namespace Winland.Lua.Tests;

public class LuaBindTests
{
    private static readonly string ConfigPath = Path.Combine(Path.GetTempPath(), "winland-lua-tests", "winland.lua");

    private static LuaTranslation Run(string code) => LuaConfig.Translate(code, ConfigPath, _ => null);

    private static string[] Lines(LuaTranslation translation) =>
        translation.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    // The bind lines a dispatcher expression makes in hl.bind("SUPER + X", expression).
    private static string Bound(string dispatcher)
    {
        var translation = Run($"hl.bind(\"SUPER + X\", {dispatcher})");
        Assert.Empty(translation.Warnings);
        return Assert.Single(Lines(translation));
    }

    [Fact]
    public void TheExampleConfigsBindsBecomeBindLines()
    {
        var translation = Run("""
            local terminal = "kitty"
            local fileManager = "dolphin"
            local menu = "wofi"
            local mainMod = "SUPER"
            hl.bind(mainMod .. " + Q", hl.dsp.exec_cmd(terminal))
            local closeWindowBind = hl.bind(mainMod .. " + C", hl.dsp.window.close())
            hl.bind(mainMod .. " + E", hl.dsp.exec_cmd(fileManager))
            hl.bind(mainMod .. " + V", hl.dsp.window.float({ action = "toggle" }))
            hl.bind(mainMod .. " + R", hl.dsp.exec_cmd(menu))
            hl.bind(mainMod .. " + P", hl.dsp.window.pseudo())
            hl.bind(mainMod .. " + J", hl.dsp.layout("togglesplit"))
            hl.bind(mainMod .. " + left",  hl.dsp.focus({ direction = "left" }))
            hl.bind(mainMod .. " + right", hl.dsp.focus({ direction = "right" }))
            hl.bind(mainMod .. " + up",    hl.dsp.focus({ direction = "up" }))
            hl.bind(mainMod .. " + down",  hl.dsp.focus({ direction = "down" }))
            for i = 1, 10 do
                local key = i % 10
                hl.bind(mainMod .. " + " .. key,             hl.dsp.focus({ workspace = i}))
                hl.bind(mainMod .. " + SHIFT + " .. key,     hl.dsp.window.move({ workspace = i }))
            end
            hl.bind(mainMod .. " + S",         hl.dsp.workspace.toggle_special("magic"))
            hl.bind(mainMod .. " + SHIFT + S", hl.dsp.window.move({ workspace = "special:magic" }))
            hl.bind(mainMod .. " + mouse_down", hl.dsp.focus({ workspace = "e+1" }))
            hl.bind(mainMod .. " + mouse_up",   hl.dsp.focus({ workspace = "e-1" }))
            hl.bind(mainMod .. " + mouse:272", hl.dsp.window.drag(),   { mouse = true })
            hl.bind(mainMod .. " + mouse:273", hl.dsp.window.resize(), { mouse = true })
            hl.bind("XF86AudioRaiseVolume", hl.dsp.exec_cmd("wpctl set-volume -l 1 @DEFAULT_AUDIO_SINK@ 5%+"), { locked = true, repeating = true })
            hl.bind("XF86AudioNext",  hl.dsp.exec_cmd("playerctl next"),       { locked = true })
            """);

        Assert.Empty(translation.Warnings);
        var lines = Lines(translation);
        Assert.Equal(
            [
                "bind = SUPER, Q, exec, kitty",
                "bind = SUPER, C, killactive",
                "bind = SUPER, E, exec, dolphin",
                "bind = SUPER, V, togglefloating",
                "bind = SUPER, R, exec, wofi",
                "bind = SUPER, P, pseudo",
                "bind = SUPER, J, layoutmsg, togglesplit",
                "bind = SUPER, left, movefocus, l",
                "bind = SUPER, right, movefocus, r",
                "bind = SUPER, up, movefocus, u",
                "bind = SUPER, down, movefocus, d",
                "bind = SUPER, 1, workspace, 1",
                "bind = SUPER SHIFT, 1, movetoworkspace, 1",
            ],
            lines[..13]);
        Assert.Contains("bind = SUPER, 0, workspace, 10", lines);
        Assert.Contains("bind = SUPER SHIFT, 0, movetoworkspace, 10", lines);
        Assert.Equal(
            [
                "bind = SUPER, S, togglespecialworkspace, magic",
                "bind = SUPER SHIFT, S, movetoworkspace, special:magic",
                "bind = SUPER, mouse_down, workspace, e+1",
                "bind = SUPER, mouse_up, workspace, e-1",
                "bindm = SUPER, mouse:272, movewindow",
                "bindm = SUPER, mouse:273, resizewindow",
                "bindle = , XF86AudioRaiseVolume, exec, wpctl set-volume -l 1 @DEFAULT_AUDIO_SINK@ 5%+",
                "bindl = , XF86AudioNext, exec, playerctl next",
            ],
            lines[^8..]);
    }

    [Fact]
    public void EachBindOptionIsItsFlag()
    {
        var translation = Run("""
            hl.bind("SUPER + A", hl.dsp.exit(), { locked = true })
            hl.bind("SUPER + B", hl.dsp.exit(), { release = true })
            hl.bind("SUPER + C", hl.dsp.exit(), { repeating = true })
            hl.bind("SUPER + D", hl.dsp.exit(), { long_press = true })
            hl.bind("SUPER + E", hl.dsp.exit(), { non_consuming = true })
            hl.bind("SUPER + F", hl.dsp.exit(), { transparent = true })
            hl.bind("SUPER + G", hl.dsp.exit(), { ignore_mods = true })
            hl.bind("SUPER + H", hl.dsp.exit(), { dont_inhibit = true })
            hl.bind("SUPER + I", hl.dsp.exit(), { click = true })
            hl.bind("SUPER + J", hl.dsp.exit(), { drag = true })
            hl.bind("SUPER + K", hl.dsp.exit(), { submap_universal = true })
            hl.bind("SUPER + L", hl.dsp.exit(), { description = "Leave ## now" })
            hl.bind("SUPER + M", hl.dsp.exit(), { desc = "Short" })
            hl.bind("SUPER + mouse:272", hl.dsp.exec_cmd("kitty"), { mouse = true })
            hl.bind("SUPER + N", hl.dsp.exit(), { locked = true, release = true, transparent = false })
            """);

        Assert.Empty(translation.Warnings);
        Assert.Equal(
            [
                "bindl = SUPER, A, exit",
                "bindr = SUPER, B, exit",
                "binde = SUPER, C, exit",
                "bindo = SUPER, D, exit",
                "bindn = SUPER, E, exit",
                "bindt = SUPER, F, exit",
                "bindi = SUPER, G, exit",
                "bindp = SUPER, H, exit",
                "bindc = SUPER, I, exit",
                "bindg = SUPER, J, exit",
                "bindu = SUPER, K, exit",
                "bindd = SUPER, L, Leave #### now, exit",
                "bindd = SUPER, M, Short, exit",
                "bindm = SUPER, mouse:272, exec kitty",
                "bindlr = SUPER, N, exit",
            ],
            Lines(translation));
    }

    [Fact]
    public void OptionsWithoutALineFormWarn()
    {
        var translation = Run("""
            hl.bind("SUPER + A", hl.dsp.exit(), { auto_consuming = true })
            hl.bind("SUPER + B", hl.dsp.exit(), { device = { list = { "my-keyboard" } } })
            hl.bind("SUPER + C", hl.dsp.exit(), { description = "a, b" })
            """);

        Assert.Equal(["bind = SUPER, A, exit", "bind = SUPER, C, exit"], Lines(translation));
        Assert.Equal(
            [
                new ConfigWarning(1, "hl.bind: auto_consuming is not honored yet"),
                new ConfigWarning(2, "hl.bind: binds for a device are not honored yet"),
                new ConfigWarning(3, "hl.bind: a description with a comma is not honored yet"),
            ],
            translation.Warnings);
    }

    [Theory]
    [InlineData("SUPER + SHIFT + CTRL + ALT + Q", "SUPER SHIFT CTRL ALT", "Q", false)]
    [InlineData("super + q", "SUPER", "q", false)]
    [InlineData("Return", "", "Return", false)]
    [InlineData("SUPER + code:28", "SUPER", "code:28", false)]
    [InlineData("SUPER + mouse:273", "SUPER", "mouse:273", false)]
    [InlineData("SUPER + mouse_down", "SUPER", "mouse_down", false)]
    [InlineData("SUPER + X + D + F", "SUPER", "X&D&F", true)]
    [InlineData("Super_L + Alt_L", "", "Super_L&Alt_L", true)]
    [InlineData("catchall", "", "catchall", false)]
    public void KeyStringsSplitIntoModifiersAndKeys(string text, string modifiers, string key, bool chord) =>
        Assert.Equal(new LuaKeys(modifiers, key, chord), LuaRun.ParseKeys(text, out _));

    [Theory]
    [InlineData("SUPER + ", "Empty key in key list")]
    [InlineData("SUPER + Q + q", "Repeated key in the key string")]
    [InlineData("Q + SUPER", "Modifiers cannot appear after keys")]
    [InlineData("SUPER + SHIFT", "A bind requires a trigger")]
    [InlineData("SUPER + Q + mouse:272", "External events cannot be combined with other keys")]
    public void BadKeyStringsSayWhy(string text, string error)
    {
        Assert.Null(LuaRun.ParseKeys(text, out var reason));
        Assert.Equal(error, reason);
    }

    [Fact]
    public void ChordsAndKeyStringsReachTheBindLine()
    {
        var translation = Run("""
            hl.bind("SUPER + X + D", hl.dsp.exit())
            hl.bind("SUPER + mouse:276", hl.dsp.exit(), { release = true })
            hl.bind("SUPER + Q + SHIFT", hl.dsp.exit())
            """);

        Assert.Equal(["binds = SUPER, X&D, exit", "bindr = SUPER, mouse:276, exit"], Lines(translation));
        Assert.Equal(new ConfigWarning(3, "hl.bind: failed to create bind: Modifiers cannot appear after keys"), Assert.Single(translation.Warnings));
    }

    [Theory]
    [InlineData("hl.dsp.cursor.move_to_corner({ corner = 2 })", "bind = SUPER, X, movecursortocorner, 2")]
    [InlineData("hl.dsp.cursor.move({ x = 10, y = 20.0 })", "bind = SUPER, X, movecursor, 10 20")]
    [InlineData("hl.dsp.group.toggle()", "bind = SUPER, X, togglegroup")]
    [InlineData("hl.dsp.group.next()", "bind = SUPER, X, changegroupactive, f")]
    [InlineData("hl.dsp.group.prev()", "bind = SUPER, X, changegroupactive, b")]
    [InlineData("hl.dsp.group.active({ index = 2 })", "bind = SUPER, X, changegroupactive, 2")]
    [InlineData("hl.dsp.group.move_window()", "bind = SUPER, X, movegroupwindow")]
    [InlineData("hl.dsp.group.move_window({ forward = false })", "bind = SUPER, X, movegroupwindow, b")]
    [InlineData("hl.dsp.group.lock()", "bind = SUPER, X, lockactivegroup, toggle")]
    [InlineData("hl.dsp.group.lock({ action = 'enable' })", "bind = SUPER, X, lockactivegroup, lock")]
    [InlineData("hl.dsp.group.lock({ all = true, action = 'off' })", "bind = SUPER, X, lockgroups, unlock")]
    [InlineData("hl.dsp.window.close({ window = 'class:kitty' })", "bind = SUPER, X, closewindow, class:kitty")]
    [InlineData("hl.dsp.window.kill()", "bind = SUPER, X, killwindow, activewindow")]
    [InlineData("hl.dsp.window.float({ action = 'enable' })", "bind = SUPER, X, setfloating")]
    [InlineData("hl.dsp.window.float({ action = 'disable', window = 'class:a' })", "bind = SUPER, X, settiled, class:a")]
    [InlineData("hl.dsp.window.fullscreen()", "bind = SUPER, X, fullscreen, 0")]
    [InlineData("hl.dsp.window.fullscreen({ mode = 'maximized', action = 'set', layout_aware = false })", "bind = SUPER, X, fullscreen, 1 set default")]
    [InlineData("hl.dsp.window.fullscreen_state({ internal = 1, client = 2 })", "bind = SUPER, X, fullscreenstate, 1 2 set")]
    [InlineData("hl.dsp.window.fullscreen_state({ internal = 2, client = 0, action = 'toggle', window = 'class:a' })", "bind = SUPER, X, fullscreenstate, 2 0 toggle class:a")]
    [InlineData("hl.dsp.window.pseudo({ window = 'class:a' })", "bind = SUPER, X, pseudo, class:a")]
    [InlineData("hl.dsp.window.move({ direction = 'l' })", "bind = SUPER, X, movewindow, l")]
    [InlineData("hl.dsp.window.move({ direction = 'down', group_aware = true })", "bind = SUPER, X, movewindoworgroup, d")]
    [InlineData("hl.dsp.window.move({ x = 100, y = 50 })", "bind = SUPER, X, movewindowpixel, exact 100 50,activewindow")]
    [InlineData("hl.dsp.window.move({ x = -10, y = 0, relative = true, window = 'class:a' })", "bind = SUPER, X, movewindowpixel, -10 0,class:a")]
    [InlineData("hl.dsp.window.move({ workspace = 3, follow = false })", "bind = SUPER, X, movetoworkspacesilent, 3")]
    [InlineData("hl.dsp.window.move({ workspace = 'name:web', window = 'class:a' })", "bind = SUPER, X, movetoworkspace, name:web,class:a")]
    [InlineData("hl.dsp.window.move({ monitor = 'DP-1' })", "bind = SUPER, X, movewindow, mon:DP-1")]
    [InlineData("hl.dsp.window.move({ monitor = '+1', follow = false })", "bind = SUPER, X, movewindow, mon:+1 silent")]
    [InlineData("hl.dsp.window.move({ into_group = 'right' })", "bind = SUPER, X, moveintogroup, r")]
    [InlineData("hl.dsp.window.move({ into_or_create_group = 'up' })", "bind = SUPER, X, moveintoorcreategroup, u")]
    [InlineData("hl.dsp.window.move({ out_of_group = true })", "bind = SUPER, X, moveoutofgroup")]
    [InlineData("hl.dsp.window.swap({ direction = 'r' })", "bind = SUPER, X, swapwindow, r")]
    [InlineData("hl.dsp.window.swap({ target = 'class:b' })", "bind = SUPER, X, swapwindow, class:b")]
    [InlineData("hl.dsp.window.center()", "bind = SUPER, X, centerwindow")]
    [InlineData("hl.dsp.window.cycle_next()", "bind = SUPER, X, cyclenext")]
    [InlineData("hl.dsp.window.cycle_next({ next = false, tiled = true })", "bind = SUPER, X, cyclenext, prev tiled")]
    [InlineData("hl.dsp.window.tag({ tag = '+work' })", "bind = SUPER, X, tagwindow, +work")]
    [InlineData("hl.dsp.window.toggle_swallow()", "bind = SUPER, X, toggleswallow")]
    [InlineData("hl.dsp.window.pin()", "bind = SUPER, X, pin")]
    [InlineData("hl.dsp.window.pin({ action = 'on', window = 'class:a' })", "bind = SUPER, X, pin, enable class:a")]
    [InlineData("hl.dsp.window.bring_to_top()", "bind = SUPER, X, bringactivetotop")]
    [InlineData("hl.dsp.window.alter_zorder({ mode = 'bottom', window = 'class:a' })", "bind = SUPER, X, alterzorder, bottom,class:a")]
    [InlineData("hl.dsp.window.set_prop({ prop = 'opaque', value = true })", "bind = SUPER, X, setprop, activewindow opaque true")]
    [InlineData("hl.dsp.window.deny_from_group({ action = 'on' })", "bind = SUPER, X, denywindowfromgroup, on")]
    [InlineData("hl.dsp.window.drag()", "bind = SUPER, X, mouse, movewindow")]
    [InlineData("hl.dsp.window.resize({ keep_aspect_ratio = true })", "bind = SUPER, X, mouse, resizewindow 1")]
    [InlineData("hl.dsp.window.resize({ x = 10, y = -10, relative = true })", "bind = SUPER, X, resizeactive, 10 -10")]
    [InlineData("hl.dsp.window.resize({ x = 800, y = 600, window = 'class:a' })", "bind = SUPER, X, resizewindowpixel, exact 800 600,class:a")]
    [InlineData("hl.dsp.workspace.rename({ workspace = 2, name = 'web' })", "bind = SUPER, X, renameworkspace, 2 web")]
    [InlineData("hl.dsp.workspace.change_id({ workspace = 2, id = 7 })", "bind = SUPER, X, changeworkspaceid, 2 7")]
    [InlineData("hl.dsp.workspace.move({ monitor = 'DP-2' })", "bind = SUPER, X, movecurrentworkspacetomonitor, DP-2")]
    [InlineData("hl.dsp.workspace.move({ monitor = 'DP-2', workspace = 4 })", "bind = SUPER, X, moveworkspacetomonitor, 4 DP-2")]
    [InlineData("hl.dsp.workspace.swap_monitors({ monitor1 = 'DP-1', monitor2 = 'DP-2' })", "bind = SUPER, X, swapactiveworkspaces, DP-1 DP-2")]
    [InlineData("hl.dsp.workspace.toggle_special()", "bind = SUPER, X, togglespecialworkspace")]
    [InlineData("hl.dsp.exec_cmd('kitty', { float = true })", "bind = SUPER, X, exec, [float] kitty")]
    [InlineData("hl.dsp.exec_raw('notepad #1')", "bind = SUPER, X, execr, notepad ##1")]
    [InlineData("hl.dsp.exit()", "bind = SUPER, X, exit")]
    [InlineData("hl.dsp.submap('resize')", "bind = SUPER, X, submap, resize")]
    [InlineData("hl.dsp.pass({ window = 'class:obs' })", "bind = SUPER, X, pass, class:obs")]
    [InlineData("hl.dsp.send_shortcut({ mods = 'CTRL', key = 'C', window = 'class:a' })", "bind = SUPER, X, sendshortcut, CTRL, C, class:a")]
    [InlineData("hl.dsp.send_shortcut({ mods = 'CTRL', key = 'C' })", "bind = SUPER, X, sendshortcut, CTRL, C")]
    [InlineData("hl.dsp.send_key_state({ mods = '', key = 'A', state = 'down' })", "bind = SUPER, X, sendkeystate, , A, down,")]
    [InlineData("hl.dsp.layout('swapsplit')", "bind = SUPER, X, layoutmsg, swapsplit")]
    [InlineData("hl.dsp.dpms()", "bind = SUPER, X, dpms, toggle")]
    [InlineData("hl.dsp.dpms({ action = 'disable', monitor = 'DP-1' })", "bind = SUPER, X, dpms, off DP-1")]
    [InlineData("hl.dsp.event('hello')", "bind = SUPER, X, event, hello")]
    [InlineData("hl.dsp.force_renderer_reload()", "bind = SUPER, X, forcerendererreload")]
    [InlineData("hl.dsp.focus({ monitor = 'DP-1' })", "bind = SUPER, X, focusmonitor, DP-1")]
    [InlineData("hl.dsp.focus({ workspace = 3, on_current_monitor = true })", "bind = SUPER, X, focusworkspaceoncurrentmonitor, 3")]
    [InlineData("hl.dsp.focus({ window = 'class:a' })", "bind = SUPER, X, focuswindow, class:a")]
    [InlineData("hl.dsp.focus({ urgent_or_last = true })", "bind = SUPER, X, focusurgentorlast")]
    [InlineData("hl.dsp.no_op()", "bind = SUPER, X, no_op")]
    public void EachDispatcherIsItsLineDispatcher(string dispatcher, string line) =>
        Assert.Equal(line, Bound(dispatcher));

    [Theory]
    [InlineData("hl.dsp.window.signal({ signal = 9 })", "hl.dsp.window.signal is not honored yet")]
    [InlineData("hl.dsp.window.clear_tags()", "hl.dsp.window.clear_tags is not honored yet")]
    [InlineData("hl.dsp.global('app:action')", "hl.dsp.global is not honored yet")]
    [InlineData("hl.dsp.force_idle(5)", "hl.dsp.force_idle is not honored yet")]
    [InlineData("hl.dsp.release_input_capture()", "hl.dsp.release_input_capture is not honored yet")]
    [InlineData("hl.dsp.focus({ last = true })", "hl.dsp.focus with last is not honored yet")]
    [InlineData("hl.dsp.window.swap({ next = true })", "hl.dsp.window.swap with next or prev is not honored yet")]
    [InlineData("hl.dsp.window.center({ window = 'class:a' })", "hl.dsp.window.center with a window is not honored yet")]
    [InlineData("hl.dsp.window.pseudo({ action = 'enable' })", "hl.dsp.window.pseudo with an action other than toggle is not honored yet")]
    [InlineData("hl.dsp.window.move({ out_of_group = 'left' })", "hl.dsp.window.move with an out_of_group direction is not honored yet")]
    public void DispatchersWithoutALineFormWarnOnceAndMakeNoLine(string dispatcher, string warning)
    {
        var translation = Run($"""
            hl.bind("SUPER + X", {dispatcher})
            hl.bind("SUPER + Y", {dispatcher})
            """);

        Assert.Empty(Lines(translation));
        Assert.Equal(new ConfigWarning(1, warning), Assert.Single(translation.Warnings));
    }

    [Fact]
    public void LockActiveIsGroupLockAndIsNotedAsDeprecated()
    {
        var translation = Run("""
            hl.bind("SUPER + X", hl.dsp.group.lock_active({ action = 'toggle' }))
            hl.bind("SUPER + Y", hl.dsp.group.lock_active())
            """);

        Assert.Equal(["bind = SUPER, X, lockactivegroup, toggle", "bind = SUPER, Y, lockactivegroup, toggle"], Lines(translation));
        Assert.Equal(
            new ConfigWarning(1, "hl.dsp.group.lock_active(): The group.lock_active() dispatcher is deprecated, use group.lock() instead"),
            Assert.Single(translation.Warnings));
    }

    [Fact]
    public void ADispatcherThatIsACommandIsBoundThroughAFunction()
    {
        var translation = Run("hl.bind(\"SUPER + R\", hl.dsp.reload_config())\n");

        Assert.Equal(["bind = SUPER, R, luafunction, 1"], Lines(translation));
        Assert.Empty(translation.Warnings);
    }

    [Fact]
    public void ADispatcherShowsItsNameAndTakesNoMoreArgumentsThanItCan()
    {
        var translation = Run("""
            assert(tostring(hl.dsp.window.close()) == "HL.Dispatcher(close)")
            assert(tostring(hl.dsp.exec_cmd("kitty")) == "HL.Dispatcher(exec_cmd)")
            assert(hl.dsp.exit(1) == nil)
            """);

        Assert.Equal(new ConfigWarning(3, "exit: expected at most 0 arguments, got 1"), Assert.Single(translation.Warnings));
    }

    [Fact]
    public void ABindReturnsItsKeybind()
    {
        var translation = Run("""
            local b = hl.bind("SUPER + SHIFT + F24", hl.dsp.exec_cmd("true"), { locked = true, description = "run it" })
            assert(b.handler == "HL.Dispatcher(exec_cmd)" and b.display_key == "SUPER + SHIFT + F24" and b.key == "F24")
            assert(b.modmask == 65 and b.locked and not b.release and b.description == "run it" and b.has_description)
            assert(b:is_enabled() and b.enabled and b.submap == "" and b.keycode == 0 and #b.devices == 0)
            local f = hl.bind("SUPER + G", function() end)
            assert(f.handler:sub(1, 10) == "function: ")
            f:set_enabled(false)
            assert(f:is_enabled() == false)
            b:unbind()
            assert(b.handler == nil and b:is_enabled() == nil and tostring(b) == "HL.Keybind(expired)")
            """);

        Assert.Empty(translation.Warnings);
        Assert.Empty(Lines(translation));
    }

    [Fact]
    public void FunctionBindsCallTheirFunctionAndWrongDispatchersWarn()
    {
        var translation = Run("""
            hl.bind("SUPER + A", function() hl.dispatch(hl.dsp.exit()) end)
            hl.bind("SUPER + B", function() end)
            hl.bind("SUPER + C", hl.dsp.window.close)
            hl.bind("SUPER + D", "killactive")
            hl.bind("SUPER + E", hl.dsp.focus({ direction = "sideways" }))
            hl.bind("SUPER + F", hl.dsp.exit())
            """);

        Assert.Equal(["bind = SUPER, A, luafunction, 1", "bind = SUPER, B, luafunction, 2", "bind = SUPER, F, exit"], Lines(translation));
        Assert.Equal(
            [
                new ConfigWarning(3, "hl.bind: dispatcher factory supplied instead of a dispatcher; call the factory first (missing parentheses?)"),
                new ConfigWarning(4, "hl.bind: expected a dispatcher (e.g. hl.dsp.window.close()) or a lua function"),
                new ConfigWarning(5, "hl.dsp.focus: invalid direction \"sideways\" (expected left/right/up/down)"),
                new ConfigWarning(5, "hl.bind: expected a dispatcher (e.g. hl.dsp.window.close()) or a lua function"),
            ],
            translation.Warnings);
    }

    [Fact]
    public void UnbindNamesTheKeysOrAll()
    {
        var translation = Run("""
            hl.unbind("SUPER + SHIFT + Q")
            hl.unbind("all")
            hl.unbind("SUPER +")
            """);

        Assert.Equal(["unbind = SUPER SHIFT, Q", "unbind = all"], Lines(translation));
        Assert.Equal(new ConfigWarning(3, "hl.unbind: failed to parse key string: Empty key in key list"), Assert.Single(translation.Warnings));
    }

    [Fact]
    public void ASubmapHoldsTheBindsItsFunctionMakes()
    {
        var translation = Run("""
            hl.bind("ALT + R", hl.dsp.submap("resize"))
            hl.define_submap("resize", function()
                hl.bind("right", hl.dsp.window.resize({ x = 10, y = 0, relative = true }), { repeating = true })
                hl.define_submap("inner", "reset", function()
                    hl.bind("a", hl.dsp.exec_cmd("thing"))
                end)
                hl.bind("escape", hl.dsp.submap("reset"))
            end)
            hl.bind("SUPER + Q", hl.dsp.exit())
            """);

        Assert.Empty(translation.Warnings);
        Assert.Equal(
            [
                "bind = ALT, R, submap, resize",
                "submap = resize",
                "binde = , right, resizeactive, 10 0",
                "submap = inner, reset",
                "bind = , a, exec, thing",
                "submap = resize",
                "bind = , escape, submap, reset",
                "submap = reset",
                "bind = SUPER, Q, exit",
            ],
            Lines(translation));
    }

    [Fact]
    public void AnErrorInASubmapFunctionWarnsAndTheConfigRunsOn()
    {
        var translation = Run("""
            hl.define_submap("broken", function()
                hl.bind("a", hl.dsp.exit())
                error("oops")
            end)
            hl.bind("SUPER + Q", hl.dsp.exit())
            """);

        Assert.Equal(["submap = broken", "bind = , a, exit", "submap = reset", "bind = SUPER, Q, exit"], Lines(translation));
        var warning = Assert.Single(translation.Warnings);
        Assert.Equal(3, warning.Line);
        Assert.Contains("oops", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheParserReadsTheBindLinesWithoutWarnings()
    {
        var translation = Run("""
            hl.bind("SUPER + SHIFT + Q", hl.dsp.window.close(), { description = "Close" })
            hl.bind("SUPER + mouse:272", hl.dsp.window.drag(), { mouse = true })
            hl.define_submap("resize", function()
                hl.bind("right", hl.dsp.window.resize({ x = 10, y = 0, relative = true }), { repeating = true })
            end)
            hl.bind("SUPER + 3", hl.dsp.window.move({ workspace = 3, follow = false }), { locked = true })
            hl.bind("SUPER + X + D", hl.dsp.exec_cmd("kitty"))
            hl.unbind("SUPER + T")
            """);

        var document = translation.Remap(ConfigParser.Parse(translation.Text));

        Assert.Empty(document.Warnings);
        var binds = document.Binds.OfType<Bind>().ToList();
        Assert.Equal(5, binds.Count);
        Assert.Equal((Modifier.Super | Modifier.Shift, "killactive", "", BindFlag.None, "Close"), (binds[0].Modifiers, binds[0].Dispatcher, binds[0].Arguments, binds[0].Flags, binds[0].Description));
        Assert.Equal(("mouse:272", "movewindow", BindFlag.Mouse), (binds[1].Key, binds[1].Dispatcher, binds[1].Flags));
        Assert.Equal(("resizeactive", "10 0", BindFlag.Repeat, "resize"), (binds[2].Dispatcher, binds[2].Arguments, binds[2].Flags, binds[2].Submap));
        Assert.Equal(("movetoworkspacesilent", "3", BindFlag.Locked, (string?)null), (binds[3].Dispatcher, binds[3].Arguments, binds[3].Flags, binds[3].Submap));
        Assert.Equal(("exec", "kitty", BindFlag.MultiKey), (binds[4].Dispatcher, binds[4].Arguments, binds[4].Flags));
        Assert.Equal(Modifier.Super, Assert.Single(document.Binds.OfType<Unbind>()).Modifiers);
    }

    [Fact]
    public void AParserWarningOnABindLinePointsAtTheLuaLine()
    {
        var translation = Run("""
            hl.bind("SUPER + Q", hl.dsp.exit())

            hl.bind("catchall", hl.dsp.exit())
            """);

        var document = translation.Remap(ConfigParser.Parse(translation.Text));

        var warning = Assert.Single(document.Warnings);
        Assert.Equal(3, warning.Line);
        Assert.Equal("Catchall binds are only allowed in a submap.", warning.Message);
    }
}
