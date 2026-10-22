using Winland.Daemon;
using Winland.Install;

// The debug options copy what Winland writes here to a file and to stdout.
var log = new RollingLogWriter(Console.Error);
Console.SetError(log);

if (InstallCommand.Parse(args) is InstallCommand command)
    return Guard(() => MaintenanceCommand.Run(args, command));

var userInstall = InstallLocations.IsPerUserInstall(
    Environment.ProcessPath,
    InstallLocations.User(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
switch (ManualStartElevation.Decide(
    Elevation.IsElevated(),
    Environment.GetEnvironmentVariable(ManualStartElevation.OptOutVariable),
    args,
    userInstall))
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

static void LaunchConfigured(WindowManager core, IEnumerable<Launch> launches)
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

    if (path is not null && !path.EndsWith(".conf", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"--config takes a .conf file, not '{path}'.");

    var configPath = path ?? ConfigPaths.Default(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    // The default is written only when Winland was not given --config and the file is not there yet.
    if (path is null && !File.Exists(configPath) && DefaultConfig.WriteIfMissing(configPath))
        Console.Error.WriteLine($"Wrote the default config to {configPath}.");

    if (!File.Exists(configPath))
        throw new InvalidOperationException($"Could not read config '{Path.GetFullPath(configPath)}'.");

    var current = ConfigFile.Read(configPath);

    foreach (var warning in current.Warnings)
    {
        var where = warning.Source ?? configPath;
        Console.Error.WriteLine($"{where}:{warning.Line}: {warning.Message}");
    }

    var core = new WindowManager();
    core.ReadConfig = () => ConfigFile.TryRead(configPath);
    ConfigWatcher? watcher = null;
    string? logFile = null;
    core.AfterReload = document =>
    {
        current = document;
        CopyLog(log, core.Debug, logFile);
        watcher?.Watch(ConfigPaths.Watched(configPath, document.Sources));
        // exec-once ran at process start. A reload launches only the commands that repeat.
        LaunchConfigured(core, core.ReloadLaunches(document));
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
    TaskbarControl.InstallSystem();
    using var host = new WindowHost(core, snapshot => Persist(store, snapshot), PipeName());
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

        host.OnOpenFile = TrayFiles.Open;
        host.OnTray = command =>
        {
            switch (command)
            {
                case TrayCommand.Reload:
                    host.Reload();
                    break;
                case TrayCommand.EditConfig:
                    TrayFiles.Open(configPath);
                    break;
                case TrayCommand.OpenLog:
                    TrayFiles.Open(logFile);
                    break;
                case TrayCommand.Quit:
                    host.Command("dispatch exit");
                    break;
            }
        };

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
        LaunchConfigured(core, WindowManager.ShutdownLaunches(current));

        Console.WriteLine("Winland stopped.");
    }
}

static void Persist(RecoveryStore store, RecoverySnapshot snapshot)
{
    // A Client that closed since the snapshot was taken is left out. The rest are still recorded.
    store.Save(WindowIdentity.Annotate(snapshot));
}

static void Stop(WindowHost host, RecoveryStore store)
{
    host.RevealHidden();
    WindowHost.ResetDecorations();
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
        InstallLocations.State(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
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

static int Usage()
{
    Console.Error.WriteLine("usage: winland");
    Console.Error.WriteLine("       winland --config <path>");
    Console.Error.WriteLine("       winland install [--user]");
    Console.Error.WriteLine("       winland update [--channel <stable|dev>]");
    Console.Error.WriteLine("       winland uninstall [--purge]");
    Console.Error.WriteLine("       winland tweaks --off [--touchpad]");
    Console.Error.WriteLine("WINLAND_NO_ELEVATE=1 or --no-elevate starts without elevation. It exists for tests.");
    Console.Error.WriteLine("Elevated features then do not work. A per-user install never asks for elevation.");
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
