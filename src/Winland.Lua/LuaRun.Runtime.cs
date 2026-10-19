using KeraLua;

namespace Winland.Lua;

/// <summary>What the Lua of a running config reaches: the commands <c>winlandctl</c> sends, and the log.</summary>
public interface ILuaWorld
{
    /// <summary>Runs a command as the pipe does, its effect applied, and gives the reply.</summary>
    string Command(string command);

    void Log(string message);
}

// The config's Lua keeps running after it loads: binds that call a function, timers, hl.dispatch and winlandctl eval
// run on the runtime's thread, between the core's own calls. Each call has the pinned release's time limit.
internal sealed partial class LuaRun
{
    // The time limits of the pinned release, in seconds.
    internal const double BindLimit = 0.1;
    internal const double TimerLimit = 0.05;
    internal const double EvalLimit = 0.25;
    internal const double DispatchLimit = 0.1;

    private readonly Dictionary<int, int> _functions = [];
    private readonly List<string> _queued = [];
    private readonly List<(int Id, int Milliseconds, bool Repeat)> _pendingTimers = [];
    private readonly List<string> _prints = [];
    private readonly List<string> _issues = [];
    private ILuaWorld? _world;
    private ILuaTimers? _timers;
    private bool _evaluating;
    private int _arm;
    private int _nextFunction;

    // The Lua function a bind calls, kept in the registry under an id the bind line names.
    private int KeepFunction(int index)
    {
        _lua.PushCopy(index);
        var reference = _lua.Ref(LuaRegistry.Index);
        var id = ++_nextFunction;
        _functions[id] = reference;
        return id;
    }

    /// <summary>The config has loaded and Winland runs: what it queued runs now, and later calls reach <paramref name="world"/>.</summary>
    public void Start(ILuaWorld world, ILuaTimers timers)
    {
        _world = world;
        _timers = timers;
        _phase = Phase.Running;
        foreach (var command in _queued)
            Report(world.Command(command), command);

        _queued.Clear();
        foreach (var (id, milliseconds, repeat) in _pendingTimers)
            timers.Start(id, milliseconds, repeat);

        _pendingTimers.Clear();
    }

    /// <summary>Calls the function a bind names.</summary>
    public void Call(int id) => Call(id, BindLimit, "keybind callback");

    /// <summary>Calls a timer's function.</summary>
    public void Tick(int id) => Call(id, TimerLimit, "hl.timer callback");

    private void Call(int id, double limit, string label)
    {
        if (!_functions.TryGetValue(id, out var reference))
            return;

        _lua.RawGetInteger(LuaRegistry.Index, reference);
        Guarded(limit, label);
    }

    // Runs the function on top of the stack with no arguments. An error is logged, or reported by an eval.
    private bool Guarded(double limit, string label, int results = 0)
    {
        Arm(limit, label);
        var status = _lua.PCall(0, results, 0);
        Arm(0, "");
        if (status == LuaStatus.OK)
            return true;

        var error = _lua.ToString(-1);
        _lua.Pop(1);
        if (_evaluating)
            _issues.Add("error: " + error);
        else
            _world?.Log($"lua: {label}: {error}");

        return false;
    }

    // Starts the time limit of a call, or ends it with 0.
    private void Arm(double seconds, string label)
    {
        if (_arm == 0)
            return;

        _lua.RawGetInteger(LuaRegistry.Index, _arm);
        if (seconds > 0)
            _lua.PushNumber(seconds);
        else
            _lua.PushNil();

        _lua.PushString(label);
        _ = _lua.PCall(2, 0, 0);
    }

    /// <summary>
    /// <c>eval</c> and <c>repl</c>: runs <paramref name="code"/> as an expression when it is one, as a chunk otherwise.
    /// The reply is "ok", the errors, or for a repl what the code printed and returned.
    /// </summary>
    public string Eval(string code, bool repl)
    {
        _prints.Clear();
        _issues.Clear();
        _evaluating = true;
        try
        {
            var expression = code.StartsWith("return", StringComparison.Ordinal) ? code : $"return {code};";
            if (_lua.LoadString(expression, "=eval") != LuaStatus.OK)
            {
                _lua.Pop(1);
                if (_lua.LoadString(code, "=eval") != LuaStatus.OK)
                {
                    var error = _lua.ToString(-1);
                    _lua.Pop(1);
                    return "error: " + error;
                }
            }

            var top = _lua.GetTop() - 1;
            if (Guarded(EvalLimit, "eval", LuaMultipleResults))
            {
                var count = _lua.GetTop() - top;
                if (count > 0)
                {
                    var values = new List<string>(count);
                    for (var i = 1; i <= count; i++)
                        values.Add(Show(top + i));

                    _prints.Add(string.Join('\t', values));
                }

                _lua.SetTop(top);
            }

            if (repl && _prints.Count > 0)
                return string.Join('\n', _prints);

            return _issues.Count > 0 ? string.Join('\n', _issues) : "ok";
        }
        finally
        {
            _evaluating = false;
        }
    }

    private const int LuaMultipleResults = -1;

    // tostring() of a value, as print shows it.
    private string Show(int index)
    {
        _lua.GetGlobal("tostring");
        _lua.PushCopy(index);
        if (_lua.PCall(1, 1, 0) != LuaStatus.OK)
        {
            _lua.Pop(1);
            return "?";
        }

        var text = _lua.ToString(-1, false);
        _lua.Pop(1);
        return text;
    }

    // print: into an eval's reply while it runs, into the log otherwise.
    private int Print(IntPtr state)
    {
        var count = _lua.GetTop();
        var values = new List<string>(count);
        for (var i = 1; i <= count; i++)
            values.Add(Show(i));

        var line = string.Join('\t', values);
        if (_evaluating)
            _prints.Add(line);
        else if (_world is null)
            Console.Error.WriteLine("lua: " + line);
        else
            _world.Log("lua: " + line);

        return 0;
    }

    // hl.dispatch(dispatcher): runs it now, or once Winland runs when the config calls it as it loads.
    private int DispatchNow(IntPtr state)
    {
        if (ReadDispatch(1) is not Dispatch dispatch)
            return Result(false, "hl.dispatch: expected a dispatcher (e.g. hl.dsp.window.close())");

        if (dispatch.Unhonored is string unhonored)
            return Result(false, $"hl.dispatch: {unhonored} is not honored yet");

        var command = ("dispatch " + dispatch.Dispatcher + " " + dispatch.Arguments).TrimEnd();
        if (_world is null)
        {
            _queued.Add(command);
            return Result(true, null);
        }

        var reply = _world.Command(command);
        return reply == "ok" ? Result(true, null) : Result(false, "hl.dispatch: " + reply);
    }

    // { ok = true, pass_event = false }, or { ok = false, error = message } with the message reported as an error.
    private int Result(bool ok, string? error)
    {
        if (error is not null)
        {
            var origin = Caller();
            var where = origin.Line > 0 ? $"line {origin.Line}: " : "";
            if (_evaluating)
                _issues.Add("error: " + where + error);
            else if (_phase == Phase.Running)
                _world?.Log("lua: " + where + error);
            else
                Warn(error);
        }

        _lua.NewTable();
        _lua.PushBoolean(ok);
        _lua.SetField(-2, "ok");
        if (ok)
        {
            _lua.PushBoolean(false);
            _lua.SetField(-2, "pass_event");
        }
        else
        {
            _lua.PushString(error ?? "");
            _lua.SetField(-2, "error");
        }

        return 1;
    }

    // hl.timer(fn, { timeout = ms, type = "repeat" | "oneshot" }).
    private int Timer(IntPtr state)
    {
        if (!_lua.IsFunction(1) || !_lua.IsTable(2))
        {
            Warn("hl.timer: expected a function and a table of options");
            return 0;
        }

        _lua.GetField(2, "timeout");
        var timeout = _lua.IsNumber(-1) ? (int)_lua.ToNumber(-1) : -1;
        var hasTimeout = _lua.IsNumber(-1);
        _lua.Pop(1);
        if (!hasTimeout)
        {
            Warn("hl.timer: opts.timeout must be a number (ms)");
            return 0;
        }

        if (timeout <= 0)
        {
            Warn("hl.timer: opts.timeout must be > 0");
            return 0;
        }

        _lua.GetField(2, "type");
        var type = _lua.IsString(-1) ? _lua.ToString(-1, false) : "";
        _lua.Pop(1);
        if (type is not ("repeat" or "oneshot"))
        {
            Warn("hl.timer: opts.type must be \"repeat\" or \"oneshot\"");
            return 0;
        }

        var id = KeepFunction(1);
        if (_timers is null)
            _pendingTimers.Add((id, timeout, type == "repeat"));
        else
            _timers.Start(id, timeout, type == "repeat");

        return 0;
    }

    // A call that sets something while Winland runs, such as hl.config from an eval, sends its line as a keyword.
    private void Send(string line)
    {
        var equals = line.IndexOf(" = ", StringComparison.Ordinal);
        if (equals < 0 || _world is null)
            return;

        var command = $"keyword {line[..equals]} {line[(equals + 3)..]}";
        Report(_world.Command(command), command);
    }

    private void Report(string reply, string command)
    {
        if (reply == "ok")
            return;

        if (_evaluating)
            _issues.Add("error: " + reply);
        else
            _world?.Log($"lua: {command}: {reply}");
    }

    // A warning while Winland runs goes to an eval's reply or the log, at the Lua line that made it.
    private void WarnRunning(string message, LuaOrigin origin)
    {
        var where = origin.Line > 0 ? $"line {origin.Line}: " : "";
        if (_evaluating)
            _issues.Add("error: " + where + message);
        else
            _world?.Log("lua: " + where + message);
    }
}

/// <summary>Starts the timers a config makes. The runtime calls back on its own thread.</summary>
internal interface ILuaTimers
{
    void Start(int id, int milliseconds, bool repeat);
}
