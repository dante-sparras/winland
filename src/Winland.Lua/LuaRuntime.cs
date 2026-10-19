using System.Collections.Concurrent;

namespace Winland.Lua;

/// <summary>A loaded Lua config: the lines it made, and its Lua, which a <see cref="LuaRuntime"/> keeps running.</summary>
public sealed record LuaLoad(LuaTranslation Translation, LuaSession Session);

/// <summary>The Lua of a loaded config. The runtime that adopts it disposes it; otherwise dispose it.</summary>
public sealed class LuaSession : IDisposable
{
    internal LuaSession(LuaRun run) => Run = run;

    internal LuaRun Run { get; }

    public void Dispose() => Run.Dispose();
}

/// <summary>
/// Keeps the config's Lua running on a thread of its own, so it never runs inside one of the core's calls: a bind's
/// function, a timer and an eval are queued here and run in turn, and their dispatches go through
/// <see cref="ILuaWorld.Command"/> as a <c>winlandctl</c> command would. A reload hands it the new config's Lua.
/// </summary>
public sealed class LuaRuntime : IDisposable
{
    /// <summary>How long an eval waits for the Lua thread before it gives up.</summary>
    public static readonly TimeSpan EvalWait = TimeSpan.FromSeconds(2);

    private readonly ILuaWorld _world;
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;
    private readonly Dictionary<int, Timer> _timers = [];
    private LuaRun? _session;
    private int _generation;
    private bool _running;

    public LuaRuntime(ILuaWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _thread = new Thread(Run) { IsBackground = true, Name = "Winland Lua" };
        _thread.Start();
    }

    /// <summary>True while a Lua config runs, from the moment it is adopted.</summary>
    public bool Running => Volatile.Read(ref _running);

    /// <summary>Runs <paramref name="session"/> in place of the Lua running now, or stops it with null.</summary>
    public void Adopt(LuaSession? session)
    {
        Volatile.Write(ref _running, session is not null);
        Post(() => Replace(session));
    }

    private void Replace(LuaSession? session)
    {
        _generation++;
        foreach (var timer in _timers.Values)
            timer.Dispose();

        _timers.Clear();
        _session?.Unload();
        _session?.Dispose();
        _session = session?.Run;
        _session?.Start(_world, new Timers(this, _generation));
    }

    /// <summary>The keys of a bind that calls a Lua function were pressed. <paramref name="arguments"/> is the function's id.</summary>
    public void Call(string arguments)
    {
        if (int.TryParse(arguments.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id))
            Post(() => _session?.Call(id));
    }

    /// <summary>
    /// Hands the config's <c>hl.on</c> handlers the events of an outcome. It only queues them, since the core calls it
    /// inside its lock: they run on the Lua thread after what is queued before. An event a handler's own dispatch
    /// raised runs after that handler and is not handed to it again.
    /// </summary>
    public void Deliver(IReadOnlyList<StreamEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0 || !Running)
            return;

        // A dispatch from Lua runs the core on the Lua thread, so an event noticed there came from the running Lua.
        var cause = Thread.CurrentThread == _thread ? _session?.Cause : null;
        Post(() => _session?.Deliver(events, cause));
    }

    /// <summary>Hands the Lua's <c>hl.on</c> handlers the events of each outcome <paramref name="core"/> notices.</summary>
    public void Listen(Core core)
    {
        ArgumentNullException.ThrowIfNull(core);
        core.Noticed += outcome => Deliver(outcome.Events);
    }

    /// <summary>
    /// The pipe commands the Lua answers: <c>eval</c> and <c>repl</c> run Lua, and with a Lua config <c>dispatch</c>
    /// with a call in it is <c>eval 'hl.dispatch(...)'</c>, as in the pinned release. A batch with one of them in it
    /// runs its other commands through <paramref name="execute"/>, as the pipe would. Anything else is the core's,
    /// and gives null.
    /// </summary>
    public string? Answer(string line, Func<string, string> execute)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(execute);
        var text = line.Trim();
        if (text.StartsWith(BatchPrefix, StringComparison.Ordinal))
        {
            var commands = ExtraCommands.SplitBatch(text[BatchPrefix.Length..]);
            if (!commands.Any(command => Single(command) is not null))
                return null;

            return string.Join("\n\n\n", commands.Select(command => Answer(command, execute) ?? execute(command)));
        }

        return Single(text) is { } single ? Answer(single.Verb, single.Argument) : null;
    }

    private const string BatchPrefix = "[[BATCH]]";

    private string Answer(string verb, string rest) => verb switch
    {
        "eval" or "repl" => Running ? Eval(rest, verb == "repl") : "eval is only supported with a Lua config",
        _ => Eval($"return hl.dispatch({rest})", repl: false),
    };

    // The verb and the rest of a command the Lua answers, or null for one it leaves to the core.
    private (string Verb, string Argument)? Single(string line)
    {
        var text = line.Trim();

        // The flags winlandctl puts before a command, such as "j/", are not the Lua's.
        var slash = text.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0 && slash <= 3 && text[..slash].All(char.IsAsciiLetterLower))
            text = text[(slash + 1)..];

        var space = text.IndexOf(' ', StringComparison.Ordinal);
        var verb = space < 0 ? text : text[..space];
        var rest = space < 0 ? "" : text[(space + 1)..].Trim();
        return verb switch
        {
            "eval" or "repl" => (verb, rest),
            "dispatch" when Running && rest.Contains('(', StringComparison.Ordinal) => (verb, rest),
            _ => null,
        };
    }

    /// <summary><c>eval</c> or <c>repl</c>: waits for the Lua thread and gives the reply.</summary>
    public string Eval(string code, bool repl)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (Thread.CurrentThread == _thread)
            return "error: eval from Lua is not allowed";

        var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => reply.TrySetResult(_session?.Eval(code, repl) ?? "error: lua state not initialized"));
        return reply.Task.Wait(EvalWait) ? reply.Task.Result : "error: the Lua thread is busy";
    }

    public void Dispose()
    {
        Adopt(null);
        _work.CompleteAdding();
        _ = _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Post(Action work)
    {
        try
        {
            _work.Add(work);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Run()
    {
        foreach (var work in _work.GetConsumingEnumerable())
        {
            try
            {
                work();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _world.Log("lua: " + exception.Message);
            }
        }
    }

    // A config's timers. One that fires after a reload belongs to the Lua that was replaced and does nothing.
    private sealed class Timers(LuaRuntime runtime, int generation) : ILuaTimers
    {
        public void Start(int id, int due, int period)
        {
            if (!runtime._timers.TryGetValue(id, out var timer))
            {
                timer = new Timer(_ => runtime.Post(() =>
                {
                    if (runtime._generation == generation)
                        runtime._session?.Tick(id);
                }));
                runtime._timers[id] = timer;
            }

            _ = timer.Change(due, period);
        }

        public void Stop(int id)
        {
            if (runtime._timers.TryGetValue(id, out var timer))
                _ = timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }
}
