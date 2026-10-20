using System.Security.Principal;
using Winland;
using Winland.Install;
using Winland.Lua;
using Winland.Plugins;
using Winland.Windows;

// rollinglog answers with the tail of what Winland writes here, and the debug options copy it to a file and stdout.
var log = new RollingLogWriter(Console.Error);
Console.SetError(log);

if (IsInstall(args))
    return RunInstall(args);

if (args.Length == 1 && IsVerb(args[0], "uninstall"))
    return RunElevated(args, Uninstall);

switch (ManualStartElevation.Decide(
    Elevation.IsElevated(),
    Environment.GetEnvironmentVariable(ManualStartElevation.OptOutVariable),
    args))
{
    case ElevationChoice.Relaunch relaunch:
        return Elevation.Relaunch(relaunch.Arguments);
    case ElevationChoice.Refuse refuse:
        Console.Error.WriteLine(refuse.Message);
        return 1;
    case ElevationChoice.Continue continued:
        args = [.. continued.Arguments];
        break;
    default:
        throw new InvalidOperationException("Unexpected start decision.");
}

if (args.Length == 0)
    return Guard(() => Run(null, log));

if (args.Length == 2 && IsVerb(args[0], "--config"))
    return Guard(() => Run(args[1], log));

return Usage();

static bool IsVerb(string value, string verb) =>
    value.Equals(verb, StringComparison.OrdinalIgnoreCase);

static bool IsInstall(string[] args) =>
    args.Length is 1 or 2 && IsVerb(args[0], "install");

static int RunInstall(string[] args)
{
    // Relaunch carries the invoking user. An over-the-shoulder UAC approval
    // runs as the admin who approved it, which is not the account that logs on.
    if (!Elevation.IsElevated())
    {
        var relay = args.Length == 1 ? [args[0], CurrentUser()] : args;
        return Elevation.Relaunch(relay);
    }

    return Guard(() => Install(args.Length == 2 ? args[1] : CurrentUser()));
}

static int RunElevated(string[] args, Func<int> action)
{
    if (!Elevation.IsElevated())
        return Elevation.Relaunch(args);

    return Guard(action);
}

static int Guard(Func<int> action)
{
    try
    {
        return action();
    }
    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
    catch (Exception ex) when (ex is not OutOfMemoryException)
    {
        // An exception nothing catches ends the process without running finally blocks. Catching it here
        // lets Run's finally show the hidden Clients and put the frames back first.
        Console.Error.WriteLine(ex);
        return 1;
    }
}

static int Install(string user)
{
    var executable = Environment.ProcessPath
        ?? throw new InvalidOperationException("The executable path is not available.");

    // The task starts this file elevated at every logon. A folder a non-administrator can
    // write, or swap through a folder above it, would let any program the user runs replace
    // what the task starts.
    var folder = Path.GetDirectoryName(executable)
        ?? throw new InvalidOperationException("The executable folder is not available.");
    if (InstallLocation.Refusal(WindowsAccessReader.Tree(folder), WindowsAccessReader.Ancestors(folder)) is string refusal)
    {
        throw new InvalidOperationException(
            $"Refusing to install: {refusal}, and the logon task would start "
            + "Winland from there with administrator rights. Copy Winland to a folder only administrators can write, "
            + "such as C:\\Program Files\\Winland, and run 'winland install' from there.");
    }

    new LogonInstall(new SchtasksCatalog()).Install(executable, user);
    Console.WriteLine("Winland will start at logon.");
    return 0;
}

static string CurrentUser()
{
    using var identity = WindowsIdentity.GetCurrent();
    return identity.Name;
}

static int Uninstall()
{
    new LogonInstall(new SchtasksCatalog()).Uninstall();
    Console.WriteLine("Winland will no longer start at logon.");
    return 0;
}

static void LaunchConfigured(Core core, IEnumerable<Launch> launches)
{
    foreach (var launch in launches)
    {
        var pid = ProcessLauncher.TryLaunch(launch);
        if (pid is int id)
            core.NoteLaunched(id, launch);
    }
}

static int Run(string? path, RollingLogWriter log)
{
    using var instance = InstanceLock.TryTake(InstanceLock.NameFor(PipeName() ?? CommandProtocol.PipeName));
    if (instance is null)
    {
        Console.Error.WriteLine("Winland is already running in this session.");
        return 1;
    }

    // A Lua config keeps running: its binds' functions, timers and evals run on the runtime's thread (ADR 0013).
    var luaWorld = new LuaWorld();
    using var lua = new LuaRuntime(luaWorld);
    if (!Dispatchers.TryBind(LuaConfig.FunctionDispatcher, arguments => { lua.Call(arguments); return new DispatcherResult(null, null); }, out var luaError))
        Console.Error.WriteLine(luaError);

    CommandServer.Intercept = line => lua.Answer(line, luaWorld.Command);

    var configPath = path ?? ConfigPaths.Default(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
    ConfigDocument current;
    if (!File.Exists(configPath))
    {
        if (path is not null)
            throw new InvalidOperationException($"Could not read config '{Path.GetFullPath(configPath)}'.");

        Console.Error.WriteLine($"No config at {configPath}.");
        current = ConfigParser.Parse("");
    }
    else
    {
        current = ReadConfig(configPath);
    }

    var plugins = PluginHost.CreateDefault();
    current = plugins.Incorporate(current, () => ReadConfig(configPath), ConfigDirectory(configPath));
    foreach (var warning in current.Warnings)
    {
        var where = warning.Source ?? configPath;
        Console.Error.WriteLine($"{where}:{warning.Line}: {warning.Message}");
    }

    var core = new Core();
    core.Plugins = plugins;
    core.Noticed += outcome => plugins.Deliver(outcome.Events);
    lua.Listen(core);
    core.LayoutScripts = lua;
    core.ReadConfig = () => ReadForReload(configPath, plugins);
    ConfigWatcher? watcher = null;
    string? logFile = null;
    core.AfterReload = document =>
    {
        current = document;
        CopyLog(log, core.Debug, logFile);
        watcher?.Watch(ConfigPaths.Watched(configPath, document.Sources));
        // exec-once ran at process start. A reload launches only the commands that repeat.
        LaunchConfigured(core, core.ReloadLaunches(document));
        lua.Adopt(LuaHosting.Take());
    };

    core.Handle(new Configure(current));
    var store = StateStore();
    logFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(store.File)) ?? ".", "winland.log");
    CopyLog(log, core.Debug, logFile);
    var prior = store.Read();

    // A recovery file is left by a run that did not exit cleanly, which may have left a drag's pointer shape behind.
    if (prior is not null)
        SessionEffects.RestoreCursors();
    SessionEffects.InstallSystem();
    if (prior?.Input is InputOriginals saved)
        SessionEffects.RestoreSaved(saved);
    if (prior?.Mirrors is { Count: > 0 } mirrored)
        SessionEffects.RestoreSaved(mirrored);
    using var host = new WindowHost(core, snapshot => Persist(store, snapshot), PipeName());
    luaWorld.Host = host;
    lua.Adopt(LuaHosting.Take());
    // Log off and shut down clean up from WM_ENDSESSION, on the loop thread. Closing the console calls in from
    // a thread of its own, so the cleanup is handed to the loop thread there too.
    host.SessionEnding = () => Stop(host, store);
    using var exit = new ExitWatch(() => host.OnLoop(() => Stop(host, store)));
    UnhandledExceptionEventHandler onCrash = (_, _) => Salvage(host);
    AppDomain.CurrentDomain.UnhandledException += onCrash;
    using var watching = new ConfigWatcher(() =>
    {
        if (!core.ReloadsOnSave)
            return;

        try
        {
            host.Reload();
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException or IOException)
        {
            Console.Error.WriteLine(exception.Message);
        }
    });
    watcher = watching;
    watcher.Watch(ConfigPaths.Watched(configPath, current.Sources));
    try
    {
        LaunchConfigured(core, core.StartupLaunches(current));

        host.Run(prior, () =>
        {
            store.Arm();
            Persist(store, core.Recovery());
            Console.WriteLine("Winland is running.");
        });
        return 0;
    }
    finally
    {
        Stop(host, store);
        AppDomain.CurrentDomain.UnhandledException -= onCrash;
        LaunchConfigured(core, core.ShutdownLaunches(current));
        plugins.Dispose();

        Console.WriteLine("Winland stopped.");
    }
}

static ConfigRead ReadForReload(string path, PluginHost plugins)
{
    var full = Path.GetFullPath(path);
    if (!File.Exists(full))
    {
        var missing = $"Could not read config '{full}'.";
        return new ConfigRead(null, new ConfigWarning(0, missing, full));
    }

    try
    {
        var document = plugins.Incorporate(ReadConfig(full), () => ReadConfig(full), ConfigDirectory(full));
        return new ConfigRead(document);
    }
    catch (InvalidOperationException exception)
    {
        return new ConfigRead(null, new ConfigWarning(0, exception.Message, full));
    }
}

static string? ConfigDirectory(string path)
{
    var full = Path.GetFullPath(path);
    return Path.GetDirectoryName(full);
}

static void Persist(RecoveryStore store, RecoverySnapshot snapshot)
{
    // A Client that closed since the snapshot was taken is left out. The rest are still recorded.
    store.Save(WindowIdentity.Annotate(snapshot with { Input = SessionEffects.Originals, Mirrors = SessionEffects.MirrorOriginals }));
}

static void Stop(WindowHost host, RecoveryStore store)
{
    host.RevealHidden();
    host.ResetDecorations();
    host.RestoreTaskbar();
    SessionEffects.Restore();
    store.Delete();
}

// An exception on another thread ends the process without Stop. This puts back what it can, and keeps the
// state file so the next start rejoins the Clients to their Workspaces.
static void Salvage(WindowHost host)
{
    try
    {
        host.Salvage();
    }
    catch (Exception exception) when (exception is not OutOfMemoryException)
    {
        Console.Error.WriteLine(exception.Message);
    }

    try
    {
        SessionEffects.Restore();
    }
    catch (Exception exception) when (exception is not OutOfMemoryException)
    {
        Console.Error.WriteLine(exception.Message);
    }
}

// The elevated daemon writes and deletes the state file as an administrator, in a folder the user's
// environment names. It uses only a folder the user could already empty (see StateLocation).
static RecoveryStore StateStore()
{
    var standard = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Winland",
        "state.json");
    var variable = Environment.GetEnvironmentVariable(StateLocation.Variable);
    if (!Elevation.IsElevated())
        return new RecoveryStore(StateLocation.Choose(variable, standard, elevated: false, _ => null).Path);

    string user;
    using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
        user = identity.User?.Value ?? throw new InvalidOperationException("The current user has no SID.");

    string? RefuseOpen(Microsoft.Win32.SafeHandles.SafeFileHandle folder) =>
        StateLocation.Refusal(WindowsFileHandles.FinalPath(folder), WindowsAccessReader.TryRead(folder, out var access) ? access : null, user);

    string? RefusePath(string folder)
    {
        try
        {
            using var handle = WindowsFileHandles.Open(folder, WindowsFileHandles.ReadAttributes | WindowsFileHandles.ReadControl, FileShare.ReadWrite | FileShare.Delete, followReparse: true);
            return RefuseOpen(handle);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }

    var (path, ignored) = StateLocation.Choose(variable, standard, elevated: true, RefusePath);
    if (ignored is not null)
        Console.Error.WriteLine(ignored);

    return new RecoveryStore(path, RefuseOpen);
}

static string? PipeName()
{
    var configured = Environment.GetEnvironmentVariable("WINLAND_PIPE");
    return string.IsNullOrWhiteSpace(configured) ? null : configured;
}

static ConfigDocument ReadConfig(string path)
{
    var full = Path.GetFullPath(path);
    var directory = Path.GetDirectoryName(full) ?? ".";
    var source = new FileConfigSource(directory);
    var text = source.Read(full)
        ?? throw new InvalidOperationException($"Could not read config '{full}'.");

    // A Lua config runs first and gives the lines its hl calls make (ADR 0013).
    var isLua = full.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);
    if (!isLua)
        LuaHosting.Clear();

    var lua = isLua ? LuaHosting.Load(text, full, source) : null;

    // [elevated] runs with Winland's administrator token, so only a config that only
    // administrators can write, sources included, may use it (ADR 0007).
    var document = ConfigParser.Parse(lua?.Text ?? text, source, KeyboardLayout.Active(), full);
    if (lua is not null)
        document = lua.Remap(document);

    return ConfigElevation.Decide(document, source.Trust.Refusal);
}

static int Usage()
{
    Console.Error.WriteLine("usage: winland");
    Console.Error.WriteLine("       winland --config <path>");
    Console.Error.WriteLine("       winland install");
    Console.Error.WriteLine("       winland uninstall");
    Console.Error.WriteLine("WINLAND_NO_ELEVATE=1 or --no-elevate starts without elevation. It exists for tests.");
    Console.Error.WriteLine("Elevated features then do not work.");
    return 2;
}

// debug:disable_logs false copies the log to winland.log beside the state file, which is written again at each start,
// and debug:enable_stdout_logs to standard output. An elevated Winland writes no file: a folder the user can change could
// send a write by name somewhere only administrators may write.
static void CopyLog(RollingLogWriter log, DebugOptions options, string? file)
{
    log.Copy(options, OpenLog, Console.Out);

    TextWriter? OpenLog()
    {
        if (file is null)
            return null;

        if (Elevation.IsElevated())
        {
            Console.Error.WriteLine("debug:disable_logs is false, but an elevated Winland writes no log file.");
            return null;
        }

        try
        {
            return new StreamWriter(new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not open the log file '{file}': {exception.Message}");
            return null;
        }
    }
}
