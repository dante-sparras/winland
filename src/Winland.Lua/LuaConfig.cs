using System.Globalization;
using System.Text;
using KeraLua;

namespace Winland.Lua;

/// <summary>Where a config line made from a Lua call came from: the line of the call and its file, null for the config itself.</summary>
public readonly record struct LuaOrigin(int Line, string? Source);

/// <summary>
/// A Lua config turned into the line-based config: one line for each setting a call made, where each came from, what
/// went wrong while it ran, and the Lua files it required.
/// </summary>
public sealed record LuaTranslation(
    string Text,
    IReadOnlyList<LuaOrigin> Origins,
    IReadOnlyList<ConfigWarning> Warnings,
    IReadOnlyList<string> Sources)
{
    /// <summary>
    /// The parsed document with its warnings moved to the Lua lines that made them, after the warnings the Lua run
    /// itself gave, and with the required files as its sources.
    /// </summary>
    public ConfigDocument Remap(ConfigDocument parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        var warnings = new List<ConfigWarning>(Warnings);
        foreach (var warning in parsed.Warnings)
        {
            if (warning.Source is null && warning.Line >= 1 && warning.Line <= Origins.Count)
            {
                var origin = Origins[warning.Line - 1];
                warnings.Add(warning with { Line = origin.Line, Source = origin.Source });
            }
            else
            {
                warnings.Add(warning);
            }
        }

        return parsed with { Warnings = warnings, Sources = [.. parsed.Sources, .. Sources] };
    }
}

/// <summary>
/// Runs a Lua config, <c>winland.lua</c>, and turns its <c>hl</c> calls into the line-based config, so the parser and
/// the core read it as they read <c>winland.conf</c>. The Lua runs without the libraries that reach the system: no
/// <c>io</c>, no <c>os.execute</c>, no C modules and no binary chunks, and <c>require</c> reads Lua files, by module
/// name from the config's folder or by path, through the same reader, because Winland runs elevated (ADR 0007, ADR 0013).
/// </summary>
public static class LuaConfig
{
    /// <summary>How long a config may run as it loads before it is stopped, in seconds of processor time.</summary>
    public const double Budget = 1.5;

    /// <summary>The dispatcher a bind that calls a Lua function names, with the function's id. It is Winland's own.</summary>
    public const string FunctionDispatcher = "luafunction";

    /// <summary>Runs <paramref name="code"/>, read from <paramref name="path"/>. <paramref name="read"/> reads a required file by its full path.</summary>
    public static LuaTranslation Translate(string code, string path, Func<string, string?> read) => Translate(code, path, read, Budget);

    /// <summary>
    /// Runs <paramref name="code"/>, read from <paramref name="path"/>. <paramref name="source"/> reads a required file
    /// by its full path and lists the files a wildcard <c>require</c> matches.
    /// </summary>
    public static LuaTranslation Translate(string code, string path, IConfigSource source) => Translate(code, path, source, Budget);

    /// <summary>
    /// Runs the config as <see cref="Translate(string, string, Func{string, string?})"/> does and keeps its Lua, so a
    /// <see cref="LuaRuntime"/> can run its binds, timers and evals while Winland runs.
    /// </summary>
    public static LuaLoad Load(string code, string path, Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return Load(code, path, new Reader(read));
    }

    /// <summary>
    /// Runs the config as <see cref="Translate(string, string, IConfigSource)"/> does and keeps its Lua, so a
    /// <see cref="LuaRuntime"/> can run its binds, timers and evals while Winland runs.
    /// </summary>
    public static LuaLoad Load(string code, string path, IConfigSource source)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(source);
        var run = new LuaRun(path, source);
        try
        {
            return new LuaLoad(run.Translate(code, Budget), new LuaSession(run));
        }
        catch
        {
            run.Dispose();
            throw;
        }
    }

    internal static LuaTranslation Translate(string code, string path, Func<string, string?> read, double budget)
    {
        ArgumentNullException.ThrowIfNull(read);
        return Translate(code, path, new Reader(read), budget);
    }

    internal static LuaTranslation Translate(string code, string path, IConfigSource source, double budget)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(source);
        using var run = new LuaRun(path, source);
        return run.Translate(code, budget);
    }

    // A reader alone, which matches no wildcard.
    private sealed class Reader(Func<string, string?> read) : IConfigSource
    {
        public string? Read(string path) => read(path);
    }
}

// One run of a config. The delegates Lua calls back into are kept in fields so the collector leaves them alone, and a
// callback never raises a Lua error: it notes a warning and returns, because an error would unwind past managed frames.
internal sealed partial class LuaRun : IDisposable
{
    // The events hl.on knows, named as Winland names them with Winland's own name for its start and shutdown.
    // window.bell, screenshare.state, layer.opened and layer.closed are accepted and never raised. Windows does not
    // report a window ringing the bell or a screenshare starting, and it has no layer surfaces.
    private static readonly string[] Events =
    [
        "window.open", "window.open_early", "window.close", "window.destroy", "window.kill", "window.active",
        "window.urgent", "window.title", "window.class", "window.pin", "window.fullscreen", "window.update_rules",
        "window.move_to_workspace", "window.bell", "window.minimize", "layer.opened", "layer.closed", "monitor.added",
        "monitor.removed", "monitor.focused", "monitor.layout_changed", "workspace.active", "workspace.special_active",
        "workspace.created", "workspace.removed", "workspace.move_to_monitor", "config.reloaded",
        "config.props_refreshed", "config.unload", "keybinds.submap", "screenshare.state", StartEvent, ShutdownEvent, "input.keyboard.key",
    ];

    private const string StartEvent = "winland.start";
    private const string ShutdownEvent = "winland.shutdown";

    // Runs before the config: takes away what reaches the system, limits load to text and require to Lua files read
    // through the config's reader, and stops a config that runs too long. The hook is set before debug goes away.
    private const string Prelude = """
        local clock = os.clock
        local started, budget, label = clock(), ...
        debug.sethook(function()
            if budget and clock() - started > budget then
                error(label .. " ran longer than " .. budget .. " seconds", 2)
            end
        end, "", 100000)

        local system = os
        os = { time = system.time, clock = system.clock, date = system.date, getenv = system.getenv, difftime = system.difftime }
        io, debug, dofile, loadfile = nil, nil, nil, nil

        local textOnly = load
        load = function(chunk, name, _, env) return textOnly(chunk, name, "t", env) end

        local readModule, matchModules, failed = __winland_require, __winland_match, __winland_require_failed
        __winland_require, __winland_match, __winland_require_failed = nil, nil, nil
        package.loadlib, package.cpath, package.path = nil, "", ""
        package.loaded.io, package.loaded.debug, package.loaded.os = nil, nil, os
        package.searchers = { package.searchers[1], function(name)
            local text, file = readModule(name)
            if not text then
                return "\n\tno file for '" .. name .. "'"
            end
            local chunk, problem = textOnly(text, "@" .. file, "t")
            if not chunk then
                error(problem, 0)
            end
            return chunk, file
        end }

        -- A module that is not found is an error. One that fails as it runs warns where it failed and gives an empty
        -- table, and the config runs on. A path with a wildcard requires each file it matches, in order, and gives a
        -- table of what each returned.
        local requireOne = require
        require = function(name, ...)
            local loaded = type(name) == "string" and package.loaded[name]
            if loaded then
                return loaded
            end
            local files = type(name) == "string" and matchModules(name)
            if not files then
                local results = table.pack(pcall(requireOne, name, ...))
                if results[1] then
                    return table.unpack(results, 2, results.n)
                end
                local problem = tostring(results[2])
                if problem:sub(1, #("module '" .. tostring(name) .. "' not found")) == "module '" .. tostring(name) .. "' not found" then
                    error(problem, 0)
                end
                failed(problem)
                local empty = {}
                if type(name) == "string" then
                    package.loaded[name] = empty
                end
                return empty
            end
            if #files == 0 then
                error("module '" .. name .. "' not found: wildcard found no match", 2)
            end
            local results = {}
            for index, file in ipairs(files) do
                local ok, value = pcall(requireOne, file)
                if ok then
                    if value == nil then value = true end
                    results[index] = value
                else
                    failed(value)
                    results[index] = {}
                end
            end
            package.loaded[name] = results
            return results
        end

        return function(seconds, what) started, budget, label = clock(), seconds, what end
        """;

    private readonly string _path;
    private readonly string _directory;
    private readonly IConfigSource _source;
    private readonly KeraLua.Lua _lua = new();
    private readonly List<(string Text, LuaOrigin Origin)> _lines = [];
    private readonly List<ConfigWarning> _warnings = [];
    private readonly List<string> _sources = [];
    private readonly HashSet<string> _warnedPending = new(StringComparer.Ordinal);
    private readonly List<LuaFunction> _kept = [];
    private Phase _phase;

    private enum Phase
    {
        Load,
        Start,
        Shutdown,
        Running,
    }

    public LuaRun(string path, IConfigSource source)
    {
        _path = Path.GetFullPath(path);
        _directory = Path.GetDirectoryName(_path) ?? ".";
        _source = source;
    }

    public LuaTranslation Translate(string code, double budget)
    {
        _lua.OpenLibs();
        Register();
        if (_lua.LoadString(Prelude, "=prelude") != LuaStatus.OK)
            throw new InvalidOperationException(_lua.ToString(-1));

        _lua.PushNumber(budget);
        _lua.PushString("the config");
        if (_lua.PCall(2, 1, 0) != LuaStatus.OK)
            throw new InvalidOperationException(_lua.ToString(-1));

        _arm = _lua.Ref(LuaRegistry.Index);

        if (_lua.LoadString(code, "@" + Path.GetFileName(_path)) != LuaStatus.OK || _lua.PCall(0, 0, 0) != LuaStatus.OK)
            NoteError(_lua.ToString(-1));

        Arm(0, "");

        var text = new StringBuilder();
        var origins = new List<LuaOrigin>(_lines.Count);
        foreach (var (line, origin) in _lines)
        {
            text.Append(line).Append('\n');
            origins.Add(origin);
        }

        return new LuaTranslation(text.ToString(), origins, _warnings, _sources);
    }

    public void Dispose() => _lua.Dispose();

    private void Register()
    {
        _lua.NewTable();
        Function("config", Config);
        Function("env", Env);
        Function("exec_cmd", ExecCmd);
        Function("on", On);
        Function("bind", Bind);
        Function("unbind", Unbind);
        Function("define_submap", DefineSubmap);
        Function("dispatch", DispatchNow);
        Function("timer", Timer);
        Function("is_key_down", IsKeyDown);
        RegisterRules();
        RegisterLayouts();
        Function("exec_scheduled_prop_refresh_immediately", state =>
        {
            RunScheduledRefresh(scheduled: false);
            return 0;
        });
        Function("clear_crashed_lockscreen", state => 0);

        RegisterDispatchers();

        _lua.SetGlobal("hl");
        RegisterObjects();
        RegisterHandle();
        Keep(Print);
        _lua.PushCFunction(_kept[^1]);
        _lua.SetGlobal("print");
        Keep(RequireModule);
        _lua.PushCFunction(_kept[^1]);
        _lua.SetGlobal("__winland_require");
        Keep(MatchModules);
        _lua.PushCFunction(_kept[^1]);
        _lua.SetGlobal("__winland_match");
        Keep(RequireFailed);
        _lua.PushCFunction(_kept[^1]);
        _lua.SetGlobal("__winland_require_failed");
    }

    private void Function(string name, LuaFunction body) => Function(name, "hl." + name, body);

    // Sets field name of the table on top of the stack to a callback that warns as label.
    private void Function(string name, string label, LuaFunction body)
    {
        Keep(state =>
        {
            // A call from a coroutine has a stack of its own, which these callbacks do not read.
            if (state != _lua.Handle)
            {
                Warn($"{label}: cannot be called from a coroutine");
                return 0;
            }

            try
            {
                return body(state);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Warn($"{label}: {exception.Message}");
                return 0;
            }
        });
        _lua.PushCFunction(_kept[^1]);
        _lua.SetField(-2, name);
    }

    private void Keep(LuaFunction function) => _kept.Add(function);

    // hl.config({ section = { key = value } }): each value whose key is not a section becomes "section:key = value".
    private int Config(IntPtr state)
    {
        if (!_lua.IsTable(1))
        {
            Warn("hl.config: argument must be a table");
            return 0;
        }

        Walk("", 1);
        return 0;
    }

    private void Walk(string prefix, int table)
    {
        table = _lua.AbsIndex(table);
        _lua.PushNil();
        while (_lua.Next(table))
        {
            if (_lua.Type(-2) != LuaType.String)
            {
                _lua.Pop(1);
                continue;
            }

            var key = prefix.Length == 0 ? _lua.ToString(-2, false) : prefix + "." + _lua.ToString(-2, false);
            if (_lua.IsTable(-1) && !IsValueTable(-1))
                Walk(key, -1);
            else if (Value(-1, OptionName(key)) is string value)
                Emit($"{OptionName(key)} = {value}");
            else
                Warn($"hl.config: '{key}' has a value of a type it cannot take");

            _lua.Pop(1);
        }
    }

    // A table that is one option's value rather than a section: a gradient, a gap per side, or a list such as a vector.
    private bool IsValueTable(int index)
    {
        if (_lua.RawLen(index) > 0)
            return true;

        foreach (var field in (string[])["colors", "top", "right", "bottom", "left"])
        {
            _lua.GetField(index, field);
            var present = !_lua.IsNil(-1);
            _lua.Pop(1);
            if (present)
                return true;
        }

        return false;
    }

    // The option a Lua path names. Winland names an option in Lua by its name with ':' as '.' and '-' as
    // '_', so a "col" part joins the next with a '.' again, and the three names with a '-' get it back.
    internal static string OptionName(string path)
    {
        var parts = path.Split('.');
        var name = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
                name.Append(parts[i - 1] == "col" ? '.' : ':');

            name.Append(parts[i]);
        }

        return name.ToString() switch
        {
            "input:touchpad:tap_and_drag" => "input:touchpad:tap-and-drag",
            "input:touchpad:tap_to_click" => "input:touchpad:tap-to-click",
            var other when other.StartsWith("input_capture:", StringComparison.Ordinal) => "input-capture:" + other["input_capture:".Length..],
            var other => other,
        };
    }

    // A color option, which takes a number as the color's 0xAARRGGBB value.
    private static bool IsColor(string option)
    {
        var last = option[(option.LastIndexOf(':') + 1)..];
        return last.StartsWith("col.", StringComparison.Ordinal)
            || (last.Contains("color", StringComparison.Ordinal) && !last.Contains("colored", StringComparison.Ordinal));
    }

    // The config text of the value at index, or null when it has none.
    private string? Value(int index, string option)
    {
        index = _lua.AbsIndex(index);
        switch (_lua.Type(index))
        {
            case LuaType.Boolean:
                return _lua.ToBoolean(index) ? "true" : "false";
            case LuaType.Number when _lua.IsInteger(index):
                var integer = _lua.ToInteger(index);
                return IsColor(option)
                    ? "0x" + ((uint)integer).ToString("X8", CultureInfo.InvariantCulture)
                    : integer.ToString(CultureInfo.InvariantCulture);
            case LuaType.Number:
                return _lua.ToNumber(index).ToString("R", CultureInfo.InvariantCulture);
            case LuaType.String:
                return _lua.ToString(index, false);
            case LuaType.Table:
                return TableValue(index, option);
            default:
                return null;
        }
    }

    // { colors = { ... }, angle = n } is a gradient, { top = .., right = .., bottom = .., left = .. } a gap per side, and
    // a list its items with spaces between.
    private string? TableValue(int index, string option)
    {
        _lua.GetField(index, "colors");
        if (_lua.IsTable(-1))
        {
            var colors = Items(_lua.AbsIndex(-1), option);
            _lua.Pop(1);
            _lua.GetField(index, "angle");
            var angle = _lua.IsNumber(-1) ? " " + _lua.ToNumber(-1).ToString("R", CultureInfo.InvariantCulture) + "deg" : "";
            _lua.Pop(1);
            return colors is null ? null : colors + angle;
        }

        _lua.Pop(1);
        if (_lua.RawLen(index) > 0)
            return Items(index, option);

        var sides = new string[4];
        var names = (string[])["top", "right", "bottom", "left"];
        for (var i = 0; i < names.Length; i++)
        {
            _lua.GetField(index, names[i]);
            sides[i] = _lua.IsNumber(-1) ? _lua.ToInteger(-1).ToString(CultureInfo.InvariantCulture) : "0";
            _lua.Pop(1);
        }

        return string.Join(' ', sides);
    }

    private string? Items(int index, string option)
    {
        var count = _lua.RawLen(index);
        var items = new List<string>((int)count);
        for (var i = 1; i <= count; i++)
        {
            _lua.RawGetInteger(index, i);
            var item = Value(-1, option);
            _lua.Pop(1);
            if (item is null)
                return null;

            items.Add(item);
        }

        return string.Join(' ', items);
    }

    // hl.env(name, value).
    private int Env(IntPtr state)
    {
        if (!_lua.IsStringOrNumber(1) || !_lua.IsStringOrNumber(2))
        {
            Warn("hl.env: name and value must be strings");
            return 0;
        }

        var name = _lua.ToString(1, false);
        if (name.Length == 0)
        {
            Warn("hl.env: name must not be empty");
            return 0;
        }

        Emit($"env = {LineText.Escape(name)},{LineText.Escape(_lua.ToString(2, false))}");
        return 0;
    }

    // hl.exec_cmd(command, rules): runs on every load, once at start in a winland.start handler, and at exit in a
    // winland.shutdown one. The rules are window rule effects for the window the command opens.
    private int ExecCmd(IntPtr state)
    {
        var command = _lua.IsStringOrNumber(1) ? _lua.ToString(1, false) : "";
        if (command.Length == 0)
        {
            Warn("hl.exec_cmd: expected command as first argument");
            return 0;
        }

        var keyword = _phase switch
        {
            Phase.Start => "exec-once",
            Phase.Shutdown => "exec-shutdown",
            _ => "exec",
        };
        Emit($"{keyword} = {ExecRules(2)}{command}");
        return 0;
    }

    // The window rule block "[effect; effect value] " an exec rules table at index makes, or "" for none.
    private string ExecRules(int index)
    {
        var rules = "";
        if (_lua.IsTable(index))
        {
            index = _lua.AbsIndex(index);
            var effects = new List<string>();
            _lua.PushNil();
            while (_lua.Next(index))
            {
                if (_lua.Type(-2) == LuaType.String)
                {
                    var effect = _lua.ToString(-2, false);
                    effect = effect == "floating" ? "float" : effect;
                    if (_lua.Type(-1) == LuaType.Boolean)
                    {
                        if (_lua.ToBoolean(-1))
                            effects.Add(effect);
                    }
                    else if (Value(-1, effect) is string value)
                    {
                        effects.Add(effect + " " + LineText.Escape(value));
                    }
                }

                _lua.Pop(1);
            }

            if (effects.Count > 0)
                rules = "[" + string.Join("; ", effects) + "] ";
        }

        return rules;
    }

    // hl.on(event, handler): adds the handler to the event's and returns its subscription. The start and shutdown
    // handlers also run now, so what they call is set up for the start or the exit; the others run when Winland
    // notices their event.
    private int On(IntPtr state)
    {
        var name = _lua.IsString(1) ? _lua.ToString(1, false) : "";
        if (!_lua.IsFunction(2))
        {
            Warn("hl.on: bad argument 2: expected a function");
            return 0;
        }

        if (!Events.Contains(name, StringComparer.Ordinal))
        {
            Warn($"hl.on: unknown event \"{name}\". Known events: {string.Join(", ", Events)}");
            return 0;
        }

        _lua.PushCopy(2);
        var handle = Subscribe(name, _lua.Ref(LuaRegistry.Index));
        if (name is StartEvent or ShutdownEvent && _phase != Phase.Running)
        {
            var before = _phase;
            _phase = name == StartEvent ? Phase.Start : Phase.Shutdown;
            _lua.PushCopy(2);
            if (_lua.PCall(0, 0, 0) != LuaStatus.OK)
            {
                NoteError(_lua.ToString(-1));
                _lua.Pop(1);
            }

            _phase = before;
        }

        _lua.RawGetInteger(LuaRegistry.Index, _makeSubscription);
        _lua.PushInteger(handle);
        if (_lua.PCall(1, 1, 0) != LuaStatus.OK)
        {
            _lua.Pop(1);
            return 0;
        }

        return 1;
    }

    // hl.is_key_down(key): whether the user holds a key, named as a keysym, such as "a" or "Super_L", or by its XKB
    // keycode. While the config is first read there is no keyboard to ask, and every key is up.
    private int IsKeyDown(IntPtr state)
    {
        bool? down;
        if (_lua.IsInteger(1))
        {
            var code = _lua.ToInteger(1);
            down = code is < 8 or > int.MaxValue ? null : _world is null ? false : _world.IsKeyDown("", (int)code);
            if (down is null)
            {
                Warn($"hl.is_key_down: invalid keycode {code.ToString(CultureInfo.InvariantCulture)}");
                return 0;
            }
        }
        else if (_lua.IsString(1))
        {
            var key = _lua.ToString(1, false);
            down = _world is null ? false : _world.IsKeyDown(key, 0);
            if (down is null)
            {
                Warn(key == "Enter" ? "Unknown keysym: \"Enter\", did you mean \"Return\"?" : $"Unknown keysym: \"{key}\"");
                return 0;
            }
        }
        else
        {
            Warn("hl.is_key_down: bad argument 1: expected integer or string");
            return 0;
        }

        _lua.PushBoolean(down.Value);
        return 1;
    }

    // require("a.b") reads a/b.lua, or a/b/init.lua, from the config's folder. A path, one that starts with ./, ../,
    // ~/, / or a drive, reads that file, or the file with .lua added, or its init.lua.
    private int RequireModule(IntPtr state)
    {
        var name = _lua.IsString(1) ? _lua.ToString(1, false) : "";
        IEnumerable<string> candidates;
        if (IsPath(name))
        {
            var full = Resolve(name);
            candidates = full.EndsWith(".lua", StringComparison.Ordinal)
                ? [full, Path.Combine(full, "init.lua")]
                : [full, full + ".lua", Path.Combine(full, "init.lua")];
        }
        else
        {
            var relative = name.Replace('.', Path.DirectorySeparatorChar);
            candidates = ((string[])[relative + ".lua", Path.Combine(relative, "init.lua")])
                .Select(candidate => Path.GetFullPath(Path.Combine(_directory, candidate)))
                .Where(full => full.StartsWith(_directory, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var full in candidates)
        {
            if (_source.Read(full) is not string text)
                continue;

            _sources.Add(full);
            _lua.PushString(text);
            _lua.PushString(Path.GetRelativePath(_directory, full));
            return 2;
        }

        _lua.PushNil();
        return 1;
    }

    // The files a path with a wildcard matches, in order, or nil for a name that is not such a path. The listing goes
    // through the same reader as the files, as a source glob's does.
    private int MatchModules(IntPtr state)
    {
        var name = _lua.IsString(1) ? _lua.ToString(1, false) : "";
        if (!IsPath(name) || !SourcePath.HasGlob(name))
        {
            _lua.PushNil();
            return 1;
        }

        var files = _source.Match(Resolve(name)).Order(StringComparer.Ordinal).ToArray();
        _lua.CreateTable(files.Length, 0);
        for (var i = 0; i < files.Length; i++)
        {
            _lua.PushString(files[i]);
            _lua.RawSetInteger(-2, i + 1);
        }

        return 1;
    }

    // A file a wildcard matched failed as it ran: the config warns where it failed and runs on.
    private int RequireFailed(IntPtr state)
    {
        var problem = _lua.ToString(1, false);
        if (_phase == Phase.Running)
            WarnRunning(problem, new LuaOrigin(0, null));
        else
            NoteError(problem);

        return 0;
    }

    // A name require reads as a path rather than a module name: Winland's /, ./, ../ and ~/, and the same
    // with a backslash, and a drive.
    internal static bool IsPath(string name) =>
        name.StartsWith('/') || name.StartsWith('\\')
        || name.StartsWith("./", StringComparison.Ordinal) || name.StartsWith(".\\", StringComparison.Ordinal)
        || name.StartsWith("../", StringComparison.Ordinal) || name.StartsWith("..\\", StringComparison.Ordinal)
        || name.StartsWith("~/", StringComparison.Ordinal) || name.StartsWith("~\\", StringComparison.Ordinal)
        || (name.Length >= 3 && char.IsAsciiLetter(name[0]) && name[1] == ':' && name[2] is '/' or '\\');

    // A path require reads, made full: ~ is the home folder, and a relative path is from the config's folder.
    private string Resolve(string name) =>
        Path.GetFullPath(Path.Combine(_directory, SourcePath.ExpandTilde(name)));

    private void Emit(string line)
    {
        if (_phase == Phase.Running)
        {
            Send(line);
            ScheduleRefresh();
        }
        else
        {
            _lines.Add((line, Caller()));
        }
    }

    // A warning at the Lua code level calls up the stack: 1 is the code that called the callback.
    private void Warn(string message, int level = 1)
    {
        var origin = Caller(level);
        if (_phase == Phase.Running)
            WarnRunning(message, origin);
        else
            _warnings.Add(new ConfigWarning(origin.Line, message, origin.Source));
    }

    // "file:line: message" from a Lua error, at that line of that file.
    private void NoteError(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message, @"^(.+?):(\d+): (.*)$", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (match.Success && int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var line))
            _warnings.Add(new ConfigWarning(line, match.Groups[3].Value, SourceOf(match.Groups[1].Value)));
        else
            _warnings.Add(new ConfigWarning(0, message));
    }

    // A chunk is named by its path from the config's folder. The config itself has no source in a warning.
    private string? SourceOf(string chunk) =>
        string.Equals(chunk, Path.GetFileName(_path), StringComparison.OrdinalIgnoreCase) ? null : Path.GetFullPath(Path.Combine(_directory, chunk));

    // The line of the Lua code that made the call, and its file when it is not the config itself.
    private LuaOrigin Caller(int level = 1)
    {
        var debug = new LuaDebug();
        if (_lua.GetStack(level, ref debug) == 0 || !_lua.GetInfo("Sl", ref debug))
            return new LuaOrigin(0, null);

        var chunk = debug.Source is { Length: > 1 } named && named[0] == '@' ? named[1..] : null;
        return new LuaOrigin(Math.Max(0, debug.CurrentLine), chunk is null ? null : SourceOf(chunk));
    }
}
