using System.Security.Principal;
using Winland;
using Winland.Install;
using Winland.Plugins;
using Winland.Windows;

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
    return Guard(() => Run(null));

if (args.Length == 2 && IsVerb(args[0], "--config"))
    return Guard(() => Run(args[1]));

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
}

static int Install(string user)
{
    var executable = Environment.ProcessPath
        ?? throw new InvalidOperationException("The executable path is not available.");
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

static int Run(string? path)
{
    var configPath = path ?? DefaultConfigPath();
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
    core.ReadConfig = () => ReadForReload(configPath, plugins);
    ConfigWatcher? watcher = null;
    core.AfterReload = document =>
    {
        current = document;
        watcher?.Watch(WatchedPaths(configPath, document));
        // exec-once ran at process start. A reload launches only the commands that repeat.
        LaunchConfigured(core, core.ReloadLaunches(document));
    };

    core.Handle(new Configure(current));
    var store = new RecoveryStore(StatePath());
    var prior = store.Read();
    SessionEffects.InstallSystem();
    if (prior?.Input is InputOriginals saved)
        SessionEffects.RestoreSaved(saved);
    using var host = new WindowHost(core, snapshot => Persist(store, snapshot), PipeName());
    using var exit = new ExitWatch(() => Stop(host, store));
    using var watching = new ConfigWatcher(() =>
    {
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
    watcher.Watch(WatchedPaths(configPath, current));
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

static IReadOnlyList<string> WatchedPaths(string configPath, ConfigDocument document)
{
    var full = Path.GetFullPath(configPath);
    var directory = Path.GetDirectoryName(full) ?? ".";
    var paths = new List<string> { full };
    foreach (var source in document.Sources)
    {
        var combined = Path.IsPathRooted(source) ? source : Path.Combine(directory, source);
        paths.Add(Path.GetFullPath(combined));
    }

    return paths;
}

static void Persist(RecoveryStore store, RecoverySnapshot snapshot)
{
    var recorded = snapshot with { Input = SessionEffects.Originals };
    if (WindowIdentity.TryAnnotate(recorded, out var annotated))
        store.Save(annotated);
}

static void Stop(WindowHost host, RecoveryStore store)
{
    host.RevealHidden();
    host.ResetDecorations();
    host.RestoreTaskbar();
    SessionEffects.Restore();
    store.Delete();
}

static string StatePath()
{
    var configured = Environment.GetEnvironmentVariable("WINLAND_STATE");
    if (!string.IsNullOrWhiteSpace(configured))
        return configured;

    return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Winland",
        "state.json");
}

static string? PipeName()
{
    var configured = Environment.GetEnvironmentVariable("WINLAND_PIPE");
    return string.IsNullOrWhiteSpace(configured) ? null : configured;
}

static ConfigDocument ReadConfig(string path)
{
    var full = Path.GetFullPath(path);
    string text;
    try
    {
        text = File.ReadAllText(full);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        throw new InvalidOperationException($"Could not read config '{full}'.", exception);
    }

    var directory = Path.GetDirectoryName(full) ?? ".";
    return ConfigParser.Parse(text, new FileConfigSource(directory), KeyboardLayout.Active());
}

static string DefaultConfigPath() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "winland", "winland.conf");

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
