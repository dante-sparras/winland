using System.Globalization;
using KeraLua;

namespace Winland.Lua;

// hl.dsp: each constructor returns a table that names the line-based dispatcher with the same effect and its argument,
// which hl.bind writes into a bind line. A constructor with no such dispatcher, or a form of one, returns a table that
// says so, and the bind that takes it warns once and makes no line.
internal sealed partial class LuaRun
{
    private const string DispatcherField = "__winland_dispatcher";
    private const string ArgumentsField = "__winland_arguments";
    private const string UnhonoredField = "__winland_unhonored";
    private const string NameField = "__winland_name";
    private const string CommandField = "__winland_command";

    private int _dispatcherMetatable;

    private enum Toggle
    {
        Toggle,
        Enable,
        Disable,
    }

    // Command is a pipe command rather than a dispatcher, such as reload, which hl.dispatch sends as it is and a bind
    // runs through a Lua function.
    private readonly record struct Dispatch(string Dispatcher, string Arguments, string? Unhonored = null, bool Command = false);

    // A config mistake in a constructor's arguments: the constructor warns with it and returns nothing.
    private sealed class DispatchArgumentException(string message) : Exception(message);

    private void RegisterDispatchers()
    {
        _lua.NewTable();

        _lua.NewTable();
        Constructor("cursor", "move_to_corner", CursorMoveToCorner);
        Constructor("cursor", "move", CursorMove);
        _lua.SetField(-2, "cursor");

        _lua.NewTable();
        Constructor("group", "toggle", label => new Dispatch("togglegroup", WithWindow("")));
        Constructor("group", "next", label => new Dispatch("changegroupactive", WithWindow("f")));
        Constructor("group", "prev", label => new Dispatch("changegroupactive", WithWindow("b")));
        Constructor("group", "active", GroupActive);
        Constructor("group", "move_window", label => new Dispatch("movegroupwindow", Bool("forward") == false ? "b" : ""));
        Constructor("group", "lock", GroupLock);
        Constructor("group", "lock_active", GroupLockActive);
        _lua.SetField(-2, "group");

        _lua.NewTable();
        Constructor("window", "close", label => Window() is string window ? new Dispatch("closewindow", window) : new Dispatch("killactive", ""));
        Constructor("window", "kill", label => new Dispatch("killwindow", Window() ?? "activewindow"));
        Constructor("window", "signal", label => NotHonored(label));
        Constructor("window", "float", WindowFloat);
        Constructor("window", "fullscreen", WindowFullscreen);
        Constructor("window", "fullscreen_state", WindowFullscreenState);
        Constructor("window", "pseudo", label => Action() == Toggle.Toggle ? new Dispatch("pseudo", Window() ?? "") : NotHonored(label + " with an action other than toggle"));
        Constructor("window", "move", WindowMove);
        Constructor("window", "swap", WindowSwap);
        Constructor("window", "center", label => new Dispatch("centerwindow", WithWindow("")));
        Constructor("window", "cycle_next", WindowCycleNext);
        Constructor("window", "tag", label => new Dispatch("tagwindow", Join(Required("tag"), Window() ?? "")));
        Constructor("window", "clear_tags", label => NotHonored(label));
        Constructor("window", "toggle_swallow", label => new Dispatch("toggleswallow", ""), 0);
        Constructor("window", "pin", label => new Dispatch("pin", Join(Action() switch { Toggle.Enable => "enable", Toggle.Disable => "disable", _ => "" }, Window() ?? "")));
        Constructor("window", "bring_to_top", label => new Dispatch("bringactivetotop", ""), 0);
        Constructor("window", "alter_zorder", label => new Dispatch("alterzorder", WithSelector(Required("mode"), Window())));
        Constructor("window", "set_prop", label => new Dispatch("setprop", $"{Window() ?? "activewindow"} {Required("prop")} {Required("value")}"));
        Constructor("window", "deny_from_group", label => new Dispatch("denywindowfromgroup", Action() switch { Toggle.Enable => "on", Toggle.Disable => "off", _ => "toggle" }));
        Constructor("window", "drag", label => new Dispatch("mouse", "movewindow"), 0);
        Constructor("window", "resize", WindowResize);
        _lua.SetField(-2, "window");

        _lua.NewTable();
        Constructor("workspace", "rename", label => new Dispatch("renameworkspace", Join(Required("workspace"), Text(1, "name") ?? "")));
        Constructor("workspace", "change_id", label => new Dispatch("changeworkspaceid", $"{Required("workspace")} {Required("id")}"));
        Constructor("workspace", "move", WorkspaceMove);
        Constructor("workspace", "swap_monitors", label => new Dispatch("swapactiveworkspaces", $"{Required("monitor1")} {Required("monitor2")}"));
        Constructor("workspace", "toggle_special", label => new Dispatch("togglespecialworkspace", _lua.IsStringOrNumber(1) ? _lua.ToString(1, false) : ""));
        _lua.SetField(-2, "workspace");

        Constructor(null, "exec_cmd", ExecCommand, 2);
        Constructor(null, "exec_raw", label => new Dispatch("execr", Argument()));
        Constructor(null, "exit", label => new Dispatch("exit", ""), 0);
        Constructor(null, "reload_config", label => new Dispatch("reload", "", Command: true), 0);
        Constructor(null, "submap", label => new Dispatch("submap", Argument()));
        Constructor(null, "pass", label => new Dispatch("pass", Required("window")));
        Constructor(null, "send_shortcut", SendShortcut);
        Constructor(null, "send_key_state", SendKeyState);
        Constructor(null, "layout", label => new Dispatch("layoutmsg", Argument()));
        Constructor(null, "dpms", Dpms);
        Constructor(null, "event", label => new Dispatch("event", Argument()));
        Constructor(null, "global", label => NotHonored(label));
        Constructor(null, "force_renderer_reload", label => new Dispatch("forcerendererreload", ""), 0);
        Constructor(null, "force_idle", label => NotHonored(label));
        Constructor(null, "release_input_capture", label => NotHonored(label), 0);
        Constructor(null, "focus", Focus);
        Constructor(null, "no_op", label => new Dispatch("no_op", ""), 0);

        _lua.SetField(-2, "dsp");
    }

    // Sets field name of the table on top of the stack to a constructor that pushes the table for what build returns.
    // The table shows as HL.Dispatcher(name), as the pinned release's dispatcher does, and a constructor given more
    // arguments than it takes warns and returns nothing.
    private void Constructor(string? group, string name, Func<string, Dispatch> build, int maxArguments = 1)
    {
        var label = group is null ? $"hl.dsp.{name}" : $"hl.dsp.{group}.{name}";
        // A DispatchArgumentException warns with the label and returns nothing, as any exception in a callback does.
        Function(name, label, state =>
        {
            var given = _lua.GetTop();
            if (given > maxArguments)
            {
                Warn($"{name}: expected at most {maxArguments} argument{(maxArguments == 1 ? "" : "s")}, got {given}");
                return 0;
            }

            var dispatch = build(label);
            _lua.CreateTable(0, 4);
            _lua.PushString(name);
            _lua.SetField(-2, NameField);
            _lua.RawGetInteger(LuaRegistry.Index, DispatcherMetatable());
            _lua.SetMetaTable(-2);
            if (dispatch.Unhonored is string unhonored)
            {
                _lua.PushString(unhonored);
                _lua.SetField(-2, UnhonoredField);
                return 1;
            }

            _lua.PushString(dispatch.Dispatcher);
            _lua.SetField(-2, DispatcherField);
            _lua.PushString(dispatch.Arguments);
            _lua.SetField(-2, ArgumentsField);
            if (dispatch.Command)
            {
                _lua.PushBoolean(true);
                _lua.SetField(-2, CommandField);
            }

            return 1;
        });
    }

    // The metatable of the dispatcher tables: tostring shows the constructor's name.
    private int DispatcherMetatable()
    {
        if (_dispatcherMetatable != 0)
            return _dispatcherMetatable;

        if (_lua.DoString($$"""
            return { __name = "HL.Dispatcher", __tostring = function(self) return "HL.Dispatcher(" .. tostring(rawget(self, "{{NameField}}")) .. ")" end }
            """))
        {
            throw new InvalidOperationException(_lua.ToString(-1));
        }

        _dispatcherMetatable = _lua.Ref(LuaRegistry.Index);
        return _dispatcherMetatable;
    }

    // The dispatch a constructor's table at index names, or null for any other value.
    private Dispatch? ReadDispatch(int index)
    {
        if (!_lua.IsTable(index))
            return null;

        if (Text(index, UnhonoredField) is string unhonored)
            return new Dispatch("", "", unhonored);

        return Text(index, DispatcherField) is string dispatcher
            ? new Dispatch(dispatcher, Text(index, ArgumentsField) ?? "", Command: Text(index, CommandField) == "true")
            : null;
    }

    private static Dispatch NotHonored(string what) => new("", "", what);

    // The line dispatcher acts on the focused window only, so a constructor given another window is not honored.
    // A dispatcher that acts on the active window takes another one after a comma, which the Lua form names.
    private string WithWindow(string argument) => WithSelector(argument, Window());

    private Dispatch? NoWindow(string label) => Window() is null ? null : NotHonored(label + " with a window");

    private static string WithSelector(string argument, string? window) => window is null ? argument : argument + "," + window;

    private Dispatch CursorMoveToCorner(string label) =>
        new Dispatch("movecursortocorner", WithWindow(Required("corner")));

    private Dispatch CursorMove(string label) =>
        new("movecursor", $"{Required("x")} {Required("y")}");

    private Dispatch GroupActive(string label) =>
        new Dispatch("changegroupactive", WithWindow(Required("index")));

    // No argument toggles the lock of the focused window's group. { all = true } locks every group, as lockgroups does.
    private Dispatch GroupLock(string label)
    {
        if (!_lua.IsTable(1))
            return _lua.IsNoneOrNil(1) ? new Dispatch("lockactivegroup", "toggle") : throw Mistake("expected a table { window?, all?, action? } or no args");

        if (Bool("all") == true)
            return new Dispatch("lockgroups", LockWord(Action()));

        return new Dispatch("lockactivegroup", WithWindow(LockWord(Action())));
    }

    // The pinned release keeps lock_active as group.lock without all, and notes that it is deprecated.
    private Dispatch GroupLockActive(string label)
    {
        const string Notice = "hl.dsp.group.lock_active(): The group.lock_active() dispatcher is deprecated, use group.lock() instead";
        if (_warnedPending.Add(Notice))
        {
            // A deprecation is a notice, not an error: an eval that uses it still succeeds.
            if (_phase == Phase.Running)
                _world?.Log("lua: " + Notice);
            else
                Warn(Notice);
        }

        return new Dispatch("lockactivegroup", LockWord(Action()));
    }

    private static string LockWord(Toggle action) => action switch
    {
        Toggle.Enable => "lock",
        Toggle.Disable => "unlock",
        _ => "toggle",
    };

    private Dispatch WindowFloat(string label)
    {
        var dispatcher = Action() switch
        {
            Toggle.Enable => "setfloating",
            Toggle.Disable => "settiled",
            _ => "togglefloating",
        };
        return new Dispatch(dispatcher, Window() ?? "");
    }

    // { mode = "fullscreen" | "maximized", action = "toggle" | "set" | "unset", layout_aware }: "fullscreen MODE [ACTION] [default]".
    private Dispatch WindowFullscreen(string label)
    {
        var mode = "0";
        var action = "";
        var layoutAware = true;
        if (_lua.IsTable(1))
        {
            mode = Text(1, "mode") switch
            {
                null or "fullscreen" or "0" => "0",
                "maximized" or "1" => "1",
                var other => throw Mistake($"invalid mode \"{other}\" (expected fullscreen/maximized)"),
            };
            action = Text(1, "action") switch
            {
                null or "toggle" => "",
                "set" => "set",
                "unset" => "unset",
                var other => throw Mistake($"invalid action \"{other}\" (expected toggle/set/unset)"),
            };
            layoutAware = Bool("layout_aware") ?? true;
        }

        return new Dispatch("fullscreen", WithWindow(Words(mode, action, layoutAware ? "" : "default")));
    }

    // { internal, client, action = "set", layout_aware, window }: "fullscreenstate INTERNAL CLIENT ACTION [default] [WINDOW]".
    private Dispatch WindowFullscreenState(string label)
    {
        if (!_lua.IsTable(1))
            throw Mistake("expected a table { internal, client, action?, window? }");

        var action = Text(1, "action") switch
        {
            null or "set" => "set",
            "toggle" => "toggle",
            "unset" => "unset",
            var other => throw Mistake($"invalid action \"{other}\" (expected toggle/set/unset)"),
        };
        if (Number(1, "internal") is not string internalMode || Number(1, "client") is not string clientMode)
            throw Mistake("'internal' and 'client' are required");

        var layoutAware = Bool("layout_aware") ?? true;
        return new Dispatch("fullscreenstate", Words(internalMode, clientMode, action, layoutAware ? "" : "default", Window() ?? ""));
    }

    private Dispatch WindowMove(string label)
    {
        if (!_lua.IsTable(1))
            throw Mistake("expected a table, e.g. { direction = \"left\" }");

        if (Text(1, "direction") is string direction)
        {
            var letter = Direction(direction) ?? throw Mistake($"invalid direction \"{direction}\" (expected left/right/up/down)");
            return NoWindow(label) ?? new Dispatch(Bool("group_aware") == true ? "movewindoworgroup" : "movewindow", letter);
        }

        if (Number(1, "x") is string x && Number(1, "y") is string y)
            return new Dispatch("movewindowpixel", WithSelector(Bool("relative") == true ? $"{x} {y}" : $"exact {x} {y}", Window() ?? "activewindow"));

        var silent = Bool("follow") == false;
        if (Text(1, "workspace") is string workspace)
            return new Dispatch(silent ? "movetoworkspacesilent" : "movetoworkspace", WithSelector(workspace, Window()));

        if (Text(1, "monitor") is string monitor)
            return NoWindow(label) ?? new Dispatch("movewindow", silent ? $"mon:{monitor} silent" : $"mon:{monitor}");

        if (Text(1, "into_group") is string into)
        {
            var letter = Direction(into) ?? throw Mistake($"invalid into_group direction \"{into}\"");
            return new Dispatch("moveintogroup", WithWindow(letter));
        }

        if (Text(1, "into_or_create_group") is string create)
        {
            var letter = Direction(create) ?? throw Mistake($"invalid into_or_create_group direction \"{create}\"");
            return new Dispatch("moveintoorcreategroup", WithWindow(letter));
        }

        if (Bool("out_of_group") is bool outOfGroup)
        {
            if (outOfGroup)
                return new Dispatch("moveoutofgroup", Window() ?? "");
        }
        else if (Text(1, "out_of_group") is not null)
        {
            return NotHonored(label + " with an out_of_group direction");
        }

        throw Mistake("unrecognized arguments. Expected one of: direction, x+y(+relative), workspace, into_group, out_of_group");
    }

    private Dispatch WindowSwap(string label)
    {
        if (!_lua.IsTable(1))
            throw Mistake("expected a table, e.g. { direction = \"left\" }");

        if (Text(1, "direction") is string direction)
        {
            var letter = Direction(direction) ?? throw Mistake($"invalid direction \"{direction}\" (expected left/right/up/down)");
            return new Dispatch("swapwindow", WithWindow(letter));
        }

        if ((Text(1, "target") ?? Text(1, "with") ?? Text(1, "other")) is string target)
            return new Dispatch("swapwindow", WithWindow(target));

        if (Bool("next") == true || Bool("prev") == true)
            return NotHonored(label + " with next or prev");

        throw Mistake("unrecognized arguments. Expected one of: direction, target/with/other, next, prev");
    }

    private Dispatch WindowCycleNext(string label)
    {
        var words = new List<string>();
        if (_lua.IsTable(1))
        {
            if (Bool("next") == false)
                words.Add("prev");
            if (Bool("tiled") == true)
                words.Add("tiled");
            if (Bool("floating") == true)
                words.Add("floating");
        }

        return NoWindow(label) ?? new Dispatch("cyclenext", string.Join(' ', words));
    }

    // No argument, or { keep_aspect_ratio }, is the mouse resize. { x, y, relative?, window? } resizes to or by a size.
    private Dispatch WindowResize(string label)
    {
        if (_lua.IsNoneOrNil(1))
            return new Dispatch("mouse", "resizewindow");

        if (!_lua.IsTable(1))
            throw Mistake("expected no args, a table { x, y, relative?, window? }, or a table { keep_aspect_ratio }");

        if (Number(1, "x") is string x && Number(1, "y") is string y)
        {
            var size = Bool("relative") == true ? $"{x} {y}" : $"exact {x} {y}";
            return Window() is string window ? new Dispatch("resizewindowpixel", size + "," + window) : new Dispatch("resizeactive", size);
        }

        if (Bool("keep_aspect_ratio") is bool keep)
            return new Dispatch("mouse", keep ? "resizewindow 1" : "resizewindow 2");

        throw Mistake("unrecognized arguments. Expected positions (x & y) or keep_aspect_ratio");
    }

    private Dispatch WorkspaceMove(string label)
    {
        var monitor = Required("monitor");
        return Text(1, "workspace") is string workspace
            ? new Dispatch("moveworkspacetomonitor", $"{workspace} {monitor}")
            : new Dispatch("movecurrentworkspacetomonitor", monitor);
    }

    private Dispatch ExecCommand(string label) => new("exec", ExecRules(2) + Argument());

    private Dispatch SendShortcut(string label)
    {
        var mods = Required("mods");
        var key = Required("key");
        return new Dispatch("sendshortcut", Window() is string window ? $"{mods}, {key}, {window}" : $"{mods}, {key}");
    }

    private Dispatch SendKeyState(string label)
    {
        var mods = Required("mods");
        var key = Required("key");
        var state = Required("state");
        if (state is not ("down" or "up" or "repeat"))
            throw Mistake("'state' must be \"down\", \"up\", or \"repeat\"");

        return new Dispatch("sendkeystate", $"{mods}, {key}, {state}, {Window() ?? ""}".TrimEnd());
    }

    private Dispatch Dpms(string label)
    {
        var action = Action() switch
        {
            Toggle.Enable => "on",
            Toggle.Disable => "off",
            _ => "toggle",
        };
        return new Dispatch("dpms", Join(action, _lua.IsTable(1) ? Text(1, "monitor") ?? "" : ""));
    }

    private Dispatch Focus(string label)
    {
        if (!_lua.IsTable(1))
            throw Mistake("expected a table, e.g. { direction = \"left\" }");

        if (Text(1, "direction") is string direction)
            return new Dispatch("movefocus", Direction(direction) ?? throw Mistake($"invalid direction \"{direction}\" (expected left/right/up/down)"));

        if (Text(1, "monitor") is string monitor)
            return new Dispatch("focusmonitor", monitor);

        if (Text(1, "workspace") is string workspace)
            return new Dispatch(Bool("on_current_monitor") == true ? "focusworkspaceoncurrentmonitor" : "workspace", workspace);

        if (Text(1, "window") is string window)
            return new Dispatch("focuswindow", window);

        if (Bool("urgent_or_last") == true)
            return new Dispatch("focusurgentorlast", "");

        if (Bool("last") == true)
            return NotHonored(label + " with last");

        throw Mistake("unrecognized arguments. Expected one of: direction, monitor, window, urgent_or_last, last");
    }

    // The line form of a direction: l, r, u or d, or null for no direction.
    private static string? Direction(string text) => text switch
    {
        "left" or "l" => "l",
        "right" or "r" => "r",
        "up" or "u" or "t" => "u",
        "down" or "d" or "b" => "d",
        _ => null,
    };

    private static string Words(params string[] words) => string.Join(' ', words.Where(word => word.Length > 0));

    private static DispatchArgumentException Mistake(string message) => new(message);

    // The first argument as text, which the constructor needs.
    private string Argument() =>
        _lua.IsStringOrNumber(1) ? _lua.ToString(1, false) : throw Mistake("bad argument 1: expected a string");

    // A field of the table argument as text, which the constructor needs.
    private string Required(string field)
    {
        if (!_lua.IsTable(1))
            throw Mistake($"expected a table with '{field}'");

        return Text(1, field) ?? throw Mistake($"'{field}' is required");
    }

    // The window selector the table argument names, or null for the focused window.
    private string? Window() => _lua.IsTable(1) ? Text(1, "window") : null;

    // The toggle the table argument's action names: enable or on, disable or off, and anything else toggles.
    private Toggle Action() => (_lua.IsTable(1) ? Text(1, "action") : null) switch
    {
        "enable" or "on" => Toggle.Enable,
        "disable" or "off" => Toggle.Disable,
        _ => Toggle.Toggle,
    };

    private bool? Bool(string field)
    {
        if (!_lua.IsTable(1))
            return null;

        var type = _lua.GetField(1, field);
        bool? value = type == LuaType.Boolean ? _lua.ToBoolean(-1) : null;
        _lua.Pop(1);
        return value;
    }

    // A string, number or boolean field as config text, a whole number without a fraction, a window, workspace or
    // monitor object as the selector that names it, or null for any other value.
    private string? Text(int table, string field)
    {
        string? value = _lua.GetField(table, field) switch
        {
            LuaType.String => _lua.ToString(-1, false),
            LuaType.Number => NumberText(-1),
            LuaType.Boolean => _lua.ToBoolean(-1) ? "true" : "false",
            LuaType.UserData => SelectorOf(-1),
            _ => null,
        };
        _lua.Pop(1);
        return value;
    }

    private string? Number(int table, string field)
    {
        _lua.GetField(table, field);
        var value = _lua.IsNumber(-1) ? NumberText(-1) : null;
        _lua.Pop(1);
        return value;
    }

    private string NumberText(int index)
    {
        if (_lua.IsInteger(index))
            return _lua.ToInteger(index).ToString(CultureInfo.InvariantCulture);

        var number = _lua.ToNumber(index);
        return number == Math.Floor(number) && Math.Abs(number) < 1e15
            ? ((long)number).ToString(CultureInfo.InvariantCulture)
            : number.ToString("R", CultureInfo.InvariantCulture);
    }
}
