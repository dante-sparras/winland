using Winland;
using Winland.Lua;

namespace Winland.Lua.Tests;

public class LuaRulesTests
{
    private static readonly string ConfigPath = Path.Combine(Path.GetTempPath(), "winland-lua-tests", "winland.lua");

    private static LuaTranslation Run(string code) => LuaConfig.Translate(code, ConfigPath, _ => null);

    private static string[] Lines(LuaTranslation translation) =>
        translation.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static ConfigDocument Parse(LuaTranslation translation) => translation.Remap(ConfigParser.Parse(translation.Text));

    private static string[] Messages(ConfigDocument document) => [.. document.Warnings.Select(warning => warning.Message)];

    [Fact]
    public void MonitorsBecomeMonitorLines()
    {
        var translation = Run("""
            hl.monitor({ output = "", mode = "preferred", position = "auto", scale = "auto" })
            hl.monitor({ output = "DP-1", mode = "2560x1440@144", position = "0x0", scale = 1.5, transform = 1, cm = "hdr", reserved = { top = 30, left = 10 } })
            hl.monitor({ output = "DP-2" })
            hl.monitor({ output = "DP-3", reserved_area = 5 })
            hl.monitor({ output = "HDMI-A-1", mode = "1920x1080", disabled = true })
            """);

        Assert.Empty(translation.Warnings);
        Assert.Equal(
            [
                "monitor = , preferred, auto, auto",
                "monitor = DP-1, 2560x1440@144, 0x0, 1.5, transform, 1, cm, hdr",
                "monitor = DP-1, addreserved, 30, 0, 10, 0",
                "monitor = DP-2, preferred, auto, auto",
                "monitor = DP-3, preferred, auto, auto",
                "monitor = DP-3, addreserved, 5, 5, 5, 5",
                "monitor = HDMI-A-1, disable",
            ],
            Lines(translation));
        Assert.Empty(Parse(translation).Warnings);
    }

    [Fact]
    public void MonitorFieldsWinlandDoesNotHonorWarn()
    {
        var translation = Run("""
            hl.monitor({ output = "DP-1", mirror = "DP-2", vrr = 1, bitdepth = 10 })
            hl.monitor({ output = "DP-2", supports_hdr = 1, refresh = 60 })
            hl.monitor({ mode = "preferred" })
            hl.monitor("DP-1")
            """);

        Assert.Equal(
            [
                "monitor = DP-1, preferred, auto, auto, mirror, DP-2, bitdepth, 10, vrr, 1",
                "monitor = DP-2, preferred, auto, auto, supports_hdr, 1",
            ],
            Lines(translation));
        Assert.Equal(
            [
                "hl.monitor: unknown field 'refresh'",
                "hl.monitor: 'output' field is required and must be a string",
                "hl.monitor: argument must be a table",
                "Variable refresh rate is not honored.",
            ],
            Messages(Parse(translation)));
    }

    [Fact]
    public void AWindowRuleIsALineForEachEffect()
    {
        var translation = Run("""
            local rule = hl.window_rule({
                name  = "move-run",
                match = { class = "^(run)$", float = false, workspace = "w[tv1]", fullscreen_state_client = 2 },
                move  = "20 monitor_h-120",
                float = true,
                border_size = 0,
                opacity = 0.8,
                border_color = { colors = { "rgba(33ccffee)", 0xff00ff99 }, angle = 45 },
                no_focus = false,
                tile = false,
                ignore = false,
            })
            rule:set_enabled(false)
            hl.window_rule({ match = { title = "Notes" }, float = false })
            """);

        Assert.Empty(translation.Warnings);
        Assert.Equal(
            [
                "windowrulev2 = border_color rgba(33ccffee) 0xFF00FF99 45deg, class:^(run)$, float:false, fullscreen_state_client:2, workspace:w[tv1], ruleid:1, ruleon:0",
                "windowrulev2 = border_size 0, class:^(run)$, float:false, fullscreen_state_client:2, workspace:w[tv1], ruleid:1, ruleon:0",
                "windowrulev2 = float, class:^(run)$, float:false, fullscreen_state_client:2, workspace:w[tv1], ruleid:1, ruleon:0",
                "windowrulev2 = move 20 monitor_h-120, class:^(run)$, float:false, fullscreen_state_client:2, workspace:w[tv1], ruleid:1, ruleon:0",
                "windowrulev2 = no_focus false, class:^(run)$, float:false, fullscreen_state_client:2, workspace:w[tv1], ruleid:1, ruleon:0",
                "windowrulev2 = opacity 0.8, class:^(run)$, float:false, fullscreen_state_client:2, workspace:w[tv1], ruleid:1, ruleon:0",
                "windowrulev2 = float, class:^(run)$, float:false, fullscreen_state_client:2, workspace:w[tv1], ruleid:1, ruleon:0",
                "windowrulev2 = tile, title:Notes, ruleid:2, ruleon:1",
            ],
            Lines(translation));

        var document = Parse(translation);
        Assert.Empty(document.Warnings);
        Assert.Equal(8, document.WindowRules.Count);
        Assert.Equal(WindowRuleEffect.Tile, document.WindowRules[^1].Effect);
        Assert.All(document.WindowRules.Take(document.WindowRules.Count - 1), rule => Assert.False(rule.Active));
        Assert.True(document.WindowRules[^1].Active);
        Assert.Equal(1, document.WindowRules[0].RuleId);
        Assert.Equal(2, document.WindowRules[^1].RuleId);
    }

    [Fact]
    public void AnIgnoreRuleReachesTheDocumentsWindowRules()
    {
        var translation = Run("""
            hl.window_rule({ name = "skip-notepad", match = { class = "^notepad$", title = "negative:.*Settings.*" }, ignore = true })
            """);

        var document = Parse(translation);

        Assert.Empty(document.Warnings);
        var rule = Assert.Single(document.WindowRules);
        Assert.Equal(WindowRuleEffect.Ignore, rule.Effect);
        Assert.Equal("^notepad$", rule.ClassPattern);
        Assert.Equal("negative:.*Settings.*", rule.TitlePattern);
    }

    [Fact]
    public void WindowRulesThatCannotBeLinesWarn()
    {
        var translation = Run("""
            hl.window_rule({ match = { class = "a{1,3}" }, float = true })
            hl.window_rule({ match = { class = "kitty" }, workspace = "1, silent" })
            hl.window_rule({ float = true })
            hl.window_rule({ name = "off", enabled = false, match = { class = "kitty" }, float = true })
            hl.window_rule({ name = "twice", match = { class = "kitty" }, float = true })
            hl.window_rule({ name = "twice", match = { class = "foot" }, float = true })
            hl.window_rule({ match = { class = "kitty", xdg_tag = "x" }, pseudo = true })
            hl.window_rule("kitty")
            """);

        Assert.Equal(
            [
                "windowrulev2 = float, class:a{13}, ruleid:1, ruleon:1",
                "windowrulev2 = workspace 1 silent, class:kitty, ruleid:2, ruleon:1",
                "windowrulev2 = float, class:kitty, ruleid:4, ruleon:0",
                "windowrulev2 = float, class:foot, ruleid:5, ruleon:1",
                "windowrulev2 = float, class:foot, ruleid:5, ruleon:1",
                "windowrulev2 = pseudo, class:kitty, xdg_tag:x, ruleid:6, ruleon:1",
            ],
            Lines(translation));
        Assert.Equal(
            [
                "hl.window_rule: a rule with nothing to match on is never applied",
                "hl.window_rule: argument must be a table",
                "Unsupported window rule 'pseudo'.",
            ],
            Messages(Parse(translation)));
    }

    [Fact]
    public void LayerRulesAndPermissionsWarnAsTheirLinesDo()
    {
        var translation = Run("""
            local overlay = hl.layer_rule({ name = "no-anim-overlay", match = { namespace = "^my-overlay$" }, no_anim = true })
            overlay:set_enabled(false)
            overlay:is_enabled()
            hl.layer_rule({ name = "no-anim-overlay", match = { namespace = "^other$" }, blur = true })
            hl.permission("/usr/bin/grim", "screencopy", "allow")
            hl.permission({ binary = "/usr/bin/grim", type = "screencopy", mode = "allow" })
            hl.permission("/usr/bin/grim")
            """);

        Assert.Equal(
            [
                "layerrule = no_anim, namespace:^my-overlay$",
                "permission = /usr/bin/grim, screencopy, allow",
                "permission = /usr/bin/grim, screencopy, allow",
            ],
            Lines(translation));
        Assert.Equal(
            [
                "hl.layer_rule: the rule's set_enabled method has no Windows meaning. Windows has no layer surfaces",
                "hl.layer_rule: the rule's is_enabled method has no Windows meaning. Windows has no layer surfaces",
                "hl.layer_rule: layer surfaces have no Windows meaning, so naming \"no-anim-overlay\" again changes nothing",
                "hl.permission: expected binary, type, mode",
                "Unsupported keyword 'layerrule'.",
                "Unsupported keyword 'permission'.",
                "Unsupported keyword 'permission'.",
            ],
            Messages(Parse(translation)));
    }

    [Fact]
    public void WorkspaceRulesBecomeWorkspaceLines()
    {
        var translation = Run("""
            hl.workspace_rule({ workspace = "w[tv1]", gaps_out = 0, gaps_in = 0 })
            hl.workspace_rule({
                workspace = 3, monitor = "DP-1", default = true, persistent = true, gaps_in = { top = 5, right = 10 },
                border_size = 4, no_border = true, no_rounding = false, decorate = false, default_name = "web",
                layout = "dwindle", layout_opts = { force_split = 2 },
            })
            hl.workspace_rule({ workspace = "special:term", on_created_empty = "wt" })
            hl.workspace_rule({ workspace = "4", enabled = false, persistent = true })
            hl.workspace_rule({ workspace = "5", no_shadow = true, gapsin = 3 })
            """);

        Assert.Equal(
            [
                "workspace = w[tv1], gapsin:0 0 0 0, gapsout:0 0 0 0, ruleid:1, ruleon:1",
                "workspace = 3, monitor:DP-1, default:true, persistent:true, gapsin:5 10 0 0, bordersize:4, border:false, rounding:true, decorate:false, defaultName:web, layout:dwindle, layoutopt:force_split:2, ruleid:2, ruleon:1",
                "workspace = special:term, on-created-empty:wt, ruleid:3, ruleon:1",
                "workspace = 4, persistent:true, ruleid:4, ruleon:0",
                "workspace = 5, shadow:false, ruleid:5, ruleon:1",
            ],
            Lines(translation));

        var document = Parse(translation);
        Assert.Equal(["hl.workspace_rule: unknown field 'gapsin'"], Messages(document));
        Assert.False(Assert.Single(document.WorkspaceRules, rule => rule.WorkspaceString == "4").Active);
        Assert.False(Assert.Single(document.WorkspaceRules, rule => rule.WorkspaceString == "5").Shadow);
        var third = Assert.Single(document.WorkspaceRules, rule => rule.WorkspaceString == "3");
        Assert.Equal("DP-1", third.Monitor);
        Assert.Equal(new EdgeInsets(5, 10, 0, 0), third.GapsIn);
        Assert.False(third.Border);
        Assert.Equal("web", third.DefaultName);
        Assert.Equal("2", third.LayoutOpts["force_split"]);
    }

    [Fact]
    public void CurvesAndAnimationsBecomeBezierAndAnimationLines()
    {
        var translation = Run("""
            hl.curve("easeOutQuint", { type = "bezier", points = { {0.23, 1}, {0.32, 1} } })
            hl.curve("easy", { type = "spring", mass = 1, stiffness = 238.1191, dampening = 24.21279333 })
            hl.animation({ leaf = "windowsIn", enabled = true, speed = 4.1, bezier = "easeOutQuint", style = "popin 87%" })
            hl.animation({ leaf = "border", enabled = false })
            hl.animation({ leaf = "fade", speed = 3, bezier = "default" })
            hl.animation({ leaf = "windows", speed = 4.79, spring = "easy" })
            """);

        Assert.Equal(
            [
                "bezier = easeOutQuint, 0.23, 1, 0.32, 1",
                "spring = easy, 238.1191, 24.21279333, 1",
                "animation = windowsIn, 1, 4.1, easeOutQuint, popin 87%",
                "animation = border, 0",
                "animation = fade, 1, 3, default",
                "animation = windows, 1, 4.79, spring:easy",
            ],
            Lines(translation));
        Assert.Empty(translation.Warnings);
        Assert.Empty(Parse(translation).Warnings);
    }

    [Fact]
    public void CurvesAndAnimationsTheParserWouldRejectWarn()
    {
        var translation = Run("""
            hl.curve("bad", { type = "bezier", points = { {0.2, 1} } })
            hl.curve("far", { type = "bezier", points = { {0.2, 1}, {3, 1} } })
            hl.curve("soft", { type = "spring", mass = 1, stiffness = 0.2, dampening = 5 })
            hl.curve("odd", { type = "elastic" })
            hl.animation({ leaf = "fade", speed = 3, spring = "nosuch" })
            hl.animation({ leaf = "fade", bezier = "default" })
            hl.animation({ leaf = "fade", speed = 0, bezier = "default" })
            hl.animation({ leaf = "fade", speed = 1 })
            hl.animation({ leaf = "nosuch", speed = 1, bezier = "nosuch" })
            """);

        Assert.Equal(["animation = nosuch, 1, 1, nosuch"], Lines(translation));
        Assert.Equal(
            [
                "hl.curve(\"bad\"): \"points\" must contain exactly 2 points, e.g. { {0, 0}, {1, 1} }",
                "hl.curve(\"far\"): point 2[1]: expected a number from -1 to 2",
                "hl.curve(\"soft\"): stiffness expects a number >= 0.5",
                "hl.curve(\"odd\"): unknown curve type \"elastic\", expected \"bezier\" or \"spring\"",
                "hl.animation(\"fade\"): no such spring \"nosuch\"",
                "hl.animation(\"fade\"): missing required field \"speed\"",
                "hl.animation(\"fade\"): speed must be greater than 0",
                "hl.animation(\"fade\"): bezier or spring is required",
                "Animation 'nosuch' does not exist.",
            ],
            Messages(Parse(translation)));
    }

    [Fact]
    public void ASpringTakesDampingAndStillTheOldSpelling()
    {
        var translation = Run("""
            hl.curve("new", { type = "spring", mass = 1, stiffness = 238, damping = 24 })
            hl.curve("old", { type = "spring", mass = 1, stiffness = 238, dampening = 24 })
            hl.curve("none", { type = "spring", mass = 1, stiffness = 238 })
            """);

        Assert.Equal(
            [
                "hl.curve(\"none\"): damping expects a number",
            ],
            translation.Warnings.Select(warning => warning.Message));
        Assert.Equal(["spring = new, 238, 24, 1", "spring = old, 238, 24, 1"], Lines(translation));
    }

    [Fact]
    public void AGestureScaleIsFromATenthToTenEitherWay()
    {
        var translation = Run("""
            hl.gesture({ fingers = 3, direction = "left", action = "close", scale = -2 })
            hl.gesture({ fingers = 3, direction = "right", action = "close", scale = 0 })
            """);

        Assert.Equal(["gesture = 3, left, scale:-2, close"], Lines(translation));
        Assert.Equal(
            "hl.gesture: field \"scale\": value must be between -10 and -0.1 or between 0.1 and 10 - It's currently: 0",
            Assert.Single(translation.Warnings).Message);
    }

    [Fact]
    public void GesturesBecomeGestureLines()
    {
        var translation = Run("""
            hl.gesture({ fingers = 3, direction = "horizontal", action = "workspace" })
            hl.gesture({ fingers = 4, direction = "up", action = "special", workspace_name = "magic" })
            hl.gesture({ fingers = 3, direction = "down", action = "float", mode = "float", mods = "SUPER", scale = 1.5 })
            hl.gesture({ fingers = 4, direction = "left", action = "scroll_move", disable_inhibit = true })
            hl.gesture({ fingers = 3, direction = "pinch", action = "cursor_zoom", zoom_level = 2, mode = "mult" })
            hl.gesture({ fingers = 4, direction = "up", action = "unset" })
            """);

        Assert.Equal(
            [
                "gesture = 3, horizontal, workspace",
                "gesture = 4, up, special, magic",
                "gesture = 3, down, mod:SUPER, scale:1.5, float, float",
                "gesturep = 4, left, scrollMove",
                "gesture = 3, pinch, cursorZoom, 2, mult",
                "gesture = 4, up, unset",
            ],
            Lines(translation));

        var document = Parse(translation);
        Assert.Equal(
            ["Gesture flag 'p' is not honored."],
            Messages(document));
        Assert.Equal(["workspace", "float", "scrollMove", "cursorZoom"], document.Gestures.Select(gesture => gesture.Action));
        Assert.Equal(Modifier.Super, document.Gestures[1].Modifiers);
        Assert.Equal(1.5, document.Gestures[1].Scale);
    }

    [Fact]
    public void GesturesWithoutALineFormWarn()
    {
        var translation = Run("""
            hl.gesture({ fingers = 3, direction = "left", action = "teleport" })
            hl.gesture({ direction = "left", action = "close" })
            """);

        Assert.Empty(Lines(translation));
        Assert.Equal(
            [
                "hl.gesture: unknown action \"teleport\"",
                "hl.gesture: missing required field \"fingers\"",
            ],
            translation.Warnings.Select(warning => warning.Message));
    }

    [Fact]
    public void AGestureWhoseActionIsAFunctionNamesItsNumber()
    {
        var translation = Run("""
            hl.gesture({ fingers = 3, direction = "up", action = function() end })
            hl.gesture({ fingers = 4, direction = "down", mods = "SUPER", disable_inhibit = true, action = { start = function() end, finish = function() end } })
            hl.gesture({ fingers = 5, direction = "up", action = { start = 1 } })
            hl.gesture({ fingers = 5, direction = "up", action = { ["end"] = function() end } })
            hl.gesture({ fingers = 5, direction = "up", action = function() end, scale = 0 })
            """);

        Assert.Equal(["gesture = 3, up, luafunction, 1", "gesturep = 4, down, mod:SUPER, luafunction, 2"], Lines(translation));
        Assert.Equal(
            [
                "hl.gesture: action.start must be a function",
                "hl.gesture: action callback table must define at least one of start, update, end, or finish",
                "hl.gesture: field \"scale\": value must be between -10 and -0.1 or between 0.1 and 10 - It's currently: 0",
            ],
            translation.Warnings.Select(warning => warning.Message));

        var document = Parse(translation);
        Assert.Equal(["Gesture flag 'p' is not honored."], Messages(document).Skip(translation.Warnings.Count));
        Assert.Equal(
            new TrackpadGesture(3, GestureDirection.Up, Modifier.None, 1, TrackpadGesture.FunctionAction, Argument: "1"),
            document.Gestures[0]);
    }

    [Fact]
    public void DevicesBecomeDeviceLinesThatWarnAsTheyDo()
    {
        var translation = Run("""
            hl.device({ name = "epic mouse v1", sensitivity = -0.5, tap_to_click = false, wheel = 1 })
            hl.device({ sensitivity = 1 })
            """);

        Assert.Equal(
            ["device[epic-mouse-v1]:sensitivity = -0.5", "device[epic-mouse-v1]:tap-to-click = false"],
            Lines(translation));
        Assert.Equal(
            [
                "hl.device: unknown field 'wheel'",
                "hl.device: 'name' field is required and must be a string",
            ],
            Messages(Parse(translation)));
        Assert.Contains(new Setting("input:touchpad:tap-to-click", "false"), Parse(translation).Settings);
        Assert.Equal(new DevicePointer("epic-mouse-v1", -0.5, null, null), Assert.Single(Parse(translation).DevicePointers));
    }

    [Fact]
    public void ACommaInARuleMonitorGestureCurveOrEnvReachesTheParsedConfig()
    {
        var translation = Run("""
            hl.workspace_rule({
                workspace = "name:a, b",
                monitor = "desc:Dell, Inc",
                default_name = "Work, personal",
                on_created_empty = "notify-send hi, there",
                layout = "dwindle",
                animation = "slide, left",
                layout_opts = { note = "yes, no" },
            })
            hl.monitor({ output = "desc:Dell, Inc", mirror = "Dell, Inc", icc = "C:/color, profile.icc", reserved = 4 })
            hl.monitor({ output = "desc:Dell, Inc", disabled = true })
            hl.gesture({ fingers = 4, direction = "up", action = "special", workspace_name = "magic, extra", mods = "SUPER, SHIFT" })
            hl.curve("ease, out", { type = "bezier", points = { {0, 0}, {1, 1} } })
            hl.animation({ leaf = "windows", speed = 4, bezier = "ease, out", style = "slide, left" })
            hl.curve("soft, spring", { type = "spring", mass = 1, stiffness = 2, damping = 3 })
            hl.animation({ leaf = "windowsIn", speed = 4, spring = "soft, spring", style = "popin" })
            hl.env("A,B", "hello, world")
            hl.exec_cmd("notify-send hi, there", { tag = "work, personal" })
            """);

        Assert.Empty(translation.Warnings);
        Assert.Contains($"layoutopt:note:yes{LineText.Comma} no", translation.Text, StringComparison.Ordinal);
        var document = Parse(translation);
        Assert.Equal(["Monitor color management other than its cm preset is not honored."], Messages(document));

        var workspace = Assert.Single(document.WorkspaceRules);
        Assert.Equal("name:a, b", workspace.WorkspaceString);
        Assert.Equal("a, b", workspace.WorkspaceName);
        Assert.Equal("desc:Dell, Inc", workspace.Monitor);
        Assert.Equal("Work, personal", workspace.DefaultName);
        Assert.Equal("[workspace a, b] notify-send hi, there", workspace.OnCreatedEmpty);
        Assert.Equal("dwindle", workspace.Layout);
        Assert.Equal("slide, left", workspace.Animation);
        Assert.Empty(workspace.LayoutOpts);

        var monitors = MonitorRules.From(document);
        Assert.Equal(3, monitors.Count);
        Assert.All(monitors, rule => Assert.Equal("desc:Dell, Inc", rule.Selector));
        Assert.Equal("Dell, Inc", monitors[0].Mirror);

        Assert.Equal(
            new TrackpadGesture(4, GestureDirection.Up, Modifier.Super | Modifier.Shift, 1, TrackpadGesture.SpecialAction, Argument: "magic, extra"),
            Assert.Single(document.Gestures));

        var motion = AnimationOptions.From(document);
        Assert.Equal("ease, out", motion.Leaf("windows").Curve);
        Assert.Equal("slide, left", motion.Leaf("windows").Style);
        Assert.Equal("spring:soft, spring", motion.Leaf("windowsIn").Curve);

        Assert.Equal(new EnvironmentAssignment("A,B", "hello, world"), Assert.Single(document.Environment));

        var launch = Assert.Single(new Core().StartupLaunches(document));
        Assert.Equal("notify-send hi, there", launch.CommandLine);
        var tag = Assert.Single(launch.Rules);
        Assert.Equal(WindowRuleEffect.Tag, tag.Effect);
        Assert.Equal("work, personal", tag.Text);
    }

    [Fact]
    public void ACommaInADeviceOrPermissionIsWrittenAndStillWarnsAsThatKeyword()
    {
        var translation = Run("""
            hl.device({ name = "board", kb_layout = "us,se", region_position = "1, 2", sensitivity = "1,2" })
            hl.permission("app, helper", "screencopy", "allow")
            hl.permission("grim", "screen,copy", "allow")
            hl.permission("grim", "screencopy", "al,low")
            """);

        Assert.Equal(
            [
                "device[board]:kb_layout = us,se",
                "device[board]:region_position = 1, 2",
                $"permission = app{LineText.Comma} helper, screencopy, allow",
            ],
            Lines(translation));
        Assert.Equal(
            [
                "hl.device: field 'sensitivity': a value with a comma is not honored yet",
                "hl.permission: field 'type': a value with a comma is not honored yet",
                "hl.permission: field 'mode': a value with a comma is not honored yet",
                "Unsupported keyword 'permission'.",
            ],
            Messages(Parse(translation)));
    }

    [Fact]
    public void AMeaninglessCommaStillWarnsAndIsLeftOut()
    {
        var translation = Run("""
            hl.monitor({ output = "DP-1", mode = "a,b" })
            hl.workspace_rule({ workspace = "1", border_size = "2,0" })
            hl.gesture({ fingers = 3, direction = "up", action = "float", mode = "float,tile" })
            hl.gesture({ fingers = 4, direction = "down", action = "cursor_zoom", zoom_level = "1,5", mode = "live" })
            hl.animation({ leaf = "win,dows", speed = 1, bezier = "default" })
            """);

        Assert.Equal(
            [
                "monitor = DP-1, preferred, auto, auto",
                "workspace = 1, ruleid:1, ruleon:1",
                "gesture = 3, up, float",
                "gesture = 4, down, cursorZoom",
            ],
            Lines(translation));
        Assert.Equal(
            [
                "hl.monitor: field 'mode': a value with a comma is not honored yet",
                "hl.workspace_rule: field 'border_size': a value with a comma is not honored yet",
                "hl.gesture: field 'mode': a value with a comma is not honored yet",
                "hl.gesture: field 'zoom_level': a value with a comma is not honored yet",
                "hl.animation(\"win,dows\"): field 'leaf': a value with a comma is not honored yet",
            ],
            Messages(Parse(translation)));
        Assert.Null(Assert.Single(Parse(translation).WorkspaceRules).BorderSize);
    }

    // The example config of the pinned release, without its comments, with its own name taken out of the run window's
    // class, and without the misc option named after it, which ADR 0002 keeps out of the code. Its commented-out calls
    // are at the end.
    [Fact]
    public void TheExampleConfigLoadsWithOnlyTheWarningsForWhatWinlandDoesNotHonor()
    {
        var translation = Run("""
            hl.monitor({
                output   = "",
                mode     = "preferred",
                position = "auto",
                scale    = "auto",
            })
            local terminal    = "kitty"
            local fileManager = "dolphin"
            local menu        = "launcher"
            hl.env("XCURSOR_SIZE", "24")
            hl.config({
                general = {
                    gaps_in  = 5,
                    gaps_out = 20,
                    border_size = 2,
                    col = {
                        active_border   = { colors = {"rgba(33ccffee)", "rgba(00ff99ee)"}, angle = 45 },
                        inactive_border = "rgba(595959aa)",
                    },
                    resize_on_border = false,
                    allow_tearing = false,
                    layout = "dwindle",
                },
                decoration = {
                    rounding       = 10,
                    rounding_power = 2,
                    active_opacity   = 1.0,
                    inactive_opacity = 1.0,
                    shadow = {
                        enabled      = true,
                        range        = 4,
                        render_power = 3,
                        color        = 0xee1a1a1a,
                    },
                    blur = {
                        enabled   = true,
                        special   = false,
                        popups    = false,
                        input_methods = false,
                    },
                },
                animations = {
                    enabled = true,
                },
            })
            hl.curve("easeOutQuint",   { type = "bezier", points = { {0.23, 1},    {0.32, 1}    } })
            hl.curve("easeInOutCubic", { type = "bezier", points = { {0.65, 0.05}, {0.36, 1}    } })
            hl.curve("linear",         { type = "bezier", points = { {0, 0},       {1, 1}       } })
            hl.curve("almostLinear",   { type = "bezier", points = { {0.5, 0.5},   {0.75, 1}    } })
            hl.curve("quick",          { type = "bezier", points = { {0.15, 0},    {0.1, 1}     } })
            hl.curve("easy",           { type = "spring", mass = 1, stiffness = 238.1191, dampening = 24.21279333 })
            hl.animation({ leaf = "global",        enabled = true,  speed = 10,   bezier = "default" })
            hl.animation({ leaf = "border",        enabled = true,  speed = 5.39, bezier = "easeOutQuint" })
            hl.animation({ leaf = "windows",       enabled = true,  speed = 4.79, spring = "easy" })
            hl.animation({ leaf = "windowsIn",     enabled = true,  speed = 4.1,  spring = "easy",         style = "popin 87%" })
            hl.animation({ leaf = "windowsOut",    enabled = true,  speed = 1.49, bezier = "linear",       style = "popin 87%" })
            hl.animation({ leaf = "fadeIn",        enabled = true,  speed = 1.73, bezier = "almostLinear" })
            hl.animation({ leaf = "fadeOut",       enabled = true,  speed = 1.46, bezier = "almostLinear" })
            hl.animation({ leaf = "fade",          enabled = true,  speed = 3.03, bezier = "quick" })
            hl.animation({ leaf = "layers",        enabled = true,  speed = 3.81, bezier = "easeOutQuint" })
            hl.animation({ leaf = "layersIn",      enabled = true,  speed = 4,    bezier = "easeOutQuint", style = "fade" })
            hl.animation({ leaf = "layersOut",     enabled = true,  speed = 1.5,  bezier = "linear",       style = "fade" })
            hl.animation({ leaf = "fadeLayersIn",  enabled = true,  speed = 1.79, bezier = "almostLinear" })
            hl.animation({ leaf = "fadeLayersOut", enabled = true,  speed = 1.39, bezier = "almostLinear" })
            hl.animation({ leaf = "workspaces",    enabled = true,  speed = 1.94, bezier = "almostLinear", style = "fade" })
            hl.animation({ leaf = "workspacesIn",  enabled = true,  speed = 1.21, bezier = "almostLinear", style = "fade" })
            hl.animation({ leaf = "workspacesOut", enabled = true,  speed = 1.94, bezier = "almostLinear", style = "fade" })
            hl.animation({ leaf = "zoomFactor",    enabled = true,  speed = 7,    bezier = "quick" })
            hl.config({ dwindle = { preserve_split = true } })
            hl.config({ master = { new_status = "master" } })
            hl.config({ scrolling = { fullscreen_on_one_column = true } })
            hl.config({ misc = { force_default_wallpaper = -1 } })
            hl.config({
                input = {
                    kb_layout  = "us",
                    kb_variant = "",
                    kb_model   = "",
                    kb_options = "",
                    kb_rules   = "",
                    follow_mouse = 1,
                    sensitivity = 0,
                    touchpad = {
                        natural_scroll = false,
                    },
                },
            })
            hl.gesture({
                fingers = 3,
                direction = "horizontal",
                action = "workspace"
            })
            hl.device({
                name        = "epic-mouse-v1",
                sensitivity = -0.5,
            })
            local mainMod = "SUPER"
            hl.bind(mainMod .. " + Q", hl.dsp.exec_cmd(terminal))
            local closeWindowBind = hl.bind(mainMod .. " + C", hl.dsp.window.close())
            hl.bind(mainMod .. " + E", hl.dsp.exec_cmd(fileManager))
            hl.bind(mainMod .. " + V", hl.dsp.window.float({ action = "toggle" }))
            hl.bind(mainMod .. " + R", hl.dsp.exec_cmd(menu))
            hl.bind(mainMod .. " + J", hl.dsp.layout("togglesplit"))
            hl.bind(mainMod .. " + mouse:272", hl.dsp.window.drag(),   { mouse = true })
            local suppressMaximizeRule = hl.window_rule({
                name  = "suppress-maximize-events",
                match = { class = ".*" },

                suppress_event = "maximize",
            })
            hl.window_rule({
                name  = "fix-xwayland-drags",
                match = {
                    class      = "^$",
                    title      = "^$",
                    xwayland   = true,
                    float      = true,
                    fullscreen = false,
                    pin        = false,
                },

                no_focus = true,
            })
            hl.window_rule({
                name  = "move-run",
                match = { class = "run" },

                move  = "20 monitor_h-120",
                float = true,
            })

            hl.workspace_rule({ workspace = "w[tv1]", gaps_out = 0, gaps_in = 0 })
            hl.workspace_rule({ workspace = "f[1]",   gaps_out = 0, gaps_in = 0 })
            hl.window_rule({
                name  = "no-gaps-wtv1",
                match = { float = false, workspace = "w[tv1]" },
                border_size = 0,
                rounding    = 0,
            })
            suppressMaximizeRule:set_enabled(false)
            """);

        var document = Parse(translation);

        var lines = Lines(translation);
        Assert.Contains("monitor = , preferred, auto, auto", lines);
        Assert.Contains("bezier = easeOutQuint, 0.23, 1, 0.32, 1", lines);
        Assert.Contains("animation = windowsOut, 1, 1.49, linear, popin 87%", lines);
        Assert.Contains("gesture = 3, horizontal, workspace", lines);
        Assert.Contains("device[epic-mouse-v1]:sensitivity = -0.5", lines);
        Assert.Contains("windowrulev2 = move 20 monitor_h-120, class:run, ruleid:3, ruleon:1", lines);
        Assert.Contains("workspace = f[1], gapsin:0 0 0 0, gapsout:0 0 0 0, ruleid:2, ruleon:1", lines);
        Assert.Contains("windowrulev2 = rounding 0, float:false, workspace:w[tv1], ruleid:4, ruleon:1", lines);

        // What remains is what Winland does not honor: popup and input-method blur, tearing, the wallpaper,
        // the XKB files, layer shell animations, the suppress_event effect and XWayland.
        // hl.config lines come in Lua's table order, so the warnings are compared sorted.
        var messages = Messages(document);
        Assert.Equal(
            [
                "Layer shell surfaces have no Windows meaning.",
                "Layer shell surfaces have no Windows meaning.",
                "Layer shell surfaces have no Windows meaning.",
                "Layer shell surfaces have no Windows meaning.",
                "Layer shell surfaces have no Windows meaning.",
                "Unsupported keyword 'decoration:blur:input_methods'.",
                "Unsupported keyword 'decoration:blur:popups'.",
                "Unsupported keyword 'general:allow_tearing'.",
                "Unsupported keyword 'input:kb_model'.",
                "Unsupported keyword 'input:kb_rules'.",
                "Unsupported keyword 'misc:force_default_wallpaper'.",
                "Unsupported window rule 'suppress_event'.",
                "Window rules that match on xwayland are not honored.",
            ],
            messages.Order(StringComparer.Ordinal));

        Assert.Equal(new DevicePointer("epic-mouse-v1", -0.5, null, null), Assert.Single(document.DevicePointers));
        Assert.Equal(new EdgeInsets(0, 0, 0, 0), Assert.Single(document.WorkspaceRules, rule => rule.WorkspaceString == "f[1]").GapsOut);
        Assert.Contains(document.WindowRules, rule => rule.Effect == WindowRuleEffect.Move && rule.ClassPattern == "run");
        Assert.Single(document.Gestures);
    }
}
