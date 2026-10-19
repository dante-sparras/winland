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
    private readonly List<Timer> _timers = [];
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
        foreach (var timer in _timers)
            timer.Dispose();

        _timers.Clear();
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
        public void Start(int id, int milliseconds, bool repeat)
        {
            var period = repeat ? milliseconds : Timeout.Infinite;
            var timer = new Timer(_ => runtime.Post(() =>
            {
                if (runtime._generation == generation)
                    runtime._session?.Tick(id);
            }));
            runtime._timers.Add(timer);
            _ = timer.Change(milliseconds, period);
        }
    }
}
