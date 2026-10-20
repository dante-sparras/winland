using System.Globalization;
using System.Text;
using KeraLua;

namespace Winland.Lua;

// hl.monitor, hl.window_rule, hl.layer_rule, hl.workspace_rule, hl.curve, hl.animation, hl.gesture, hl.device and
// hl.permission, turned into monitor, windowrulev2, layerrule, workspace, bezier, animation, gesture, device and
// permission lines. The parser then honors or warns about each line as it does in winland.conf.
internal sealed partial class LuaRun
{
    // The monitor fields a monitor line takes as an option after its scale, in the order they are written.
    private static readonly string[] MonitorOptions =
        ["transform", "mirror", "bitdepth", "cm", "sdr_eotf", "sdrbrightness", "sdrsaturation", "vrr", "icc"];

    // The monitor fields that describe a monitor's HDR range, which no monitor line option here takes.
    private static readonly string[] MonitorLuminance =
    [
        "supports_wide_color", "supports_hdr", "sdr_min_luminance", "sdr_max_luminance", "min_luminance", "max_luminance",
        "max_avg_luminance",
    ];

    private static readonly string[] MonitorFields = ["mode", "position", "scale", "reserved", "reserved_area", "disabled"];

    // The fields of hl.device, as the pinned release names them.
    private static readonly string[] DeviceFields =
    [
        "sensitivity", "accel_profile", "rotation", "kb_file", "kb_layout", "kb_variant", "kb_options", "kb_rules", "kb_model",
        "repeat_rate", "repeat_delay", "natural_scroll", "tap_button_map", "numlock_by_default", "resolve_binds_by_sym",
        "disable_while_typing", "clickfinger_behavior", "middle_button_emulation", "tap_to_click", "tap_and_drag", "drag_lock",
        "left_handed", "scroll_method", "scroll_button", "scroll_button_lock", "scroll_points", "scroll_factor", "transform",
        "output", "enabled", "region_position", "absolute_region_position", "region_size", "relative_input",
        "active_area_position", "active_area_size", "flip_x", "flip_y", "drag_3fg", "keybinds", "share_states",
        "release_pressed_on_close", "tags",
    ];

    // Device fields stored as text, including a vec2 written as text. A device line is not split at commas, so a comma
    // in one is written as itself.
    private static readonly HashSet<string> DeviceText = new(StringComparer.Ordinal)
    {
        "accel_profile", "kb_file", "kb_layout", "kb_variant", "kb_options", "kb_rules", "kb_model", "tap_button_map",
        "scroll_method", "scroll_points", "output", "tags", "region_position", "region_size", "active_area_position",
        "active_area_size",
    };

    // The fields of hl.workspace_rule and the workspace rule field each one is. A field whose value is negated is "!".
    private static readonly (string Field, string Rule)[] WorkspaceFields =
    [
        ("monitor", "monitor"), ("default", "default"), ("persistent", "persistent"), ("gaps_in", "gapsin"),
        ("gaps_out", "gapsout"), ("float_gaps", "float_gaps"), ("border_size", "bordersize"), ("no_border", "!border"),
        ("no_rounding", "!rounding"), ("decorate", "decorate"), ("no_shadow", "!shadow"), ("on_created_empty", "on-created-empty"),
        ("default_name", "defaultName"), ("layout", "layout"), ("animation", "animation"),
    ];

    private readonly HashSet<string> _springs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ruleNames = new(StringComparer.Ordinal);
    private int _handle;

    private void RegisterRules()
    {
        Function("monitor", AddMonitor);
        Function("window_rule", AddWindowRule);
        Function("layer_rule", state => AddRule("hl.layer_rule", "layerrule"));
        Function("workspace_rule", AddWorkspaceRule);
        Function("curve", AddCurve);
        Function("animation", AddAnimation);
        Function("gesture", AddGesture);
        Function("device", AddDevice);
        Function("permission", AddPermission);
    }

    // The rule calls return an object whose methods warn, so a config that calls one runs on.
    private void RegisterHandle()
    {
        _lua.NewTable();
        Function("warn", "a rule object", state =>
        {
            if (_lua.IsString(1) && _lua.IsString(2))
                WarnOnce($"{_lua.ToString(1, false)}: the rule's {_lua.ToString(2, false)} method is not honored yet", 2);
            return 0;
        });
        _lua.SetGlobal("__winland_handle_warn");
        if (_lua.DoString("""
            local warn = __winland_handle_warn.warn
            __winland_handle_warn = nil
            __winland_handle = function(label)
                return setmetatable({}, { __index = function(_, method)
                    return function() warn(label, tostring(method)) end
                end })
            end
            """))
        {
            throw new InvalidOperationException(_lua.ToString(-1));
        }

        _lua.GetGlobal("__winland_handle");
        _handle = _lua.Ref(LuaRegistry.Index);
        _lua.PushNil();
        _lua.SetGlobal("__winland_handle");
    }

    private int PushHandle(string label)
    {
        _lua.RawGetInteger(LuaRegistry.Index, _handle);
        _lua.PushString(label);
        if (_lua.PCall(1, 1, 0) != LuaStatus.OK)
        {
            NoteError(_lua.ToString(-1));
            _lua.Pop(1);
            return 0;
        }

        return 1;
    }

    // hl.monitor({ output, mode, position, scale, ... }): "monitor = output, mode, position, scale[, option, value]...",
    // "monitor = output, disable" when disabled, and "monitor = output, addreserved, top, bottom, left, right" for a
    // reserved area.
    private int AddMonitor(IntPtr state)
    {
        const string Label = "hl.monitor";
        if (!_lua.IsTable(1))
        {
            Warn("hl.monitor: argument must be a table");
            return 0;
        }

        if (StringField(1, "output") is not string output)
        {
            Warn("hl.monitor: 'output' field is required and must be a string");
            return 0;
        }

        output = LineText.Escape(output);

        foreach (var key in Keys(1))
        {
            if (key != "output" && !MonitorFields.Contains(key) && !MonitorOptions.Contains(key) && !MonitorLuminance.Contains(key))
                Warn($"hl.monitor: unknown field '{key}'");
        }

        if (Bool(1, "disabled") == true)
        {
            EmitLine($"monitor = {output}, disable", Label);
            return 0;
        }

        var line = new StringBuilder("monitor = ").Append(output)
            .Append(", ").Append(Part(1, "mode", Label) ?? "preferred")
            .Append(", ").Append(Part(1, "position", Label) ?? "auto")
            .Append(", ").Append(Part(1, "scale", Label) ?? "auto");
        foreach (var option in MonitorOptions)
        {
            if (Part(1, option, Label, option is "mirror" or "icc") is string value)
                line.Append(", ").Append(option).Append(", ").Append(value);
        }

        if (MonitorLuminance.FirstOrDefault(field => Present(1, field)) is string luminance)
            Warn($"hl.monitor: '{luminance}': monitor HDR ranges are not honored");

        EmitLine(line.ToString(), Label);
        if ((CssGap(1, "reserved", Label) ?? CssGap(1, "reserved_area", Label)) is { } reserved)
            EmitLine($"monitor = {output}, addreserved, {reserved.Top}, {reserved.Bottom}, {reserved.Left}, {reserved.Right}", Label);

        return 0;
    }

    // hl.layer_rule({ name, enabled, match = { key = value }, effect = value, ... }): one layerrule line for each effect.
    // A name already used warns and changes nothing. The object it returns only warns.
    private int AddRule(string label, string keyword)
    {
        if (!_lua.IsTable(1))
        {
            Warn($"{label}: argument must be a table");
            return 0;
        }

        var name = StringField(1, "name") ?? "";
        if (name.Length > 0 && !_ruleNames.Add(label + "\n" + name))
        {
            Warn($"{label}: changing the rule \"{name}\" again is not honored yet");
            return PushHandle(label);
        }

        if (Bool(1, "enabled") == false)
            return PushHandle(label);

        if (RuleMatches(label) is not List<string> matches)
            return PushHandle(label);

        if (matches.Count == 0)
        {
            Warn($"{label}: a rule with nothing to match on is never applied");
            return PushHandle(label);
        }

        var suffix = ", " + string.Join(", ", matches);
        foreach (var key in Keys(1))
        {
            if (key is "name" or "enabled" or "match")
                continue;

            if (RuleEffect(label, key) is string effect)
                EmitLine($"{keyword} = {effect}{suffix}", label);
        }

        return PushHandle(label);
    }

    // The "key:value" matches of the rule's match table, or null when one cannot be written, which warns.
    private List<string>? RuleMatches(string label)
    {
        var matches = new List<string>();
        _lua.GetField(1, "match");
        try
        {
            if (!_lua.IsTable(-1))
                return matches;

            var table = _lua.AbsIndex(-1);
            foreach (var key in Keys(table))
            {
                _lua.GetField(table, key);
                var value = _lua.Type(-1) switch
                {
                    LuaType.Boolean => _lua.ToBoolean(-1) ? "true" : "false",
                    LuaType.Number => NumberText(-1),
                    LuaType.String => _lua.ToString(-1, false),
                    _ => null,
                };
                _lua.Pop(1);
                if (value is null)
                {
                    Warn($"{label}: match value for '{key}' must be string, bool, or number");
                    continue;
                }

                matches.Add(key + ":" + LineText.Escape(value));
            }

            return matches;
        }
        finally
        {
            _lua.Pop(1);
        }
    }

    // The effect part of a rule line for field key of the rule table: "float", "no_focus false" or "move 20 30". A false
    // float is tile, a false tile is float, and a false ignore makes no line.
    private string? RuleEffect(string label, string key)
    {
        _lua.GetField(1, key);
        try
        {
            if (_lua.Type(-1) == LuaType.Boolean)
            {
                if (_lua.ToBoolean(-1))
                    return key;

                return key switch
                {
                    "float" => "tile",
                    "tile" => "float",
                    "ignore" => null,
                    _ => key + " false",
                };
            }

            var value = Value(-1, key);
            if (value is null)
            {
                Warn($"{label}: field '{key}': value must be a string, bool, number or table");
                return null;
            }

            return value.Length == 0 ? key : key + " " + LineText.Escape(value);
        }
        finally
        {
            _lua.Pop(1);
        }
    }

    // The "layoutopt:key:value" parts of a workspace rule's layout_opts table.
    private List<string> LayoutOpts(string label)
    {
        var parts = new List<string>();
        _lua.GetField(1, "layout_opts");
        try
        {
            if (_lua.IsNil(-1))
                return parts;

            if (!_lua.IsTable(-1))
            {
                Warn($"{label}: field 'layout_opts' must be a table");
                return parts;
            }

            var table = _lua.AbsIndex(-1);
            foreach (var key in Keys(table))
            {
                if (Part(table, key, label, commas: true) is string value)
                    parts.Add($"layoutopt:{key}:{value}");
            }

            return parts;
        }
        finally
        {
            _lua.Pop(1);
        }
    }

    // hl.curve(name, { type = "bezier", points = { { x0, y0 }, { x1, y1 } } }) is "bezier = name, x0, y0, x1, y1", and
    // hl.curve(name, { type = "spring", stiffness, damping, mass }) is "spring = name, stiffness, damping, mass".
    private int AddCurve(IntPtr state)
    {
        if (!_lua.IsStringOrNumber(1))
        {
            Warn("hl.curve: first argument (name) must be a string");
            return 0;
        }

        var name = _lua.ToString(1, false);
        var written = LineText.Escape(name);
        if (!_lua.IsTable(2))
        {
            Warn("hl.curve: second argument must be a table, e.g. { type = \"bezier\", points = { {0, 0}, {1, 1} } }");
            return 0;
        }

        var label = $"hl.curve(\"{name}\")";
        switch (StringField(2, "type"))
        {
            case null:
                Warn($"{label}: missing required field \"type\"");
                return 0;
            case "bezier":
                if (BezierPoints(label) is string points)
                    EmitLine($"bezier = {written}, {points}", "hl.curve");
                return 0;
            case "spring":
                var numbers = new List<string>(3);
                foreach (var field in (string[])["stiffness", "damping", "mass"])
                {
                    // damping was spelled dampening before, and the pinned release still reads that spelling.
                    if (_lua.GetField(2, field) == LuaType.Nil && field == "damping")
                    {
                        _lua.Pop(1);
                        _lua.GetField(2, "dampening");
                    }

                    var number = _lua.IsNumber(-1) ? _lua.ToNumber(-1) : (double?)null;
                    var text = _lua.IsNumber(-1) ? NumberText(-1) : "";
                    _lua.Pop(1);
                    if (number is not double value)
                    {
                        Warn($"{label}: {field} expects a number");
                        return 0;
                    }

                    if (value <= 0.5)
                    {
                        Warn($"{label}: {field} expects a number >= 0.5");
                        return 0;
                    }

                    numbers.Add(text);
                }

                if (EmitLine($"spring = {written}, {string.Join(", ", numbers)}", "hl.curve"))
                    _springs.Add(name);
                return 0;
            case var other:
                Warn($"{label}: unknown curve type \"{other}\", expected \"bezier\" or \"spring\"");
                return 0;
        }
    }

    // "x0, y0, x1, y1" from the curve's points, or null when they are not two points of two numbers, which warns.
    private string? BezierPoints(string label)
    {
        _lua.GetField(2, "points");
        try
        {
            if (!_lua.IsTable(-1))
            {
                Warn($"{label}: missing or invalid \"points\" field, expected a table of two points");
                return null;
            }

            var points = _lua.AbsIndex(-1);
            if (_lua.RawLen(points) != 2)
            {
                Warn($"{label}: \"points\" must contain exactly 2 points, e.g. {{ {{0, 0}}, {{1, 1}} }}");
                return null;
            }

            var coordinates = new List<string>(4);
            for (var point = 1; point <= 2; point++)
            {
                _lua.RawGetInteger(points, point);
                try
                {
                    if (!_lua.IsTable(-1) || _lua.RawLen(-1) != 2)
                    {
                        Warn($"{label}: point {point} must be a table of 2 numbers, e.g. {{0.25, 0.1}}");
                        return null;
                    }

                    for (var part = 1; part <= 2; part++)
                    {
                        _lua.RawGetInteger(-1, part);
                        var number = _lua.IsNumber(-1) ? _lua.ToNumber(-1) : (double?)null;
                        var text = _lua.IsNumber(-1) ? NumberText(-1) : "";
                        _lua.Pop(1);
                        if (number is not double value || value < -1 || value > 2)
                        {
                            Warn($"{label}: point {point}[{part}]: expected a number from -1 to 2");
                            return null;
                        }

                        coordinates.Add(text);
                    }
                }
                finally
                {
                    _lua.Pop(1);
                }
            }

            return string.Join(", ", coordinates);
        }
        finally
        {
            _lua.Pop(1);
        }
    }

    // hl.animation({ leaf, enabled, speed, bezier | spring, style }) is "animation = leaf, 1, speed, curve[, style]", where
    // a spring's curve is "spring:name" as in the pinned release, or "animation = leaf, 0" when it is not enabled.
    private int AddAnimation(IntPtr state)
    {
        if (!_lua.IsTable(1))
        {
            Warn("hl.animation: expected a table, e.g. { leaf = \"global\", enabled = true, speed = 5, bezier = \"default\" }");
            return 0;
        }

        if (StringField(1, "leaf") is not string leaf)
        {
            Warn("hl.animation: missing required field \"leaf\"");
            return 0;
        }

        var label = $"hl.animation(\"{leaf}\")";
        if (leaf.Contains(','))
        {
            Warn($"{label}: field 'leaf': a value with a comma is not honored yet");
            return 0;
        }

        if (Bool(1, "enabled") == false)
        {
            EmitLine($"animation = {leaf}, 0", "hl.animation");
            return 0;
        }

        _lua.GetField(1, "speed");
        var speed = _lua.IsNumber(-1) ? NumberText(-1) : null;
        var positive = _lua.IsNumber(-1) && _lua.ToNumber(-1) > 0;
        _lua.Pop(1);
        if (speed is null)
        {
            Warn($"{label}: missing required field \"speed\"");
            return 0;
        }

        if (!positive)
        {
            Warn($"{label}: speed must be greater than 0");
            return 0;
        }

        string curve;
        if (Present(1, "bezier"))
        {
            if (StringField(1, "bezier") is not string bezier)
            {
                Warn($"{label}: field \"bezier\" must be a string");
                return 0;
            }

            curve = LineText.Escape(bezier);
        }
        else if (Present(1, "spring"))
        {
            var spring = StringField(1, "spring") ?? "";
            if (!_springs.Contains(spring))
            {
                Warn($"{label}: no such spring \"{spring}\"");
                return 0;
            }

            curve = "spring:" + LineText.Escape(spring);
        }
        else
        {
            Warn($"{label}: bezier or spring is required");
            return 0;
        }

        var style = Present(1, "style") ? Part(1, "style", "hl.animation", commas: true) : null;
        EmitLine(
            string.IsNullOrEmpty(style) ? $"animation = {leaf}, 1, {speed}, {curve}" : $"animation = {leaf}, 1, {speed}, {curve}, {style}",
            "hl.animation");
        return 0;
    }

    // hl.gesture({ fingers, direction, action, mods, scale, disable_inhibit, ... }) is
    // "gesture = fingers, direction[, mod:MODS][, scale:S], action[, argument]", "gesturep" with disable_inhibit. An
    // action that is a Lua function, or a table of them, is "luafunction, N".
    private int AddGesture(IntPtr state)
    {
        const string Label = "hl.gesture";
        if (!_lua.IsTable(1))
        {
            Warn("hl.gesture: expected a table, e.g. { fingers = 3, direction = \"horizontal\", action = \"workspace\" }");
            return 0;
        }

        if (Number(1, "fingers") is not string fingers)
        {
            Warn("hl.gesture: missing required field \"fingers\"");
            return 0;
        }

        if (StringField(1, "direction") is not string direction)
        {
            Warn("hl.gesture: missing required field \"direction\"");
            return 0;
        }

        // An action that is a Lua function, or a table of them, is Winland's luafunction action with the number they
        // are kept under.
        var (function, error) = KeepGestureAction();
        if (error is not null)
        {
            Warn(error);
            return 0;
        }

        IEnumerable<string>? arguments;
        if (function is int id)
        {
            arguments = [TrackpadGesture.FunctionAction, id.ToString(CultureInfo.InvariantCulture)];
        }
        else
        {
            if (StringField(1, "action") is not string action)
            {
                Warn("hl.gesture: missing required field \"action\"");
                return 0;
            }

            arguments = action switch
            {
                "workspace" or "resize" or "move" or "close" or "unset" => [action],
                "scroll_move" => ["scrollMove"],
                "special" => Given(Part(1, "workspace_name", Label, commas: true)).Prepend("special"),
                "float" or "fullscreen" => Given(Part(1, "mode", Label)).Prepend(action),
                "cursor_zoom" or "cursorZoom" => Given(Part(1, "zoom_level", Label), Part(1, "mode", Label)).Prepend("cursorZoom"),
                _ => null,
            };

            if (arguments is null)
            {
                Warn($"hl.gesture: unknown action \"{action}\"");
                return 0;
            }
        }

        var line = new StringBuilder(Bool(1, "disable_inhibit") == true ? "gesturep" : "gesture")
            .Append(" = ").Append(fingers).Append(", ").Append(direction);
        if (Part(1, "mods", Label, commas: true) is string mods)
            line.Append(", mod:").Append(mods);
        if (Number(1, "scale") is string scale)
        {
            // A scale is from 0.1 to 10 either way: a negative one reverses the gesture.
            var value = double.Parse(scale, CultureInfo.InvariantCulture);
            if (Math.Abs(value) < 0.1 || Math.Abs(value) > 10)
            {
                if (function is int dropped)
                    DropCallbacks(dropped);

                Warn($"hl.gesture: field \"scale\": value must be between -10 and -0.1 or between 0.1 and 10 - It's currently: {scale}");
                return 0;
            }

            line.Append(", scale:").Append(scale);
        }
        foreach (var argument in arguments)
            line.Append(", ").Append(argument);

        EmitLine(line.ToString(), Label);
        return 0;
    }

    // The words up to the first that is missing.
    private static IEnumerable<string> Given(params string?[] words) => words.TakeWhile(word => !string.IsNullOrEmpty(word))!;

    // hl.device({ name, field = value, ... }) is "device[name]:field = value" for each field.
    private int AddDevice(IntPtr state)
    {
        const string Label = "hl.device";
        if (!_lua.IsTable(1))
        {
            Warn("hl.device: argument must be a table");
            return 0;
        }

        if (StringField(1, "name") is not string name)
        {
            Warn("hl.device: 'name' field is required and must be a string");
            return 0;
        }

        name = name.Replace(' ', '-');
        foreach (var key in Keys(1))
        {
            if (key == "name")
                continue;

            if (!DeviceFields.Contains(key))
            {
                Warn($"hl.device: unknown field '{key}'");
                continue;
            }

            if (Part(1, key, Label, DeviceText.Contains(key), escape: false) is string value)
            {
                var option = key switch
                {
                    "tap_to_click" => "tap-to-click",
                    "tap_and_drag" => "tap-and-drag",
                    _ => key,
                };
                EmitLine($"device[{name}]:{option} = {value}", Label);
            }
        }

        return 0;
    }

    // hl.permission(binary, type, mode) or hl.permission({ binary, type, mode }) is "permission = binary, type, mode".
    private int AddPermission(IntPtr state)
    {
        string? binary;
        string? type;
        string? mode;
        if (_lua.IsTable(1))
        {
            binary = StringField(1, "binary") ?? StringField(1, "target");
            type = StringField(1, "type");
            mode = StringField(1, "mode");
            if (binary is null || type is null || mode is null)
            {
                Warn("hl.permission: expected { binary, type, mode }");
                return 0;
            }
        }
        else
        {
            binary = _lua.IsString(1) ? _lua.ToString(1, false) : null;
            type = _lua.IsString(2) ? _lua.ToString(2, false) : null;
            mode = _lua.IsString(3) ? _lua.ToString(3, false) : null;
            if (binary is null || type is null || mode is null)
            {
                Warn("hl.permission: expected binary, type, mode");
                return 0;
            }
        }

        if (binary.Length == 0)
        {
            Warn("hl.permission: binary must not be empty");
            return 0;
        }

        if (type.Contains(',', StringComparison.Ordinal))
        {
            Warn("hl.permission: field 'type': a value with a comma is not honored yet");
            return 0;
        }

        if (mode.Contains(',', StringComparison.Ordinal))
        {
            Warn("hl.permission: field 'mode': a value with a comma is not honored yet");
            return 0;
        }

        EmitLine($"permission = {LineText.Escape(binary)}, {type}, {mode}", "hl.permission");
        return 0;
    }

    // The string keys of the table at index, in order.
    private List<string> Keys(int table)
    {
        table = _lua.AbsIndex(table);
        var keys = new List<string>();
        _lua.PushNil();
        while (_lua.Next(table))
        {
            if (_lua.Type(-2) == LuaType.String)
                keys.Add(_lua.ToString(-2, false));

            _lua.Pop(1);
        }

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    // A string or number field as text, or null for any other value.
    private string? StringField(int table, string field)
    {
        string? value = _lua.GetField(table, field) switch
        {
            LuaType.String => _lua.ToString(-1, false),
            LuaType.Number => NumberText(-1),
            _ => null,
        };
        _lua.Pop(1);
        return value;
    }

    private bool? Bool(int table, string field)
    {
        var type = _lua.GetField(table, field);
        bool? value = type == LuaType.Boolean ? _lua.ToBoolean(-1) : null;
        _lua.Pop(1);
        return value;
    }

    // The config text of a field for a line whose values are split at commas, or null when it is absent or cannot be
    // written there, which warns. commas keeps a comma the pinned release's Lua takes: escape writes the private-use
    // character when the line splits at commas, and leaves the comma when the line does not.
    private string? Part(int table, string field, string label, bool commas = false, bool escape = true)
    {
        _lua.GetField(table, field);
        try
        {
            if (_lua.IsNil(-1))
                return null;

            var value = Value(-1, field);
            if (value is null)
            {
                Warn($"{label}: field '{field}' has a value of a type it cannot take");
                return null;
            }

            if (!value.Contains(',', StringComparison.Ordinal))
                return value;

            if (!commas)
            {
                Warn($"{label}: field '{field}': a value with a comma is not honored yet");
                return null;
            }

            return escape ? LineText.Escape(value) : value;
        }
        finally
        {
            _lua.Pop(1);
        }
    }

    // A gap per side: a number for every side, or { top, right, bottom, left } with 0 for a side it leaves out.
    private (long Top, long Right, long Bottom, long Left)? CssGap(int table, string field, string label)
    {
        _lua.GetField(table, field);
        try
        {
            if (_lua.IsNil(-1))
                return null;

            if (_lua.IsNumber(-1))
            {
                var all = (long)_lua.ToNumber(-1);
                return (all, all, all, all);
            }

            if (!_lua.IsTable(-1))
            {
                Warn($"{label}: field '{field}': css_gap type requires an integer or a table with optional \"top\", \"right\", \"bottom\", \"left\" fields");
                return null;
            }

            var sides = new long[4];
            var names = (string[])["top", "right", "bottom", "left"];
            for (var i = 0; i < names.Length; i++)
            {
                _lua.GetField(-1, names[i]);
                var nil = _lua.IsNil(-1);
                var number = _lua.IsNumber(-1);
                sides[i] = number ? (long)_lua.ToNumber(-1) : 0;
                _lua.Pop(1);
                if (!nil && !number)
                {
                    Warn($"{label}: field '{field}': css_gap \"{names[i]}\" must be an integer");
                    return null;
                }
            }

            return (sides[0], sides[1], sides[2], sides[3]);
        }
        finally
        {
            _lua.Pop(1);
        }
    }
}
