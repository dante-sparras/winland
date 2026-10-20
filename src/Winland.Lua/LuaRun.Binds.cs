using System.Globalization;
using System.Text;
using KeraLua;

namespace Winland.Lua;

/// <summary>The modifiers and the key of a Lua key string, as a bind line writes them.</summary>
internal readonly record struct LuaKeys(string Modifiers, string Key, bool Chord);

// hl.bind, hl.unbind and hl.define_submap, turned into bind, unbind and submap lines.
internal sealed partial class LuaRun
{
    // The modifier names a key string starts with, as the pinned release reads them.
    private static readonly string[] ModifierNames =
        ["SHIFT", "CAPS", "CTRL", "CONTROL", "ALT", "MOD1", "MOD2", "MOD3", "SUPER", "WIN", "LOGO", "MOD4", "META", "MOD5"];

    // Modifier keys of one side, which a key string lists among the modifiers but which are keys of their own.
    private static readonly string[] SidedModifiers =
        ["SHIFT_L", "SHIFT_R", "CONTROL_L", "CTRL_L", "CONTROL_R", "CTRL_R", "ALT_L", "ALT_R", "SUPER_L", "SUPER_R"];

    // The options of hl.bind and the bind flag each one is, in the order the flags are written.
    private static readonly (string Option, char Flag)[] BindFlags =
    [
        ("locked", 'l'), ("release", 'r'), ("repeating", 'e'), ("long_press", 'o'), ("mouse", 'm'), ("non_consuming", 'n'),
        ("transparent", 't'), ("ignore_mods", 'i'), ("dont_inhibit", 'p'), ("click", 'c'), ("drag", 'g'), ("submap_universal", 'u'),
    ];

    private readonly Stack<(string Name, string Reset)> _submaps = new();

    // hl.bind(keys, dispatcher, options): "SUPER + SHIFT + Q" with hl.dsp.window.close() is "bind = SUPER SHIFT, Q, killactive".
    private int Bind(IntPtr state)
    {
        if (!_lua.IsString(1))
        {
            Warn("hl.bind: bad argument 1: expected a string");
            return 0;
        }

        if (ParseKeys(_lua.ToString(1, false), out var error) is not LuaKeys keys)
        {
            // The pinned release splits the string first and makes the bind from its keys after, and says which failed.
            var stage = error is "Empty key in key list" or "Repeated key in the key string" ? "failed to parse key string" : "failed to create bind";
            Warn($"hl.bind: {stage}: {error}");
            return 0;
        }

        if (_lua.IsCFunction(2))
        {
            Warn("hl.bind: dispatcher factory supplied instead of a dispatcher; call the factory first (missing parentheses?)");
            return 0;
        }

        // The handler a keybind object shows: the dispatcher as HL.Dispatcher(name), or the function.
        var handler = Show(2);

        // A Lua function runs when the keys are pressed, through Winland's own dispatcher. So does a dispatcher that is
        // a pipe command, such as reload_config, which no line dispatcher runs.
        var called = _lua.IsFunction(2) ? new Dispatch(LuaConfig.FunctionDispatcher, KeepFunction(2).ToString(CultureInfo.InvariantCulture)) : (Dispatch?)null;
        if ((called ?? ReadDispatch(2)) is not Dispatch dispatch)
        {
            Warn("hl.bind: expected a dispatcher (e.g. hl.dsp.window.close()) or a lua function");
            return 0;
        }

        if (dispatch.Command)
            dispatch = new Dispatch(LuaConfig.FunctionDispatcher, KeepDispatch(2).ToString(CultureInfo.InvariantCulture));

        if (dispatch.Unhonored is string unhonored)
        {
            WarnOnce(unhonored + " is not honored yet");
            return 0;
        }

        var flags = new StringBuilder();
        string? description = null;
        if (_lua.IsTable(3))
        {
            if (Present(3, "device"))
            {
                WarnOnce("hl.bind: binds for a device are not honored yet");
                return 0;
            }

            foreach (var (option, flag) in BindFlags)
            {
                if (Truthy(3, option))
                    flags.Append(flag);
            }

            if (Truthy(3, "auto_consuming"))
                WarnOnce("hl.bind: auto_consuming is not honored yet");

            description = Text(3, "description") ?? Text(3, "desc");
        }

        if (keys.Chord)
            flags.Append('s');

        if (description is not null)
            flags.Append('d');

        var mouse = flags.ToString().Contains('m', StringComparison.Ordinal);
        var line = new StringBuilder("bind").Append(flags).Append(" = ").Append(keys.Modifiers).Append(", ").Append(keys.Key);
        if (description is not null)
            line.Append(", ").Append(Escape(LineText.Escape(description)));

        // window.drag() and window.resize() are the mouse dispatcher, which a mouse bind runs as its action.
        if (mouse)
        {
            var action = dispatch.Dispatcher == "mouse" ? dispatch.Arguments : Join(dispatch.Dispatcher, dispatch.Arguments);
            line.Append(", ").Append(Escape(action));
        }
        else
        {
            line.Append(", ").Append(dispatch.Dispatcher);
            if (dispatch.Arguments.Length > 0)
                line.Append(", ").Append(Escape(dispatch.Arguments));
        }

        var text = line.ToString();
        var at = _lines.Count;
        if (!EmitLine(text, "hl.bind"))
            return 0;

        var record = new BindRecord(++_nextBind, text, $"unbind = {keys.Modifiers}, {keys.Key}")
        {
            Line = _lines.Count > at ? at : -1,
            DisplayKey = _lua.ToString(1, false),
            Handler = handler,
            Argument = dispatch.Arguments,
            Submap = _submaps.TryPeek(out var submap) ? submap.Name : "",
            ModifierMask = ModifierMask(keys.Modifiers),
            Key = keys.Key,
            Flags = flags.ToString(),
            Description = description,
        };
        _binds[record.Id] = record;
        _lua.RawGetInteger(LuaRegistry.Index, _makeKeybind);
        _lua.PushInteger(record.Id);
        if (_lua.PCall(1, 1, 0) != LuaStatus.OK)
        {
            _lua.Pop(1);
            return 0;
        }

        return 1;
    }

    // What a keybind object reads: the bind's line, the line that removes it, and its fields as the pinned release
    // shows them.
    private sealed record BindRecord(int Id, string BindLine, string UnbindLine)
    {
        public int Line { get; set; } = -1;

        public bool Enabled { get; set; } = true;

        public bool Removed { get; set; }

        public string DisplayKey { get; init; } = "";

        public string Handler { get; init; } = "";

        public string Argument { get; init; } = "";

        public string Submap { get; init; } = "";

        public long ModifierMask { get; init; }

        public string Key { get; init; } = "";

        public string Flags { get; init; } = "";

        public string? Description { get; init; }
    }

    private readonly Dictionary<int, BindRecord> _binds = [];
    private int _nextBind;
    private int _makeKeybind;

    // The modifier mask of the pinned release for the modifier names of a key string.
    private static long ModifierMask(string modifiers)
    {
        long mask = 0;
        foreach (var name in modifiers.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            mask |= name switch
            {
                "SHIFT" => 1L,
                "CAPS" => 2L,
                "CTRL" or "CONTROL" => 4L,
                "ALT" or "MOD1" => 8L,
                "MOD2" => 16L,
                "MOD3" => 32L,
                "SUPER" or "WIN" or "LOGO" or "MOD4" or "META" => 64L,
                "MOD5" => 128L,
                _ => 0L,
            };
        }

        return mask;
    }

    // The Lua function a bind runs for a dispatcher that is a pipe command: it calls hl.dispatch with the dispatcher.
    private int KeepDispatch(int index)
    {
        index = _lua.AbsIndex(index);
        if (_lua.LoadString("local d = ... return function() return hl.dispatch(d) end", "=bind") != LuaStatus.OK)
            throw new InvalidOperationException(_lua.ToString(-1));

        _lua.PushCopy(index);
        _lua.Call(1, 1);
        var id = KeepFunction(-1);
        _lua.Pop(1);
        return id;
    }

    // bindinfo(id): the fields of a keybind object, or nil once the bind is removed.
    private int BindInfo(IntPtr state)
    {
        if (!_lua.IsInteger(1) || !_binds.TryGetValue((int)_lua.ToInteger(1), out var bind) || bind.Removed)
        {
            _lua.PushNil();
            return 1;
        }

        _lua.NewTable();
        SetField("enabled", bind.Enabled);
        SetField("has_description", bind.Description is not null);
        if (bind.Description is not null)
            SetField("description", bind.Description);
        SetField("display_key", bind.DisplayKey);
        SetField("submap", bind.Submap);
        SetField("handler", bind.Handler);
        SetField("arg", bind.Argument);
        _lua.PushInteger(bind.ModifierMask);
        _lua.SetField(-2, "modmask");
        var catchAll = bind.Key == "catchall";
        var code = bind.Key.StartsWith("code:", StringComparison.Ordinal) && long.TryParse(bind.Key[5..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;
        SetField("key", catchAll || code != 0 ? "" : bind.Key[(bind.Key.LastIndexOf('&') + 1)..]);
        _lua.PushInteger(code);
        _lua.SetField(-2, "keycode");
        SetField("catchall", catchAll);
        foreach (var (option, flag) in BindFlags)
            SetField(option, bind.Flags.Contains(flag, StringComparison.Ordinal));

        SetField("auto_consuming", false);
        SetField("device_inclusive", false);
        SetField("allow_input_capture", false);
        _lua.NewTable();
        _lua.SetField(-2, "devices");
        return 1;

        void SetField(string name, object value)
        {
            if (value is bool flag)
                _lua.PushBoolean(flag);
            else
                _lua.PushString((string)value);

            _lua.SetField(-2, name);
        }
    }

    // bindenable(id, enabled): a disabled bind is unbound, and enabling it binds it again. While the config loads
    // its line is left out instead.
    private int BindEnable(IntPtr state)
    {
        if (!_lua.IsInteger(1) || !_binds.TryGetValue((int)_lua.ToInteger(1), out var bind) || bind.Removed)
            return 0;

        var enabled = _lua.ToBoolean(2);
        if (enabled == bind.Enabled)
            return 0;

        bind.Enabled = enabled;
        if (_phase != Phase.Running && bind.Line >= 0)
            _lines[bind.Line] = (enabled ? bind.BindLine : "", _lines[bind.Line].Origin);
        else
            Emit(enabled ? bind.BindLine : bind.UnbindLine);

        return 0;
    }

    // bindremove(id): removes the bind, as hl.unbind with its keys does.
    private int BindRemove(IntPtr state)
    {
        if (!_lua.IsInteger(1) || !_binds.TryGetValue((int)_lua.ToInteger(1), out var bind) || bind.Removed)
            return 0;

        bind.Removed = true;
        if (_phase != Phase.Running && bind.Line >= 0)
            _lines[bind.Line] = ("", _lines[bind.Line].Origin);
        else if (bind.Enabled)
            Emit(bind.UnbindLine);

        return 0;
    }

    // hl.unbind(keys) is "unbind = MODS, KEY", and hl.unbind("all") is "unbind = all".
    private int Unbind(IntPtr state)
    {
        if (!_lua.IsString(1))
        {
            Warn("hl.unbind: bad argument 1: expected a string");
            return 0;
        }

        var text = _lua.ToString(1, false);
        if (text == "all" && _lua.GetTop() == 1)
        {
            Emit("unbind = all");
            return 0;
        }

        if (ParseKeys(text, out var error) is not LuaKeys keys)
        {
            Warn("hl.unbind: failed to parse key string: " + error);
            return 0;
        }

        Emit($"unbind = {keys.Modifiers}, {keys.Key}");
        return 0;
    }

    // hl.define_submap(name, [reset,] fn): the binds fn makes go into the submap, and reset is the submap each of them
    // goes to after it runs. The submap around it comes back after fn.
    private int DefineSubmap(IntPtr state)
    {
        if (!_lua.IsString(1))
        {
            Warn("hl.define_submap: bad argument 1: expected a string");
            return 0;
        }

        var name = _lua.ToString(1, false);
        var reset = "";
        var body = 2;
        if (_lua.GetTop() >= 3 && _lua.IsString(2))
        {
            reset = _lua.ToString(2, false);
            body = 3;
        }

        if (!_lua.IsFunction(body))
        {
            Warn($"hl.define_submap: bad argument {body}: expected a function");
            return 0;
        }

        _submaps.Push((name, reset));
        Emit(SubmapLine(name, reset));
        _lua.PushCopy(body);
        if (_lua.PCall(0, 0, 0) != LuaStatus.OK)
        {
            NoteError(_lua.ToString(-1));
            _lua.Pop(1);
        }

        _submaps.Pop();
        Emit(_submaps.TryPeek(out var outer) ? SubmapLine(outer.Name, outer.Reset) : "submap = reset");
        return 0;
    }

    private static string SubmapLine(string name, string reset) =>
        reset.Length == 0 ? $"submap = {name}" : $"submap = {name}, {reset}";

    /// <summary>
    /// Reads a key string as the pinned release does: names joined by <c>+</c>, the modifier names first. More than one
    /// key is a chord, written with <c>&amp;</c>. Null with the reason when the string has no bind.
    /// </summary>
    internal static LuaKeys? ParseKeys(string text, out string error)
    {
        var names = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = new List<string>();
        var keys = new List<string>();
        var events = 0;
        foreach (var name in names)
        {
            if (name.Length == 0)
            {
                error = "Empty key in key list";
                return null;
            }

            if (names.Count(other => string.Equals(other, name, StringComparison.OrdinalIgnoreCase)) > 1)
            {
                error = "Repeated key in the key string";
                return null;
            }

            var upper = name.ToUpperInvariant();
            var modifier = ModifierNames.Contains(upper, StringComparer.Ordinal);
            var sided = SidedModifiers.Contains(upper, StringComparer.Ordinal);
            if ((modifier || sided) && keys.Count > 0 && !keys.All(IsSided))
            {
                error = "Modifiers cannot appear after keys";
                return null;
            }

            if (modifier)
            {
                modifiers.Add(upper);
                continue;
            }

            if (IsEvent(name))
                events++;

            keys.Add(name);
        }

        if (keys.Count == 0)
        {
            error = "A bind requires a trigger";
            return null;
        }

        if (events > 1 || (events > 0 && keys.Count > 1))
        {
            error = "External events cannot be combined with other keys";
            return null;
        }

        error = "";
        return new LuaKeys(string.Join(' ', modifiers), string.Join('&', keys), keys.Count > 1);
    }

    private static bool IsSided(string name) => SidedModifiers.Contains(name.ToUpperInvariant(), StringComparer.Ordinal);

    private static bool IsEvent(string name) =>
        name is "catchall" or "mouse_down" or "mouse_up" or "mouse_left" or "mouse_right"
        || name.StartsWith("mouse:", StringComparison.Ordinal)
        || name.StartsWith("switch:", StringComparison.Ordinal);

    // A '#' starts a comment in a config line, and "##" is one '#'.
    private static string Escape(string text) => text.Replace("#", "##", StringComparison.Ordinal);

    private static string Join(string first, string second) => second.Length == 0 ? first : first + " " + second;

    // A line holds no line break: a value with one would end it there.
    private bool EmitLine(string line, string label)
    {
        if (line.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            Warn($"{label}: a value with a line break is not honored");
            return false;
        }

        Emit(line);
        return true;
    }

    private void WarnOnce(string message, int level = 1)
    {
        if (_warnedPending.Add(message))
            Warn(message, level);
    }

    private bool Present(int table, string field)
    {
        var present = _lua.GetField(table, field) != LuaType.Nil;
        _lua.Pop(1);
        return present;
    }

    private bool Truthy(int table, string field)
    {
        _lua.GetField(table, field);
        var value = _lua.ToBoolean(-1);
        _lua.Pop(1);
        return value;
    }
}
