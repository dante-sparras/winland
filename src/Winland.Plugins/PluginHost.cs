using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Winland.PluginApi;

namespace Winland.Plugins;

public sealed class PluginHost : IPluginRuntime, IDisposable
{
    public const int FailureLimit = 3;

    public static readonly TimeSpan DefaultCallBudget = TimeSpan.FromMilliseconds(250);

    private static readonly JsonSerializerOptions ListJsonOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    private readonly Lock _gate = new();
    private readonly IPluginLocationGuard _guard;
    private readonly IPluginLog _log;
    private readonly TimeSpan _budget;
    private readonly string _directory;
    private readonly string _approvalsPath;
    private readonly List<LoadedPlugin> _plugins = [];

    public PluginHost(IPluginLocationGuard guard, IPluginLog log, PluginHostOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(log);
        _guard = guard;
        _log = log;
        var budget = options?.CallBudget ?? DefaultCallBudget;
        if (budget <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "The plugin call budget must be positive.");

        _budget = budget;
        if (!string.IsNullOrWhiteSpace(options?.Directory))
        {
            _directory = options.Directory;
            _approvalsPath = PluginPaths.DefaultApprovals(options.ApprovalsPath, null, _directory);
            return;
        }

        var location = PluginPaths.Choose(
            Environment.GetEnvironmentVariable(PluginPaths.DirectoryVariable),
            Environment.GetEnvironmentVariable(PluginPaths.ApprovalsVariable),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            guard);
        if (location.Ignored is not null)
            log.Write(location.Ignored);

        _directory = location.Directory;
        _approvalsPath = string.IsNullOrWhiteSpace(options?.ApprovalsPath) ? location.Approvals : options.ApprovalsPath;
    }

    [SupportedOSPlatform("windows")]
    public static PluginHost CreateDefault() => new(new WindowsPluginLocationGuard(), new ConsolePluginLog());

    internal int EventDeliveries { get; private set; }

    public PluginSync Sync(ConfigDocument document, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var before = PluginKeywords.Count;
        var warnings = new List<string>();
        var desired = new List<string>();
        foreach (var setting in document.Settings)
        {
            if (!setting.Keyword.Equals("plugin", StringComparison.Ordinal))
                continue;

            if (string.IsNullOrWhiteSpace(setting.Value))
            {
                warnings.Add("Plugin path is empty.");
                continue;
            }

            desired.Add(Resolve(setting.Value, baseDirectory));
        }

        List<LoadedPlugin> removed;
        lock (_gate)
        {
            removed = _plugins
                .Where(plugin => plugin.Source == PluginSource.Config && !Listed(desired, plugin.Path))
                .ToList();
            foreach (var plugin in removed)
                _plugins.Remove(plugin);
        }

        foreach (var plugin in removed)
            ShutdownAndRelease(plugin);

        if (desired.Count > 0)
        {
            var denied = _guard.Deny(_directory, _approvalsPath);
            if (denied is not null)
            {
                warnings.Add(denied);
                _log.Write(denied);
            }
            else
            {
                foreach (var path in desired)
                {
                    if (LoadedPath(path))
                        continue;

                    var report = LoadPath(path, baseDirectory: null, PluginSource.Config);
                    warnings.AddRange(report.Failures);
                }
            }
        }

        return new PluginSync(PluginKeywords.Count > before, warnings);
    }

    public ConfigDocument Incorporate(ConfigDocument document, Func<ConfigDocument> reread, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(reread);
        var sync = Sync(document, baseDirectory);
        if (!sync.RegisteredKeywords)
            return WithWarnings(document, sync.Warnings);

        var again = reread();
        var second = Sync(again, baseDirectory);
        return WithWarnings(again, second.Warnings);
    }

    public string ListJson()
    {
        LoadedPlugin[] snapshot;
        lock (_gate)
            snapshot = [.. _plugins];

        var items = snapshot
            .OrderBy(plugin => plugin.Name, StringComparer.Ordinal)
            .Select(plugin => new PluginListItem(
                plugin.Name,
                plugin.Version,
                plugin.Life == PluginLife.Disabled ? "disabled" : "loaded",
                plugin.ConsecutiveFailures))
            .ToArray();
        return JsonSerializer.Serialize(items, ListJsonOptions);
    }

    public string Load(string path)
    {
        var report = LoadPath(path, baseDirectory: null, PluginSource.Manual);
        if (report.Failures.Count == 0 && report.Loaded > 0)
            return "ok";

        var message = report.Failures.Count == 0
            ? "Plugin did not load."
            : string.Join(' ', report.Failures);
        return PipeError(message);
    }

    public string Unload(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        LoadedPlugin? plugin;
        lock (_gate)
        {
            plugin = _plugins.Find(candidate => candidate.Name == name);
            if (plugin is not null)
                _plugins.Remove(plugin);
        }

        if (plugin is null)
            return PipeError($"plugin '{name}' is not loaded.");

        ShutdownAndRelease(plugin);
        _log.Write($"Plugin '{name}' unloaded.");
        return "ok";
    }

    public void Deliver(IReadOnlyList<StreamEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            return;

        LoadedPlugin[] snapshot;
        lock (_gate)
            snapshot = [.. _plugins];

        foreach (var streamEvent in events)
        {
            var pluginEvent = PluginEvents.Map(streamEvent);
            if (pluginEvent is null)
                continue;

            foreach (var plugin in snapshot)
            {
                PluginEventHandler[] handlers;
                lock (_gate)
                    handlers = [.. plugin.Binding.Handlers];

                foreach (var handler in handlers)
                {
                    if (TryCall(plugin, "event", () => handler(pluginEvent), out _))
                    {
                        lock (_gate)
                            EventDeliveries++;
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        LoadedPlugin[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _plugins];
            _plugins.Clear();
        }

        foreach (var plugin in snapshot)
            ShutdownAndRelease(plugin);
    }

    internal WeakReference? LoadContextReference(string name)
    {
        lock (_gate)
        {
            var plugin = _plugins.Find(candidate => candidate.Name == name);
            return plugin?.Context is null ? null : new WeakReference(plugin.Context);
        }
    }

    private LoadReport LoadPath(string path, string? baseDirectory, PluginSource source)
    {
        var report = new LoadReport();
        if (string.IsNullOrWhiteSpace(path))
        {
            report.Failures.Add("Plugin path is empty.");
            return report;
        }

        var full = Resolve(path, baseDirectory);

        // The approvals are opened before the location is checked and read through that handle, so
        // what is read is the file that was checked, even when its folder is swapped in between.
        FileStream? approvals;
        try
        {
            approvals = File.Exists(_approvalsPath)
                ? new FileStream(_approvalsPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var message = $"Could not read approvals '{_approvalsPath}': {exception.Message}";
            report.Failures.Add(message);
            _log.Write(message);
            return report;
        }

        using var opened = approvals;
        var denied = _guard.Deny(_directory, _approvalsPath);
        if (denied is null && approvals is not null)
            denied = _guard.DenyApprovals(approvals);

        if (denied is not null)
        {
            report.Failures.Add(denied);
            _log.Write(denied);
            return report;
        }

        // Only the protected directory holds what was approved. A copy elsewhere, such as the
        // plugin manager's staging folder, does not load even when its hash matches.
        if (!PluginNames.IsInside(_directory, full))
        {
            var message = $"Plugin assembly '{full}' is outside the plugin directory '{_directory}'.";
            report.Failures.Add(message);
            _log.Write(message);
            return report;
        }

        if (!File.Exists(full))
        {
            report.Failures.Add($"Plugin assembly '{full}' was not found.");
            return report;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(full);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            report.Failures.Add($"Could not read '{full}'.");
            _ = exception;
            return report;
        }

        var hash = PluginHash.Of(bytes);
        var approved = approvals is null ? [] : ApprovalFile.Read(approvals, _approvalsPath, _log);
        var matches = approved
            .Where(entry => entry.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
        {
            var message = $"Assembly '{Path.GetFileName(full)}' is not approved. The file does not match an approved SHA-256.";
            report.Failures.Add(message);
            _log.Write(message);
            return report;
        }

        foreach (var approval in matches)
        {
            var outcome = LoadApproved(full, bytes, approval, source);
            if (outcome.Ok)
                report.Loaded++;
            else
                report.Failures.Add(outcome.Message);
        }

        return report;
    }

    private LoadOutcome LoadApproved(string fullPath, byte[] bytes, PluginApproval approval, PluginSource source)
    {
        lock (_gate)
        {
            if (_plugins.Exists(candidate => candidate.Name == approval.Name))
                return LoadOutcome.Failed($"Plugin '{approval.Name}' is already loaded.");
        }

        var context = new PluginLoadContext();
        var worker = new PluginWorker(approval.Name);
        Assembly assembly;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            assembly = context.LoadFromStream(stream);
        }
        catch (Exception loadError) when (loadError is BadImageFormatException or FileLoadException or IOException)
        {
            Release(context, worker, approval.Name);
            return LoadOutcome.Failed($"Could not load '{Path.GetFileName(fullPath)}': {loadError.Message}");
        }

        // Constructors and the Name and ApiVersion getters are plugin code too. They run under
        // the same budget and guard as every other call, so one that throws or hangs refuses
        // the load instead of ending or freezing Winland at logon.
        var problems = new List<string>();
        var candidate = CreatePlugins(worker, assembly, problems).Find(found => found.Name == approval.Name);
        if (candidate is null)
        {
            Release(context, worker, approval.Name);
            var detail = problems.Count == 0 ? "" : " " + string.Join(' ', problems);
            return LoadOutcome.Failed($"Approved plugin '{approval.Name}' was not found in '{Path.GetFileName(fullPath)}'.{detail}");
        }

        if (candidate.Problem is not null)
        {
            Release(context, worker, approval.Name);
            return LoadOutcome.Failed($"Plugin '{candidate.Name}' {candidate.Problem}");
        }

        if (candidate.ApiVersion != PluginApiVersion.Current)
        {
            Release(context, worker, approval.Name);
            return LoadOutcome.Failed(
                $"Plugin '{candidate.Name}' declares API version {candidate.ApiVersion.ToString(CultureInfo.InvariantCulture)}; Winland accepts {PluginApiVersion.Current.ToString(CultureInfo.InvariantCulture)}.");
        }

        var plugin = candidate.Plugin;
        var loaded = new LoadedPlugin
        {
            Name = candidate.Name,
            Version = candidate.ApiVersion,
            Path = fullPath,
            Source = source,
            Instance = plugin,
            Context = context,
            Worker = worker,
        };
        loaded.Binding = new Binding(this, loaded);
        var started = worker.Run(_budget, () => plugin.Initialize(loaded.Binding), out var exception, out var timedOut);
        if (!started)
        {
            loaded.Binding.Close();
            loaded.Instance = null;
            loaded.Context = null;
            Release(context, worker, loaded.Name);
            if (timedOut)
            {
                var message = $"Plugin '{loaded.Name}' initialize exceeded {BudgetText()} and was abandoned.";
                _log.Write(message);
                return LoadOutcome.Failed(message);
            }

            var failed = $"Plugin '{loaded.Name}' initialize failed: {exception?.Message}";
            _log.Write(failed);
            return LoadOutcome.Failed(failed);
        }

        lock (_gate)
        {
            if (_plugins.Exists(candidate => candidate.Name == loaded.Name))
            {
                loaded.Unloading = true;
            }
            else
            {
                _plugins.Add(loaded);
                _log.Write($"Plugin '{loaded.Name}' loaded.");
                return LoadOutcome.Succeeded();
            }
        }

        ShutdownAndRelease(loaded);
        return LoadOutcome.Failed($"Plugin '{loaded.Name}' is already loaded.");
    }

    private bool TryCall(LoadedPlugin plugin, string what, Action action, out string? error)
    {
        lock (_gate)
        {
            if (plugin.Unloading)
            {
                error = PipeError($"plugin '{plugin.Name}' is unloaded.");
                return false;
            }

            if (plugin.Life == PluginLife.Disabled)
            {
                error = PipeError($"plugin '{plugin.Name}' is disabled.");
                return false;
            }
        }

        var finished = plugin.Worker.Run(_budget, action, out var exception, out var timedOut);
        lock (_gate)
        {
            if (timedOut)
            {
                NoteFailure(plugin, $"Plugin '{plugin.Name}' {what} exceeded {BudgetText()} and was abandoned.");
                error = PipeError($"plugin '{plugin.Name}' {what} exceeded the time budget.");
                return false;
            }

            if (!finished || exception is not null)
            {
                var detail = exception?.Message ?? "failed";
                NoteFailure(plugin, $"Plugin '{plugin.Name}' {what} failed: {detail}");
                error = PipeError($"plugin '{plugin.Name}' {what} failed: {detail}");
                return false;
            }

            if (plugin.Life != PluginLife.Disabled)
                plugin.ConsecutiveFailures = 0;

            error = null;
            return true;
        }
    }

    private void NoteFailure(LoadedPlugin plugin, string message)
    {
        _log.Write(message);
        if (plugin.Life == PluginLife.Disabled || plugin.Unloading)
            return;

        plugin.ConsecutiveFailures++;
        if (plugin.ConsecutiveFailures >= FailureLimit)
        {
            plugin.Life = PluginLife.Disabled;
            _log.Write($"Plugin '{plugin.Name}' is disabled after {FailureLimit.ToString(CultureInfo.InvariantCulture)} consecutive failures.");
        }
    }

    private DispatcherResult InvokeDispatcher(LoadedPlugin plugin, string name, PluginDispatcher handler, string arguments)
    {
        PluginResult result = default;
        if (!TryCall(plugin, $"dispatcher '{name}'", () => result = handler(arguments ?? ""), out var error))
            return new DispatcherResult(error, null);

        if (!result.Ok)
        {
            var detail = string.IsNullOrWhiteSpace(result.Error) ? "failed" : result.Error;
            return new DispatcherResult(PipeError($"plugin '{plugin.Name}' dispatcher '{name}' failed: {detail}"), null);
        }

        return new DispatcherResult(null, result.Value);
    }

    private void ApplyKeyword(LoadedPlugin plugin, string name, PluginKeywordHandler handler, string value, Action<string> warn)
    {
        var sink = new WarningSink();
        if (!TryCall(plugin, $"keyword '{name}'", () => handler(value, sink), out var error) && error is not null)
            warn(Strip(error));

        foreach (var warning in sink.Snapshot())
            warn(warning);
    }

    private void ShutdownAndRelease(LoadedPlugin plugin)
    {
        lock (_gate)
            plugin.Unloading = true;

        if (plugin.Instance is IPlugin instance)
        {
            var finished = plugin.Worker.Run(_budget, instance.Shutdown, out var exception, out var timedOut);
            if (timedOut)
                _log.Write($"Plugin '{plugin.Name}' shutdown exceeded {BudgetText()} and was abandoned.");
            else if (!finished)
                _log.Write($"Plugin '{plugin.Name}' shutdown failed: {exception?.Message}");
        }

        plugin.Binding.Close();
        var context = plugin.Context;
        plugin.Context = null;
        plugin.Instance = null;
        Release(context, plugin.Worker, plugin.Name);
    }

    // The worker's thread ends once its queue drains, so it no longer holds the context.
    private void Release(PluginLoadContext? context, PluginWorker worker, string name)
    {
        worker.Dispose();
        ReleaseContext(context, name);
    }

    private void ReleaseContext(PluginLoadContext? context, string name)
    {
        if (context is null)
            return;

        try
        {
            context.Unload();
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            _log.Write($"Plugin '{name}' context did not unload: {exception.Message}");
        }
    }

    private bool LoadedPath(string path)
    {
        lock (_gate)
            return _plugins.Exists(plugin => plugin.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    private string BudgetText() =>
        ((int)_budget.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + " ms";

    private static bool Listed(List<string> paths, string path) =>
        paths.Exists(candidate => candidate.Equals(path, StringComparison.OrdinalIgnoreCase));

    private static string Resolve(string path, string? baseDirectory)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        var root = string.IsNullOrWhiteSpace(baseDirectory) ? Environment.CurrentDirectory : baseDirectory;
        return Path.GetFullPath(Path.Combine(root, path));
    }

    private static ConfigDocument WithWarnings(ConfigDocument document, IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
            return document;

        var combined = new List<ConfigWarning>(document.Warnings.Count + warnings.Count);
        combined.AddRange(document.Warnings);
        foreach (var warning in warnings)
            combined.Add(new ConfigWarning(0, warning, null));

        return document with { Warnings = combined };
    }

    private static string PipeError(string message) =>
        message.StartsWith("error:", StringComparison.Ordinal) ? message : "error: " + message;

    private static string Strip(string message) =>
        message.StartsWith("error: ", StringComparison.Ordinal) ? message["error: ".Length..] : message;

    // Each type is constructed and asked its name and API version in one guarded call. A type
    // whose name cannot be read is skipped and described in problems; one whose API version
    // cannot be read is kept with the problem, so the load names it.
    private List<Candidate> CreatePlugins(PluginWorker worker, Assembly assembly, List<string> problems)
    {
        IEnumerable<Type> types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types.Where(type => type is not null)!;
        }

        var plugins = new List<Candidate>();
        foreach (var type in types)
        {
            if (type is null || type.IsAbstract || !type.IsClass || type.IsInterface || !typeof(IPlugin).IsAssignableFrom(type))
                continue;

            if (type.GetConstructor(Type.EmptyTypes) is null)
                continue;

            IPlugin? plugin = null;
            string? name = null;
            if (!worker.Run(_budget, () =>
                {
                    plugin = Activator.CreateInstance(type) as IPlugin;
                    name = plugin?.Name;
                }, out var exception, out var timedOut))
            {
                problems.Add(timedOut
                    ? $"Constructing '{type.FullName}' exceeded {BudgetText()} and was abandoned."
                    : $"Constructing '{type.FullName}' failed: {(exception?.InnerException ?? exception)?.Message}");
                continue;
            }

            if (plugin is null || string.IsNullOrWhiteSpace(name))
                continue;

            var version = 0;
            var found = plugin;
            string? problem = null;
            if (!worker.Run(_budget, () => version = found.ApiVersion, out exception, out timedOut))
            {
                problem = timedOut
                    ? $"API version exceeded {BudgetText()} and was abandoned."
                    : $"API version failed: {exception?.Message}";
            }

            plugins.Add(new Candidate(found, name, version, problem));
        }

        return plugins;
    }

    private sealed record Candidate(IPlugin Plugin, string Name, int ApiVersion, string? Problem);

    private enum PluginSource
    {
        Config,
        Manual,
    }

    private enum PluginLife
    {
        Loaded,
        Disabled,
    }

    private sealed class LoadedPlugin
    {
        public required string Name { get; init; }

        public required int Version { get; init; }

        public required string Path { get; init; }

        public required PluginSource Source { get; init; }

        public PluginLife Life { get; set; }

        public int ConsecutiveFailures { get; set; }

        public bool Unloading { get; set; }

        public IPlugin? Instance { get; set; }

        public PluginLoadContext? Context { get; set; }

        public Binding Binding { get; set; } = null!;

        public required PluginWorker Worker { get; init; }
    }

    private sealed class LoadReport
    {
        public int Loaded { get; set; }

        public List<string> Failures { get; } = [];
    }

    private readonly record struct LoadOutcome(bool Ok, string Message)
    {
        public static LoadOutcome Succeeded() => new(true, "");

        public static LoadOutcome Failed(string message) => new(false, message);
    }

    private sealed class Binding : IPluginHost
    {
        private readonly PluginHost _host;
        private readonly LoadedPlugin _plugin;
        private readonly Lock _gate = new();
        private readonly List<string> _dispatchers = [];
        private readonly List<string> _keywords = [];
        private readonly List<PluginEventHandler> _handlers = [];
        private int _open = 1;

        public Binding(PluginHost host, LoadedPlugin plugin)
        {
            _host = host;
            _plugin = plugin;
        }

        public IReadOnlyList<PluginEventHandler> Handlers
        {
            get
            {
                lock (_gate)
                    return [.. _handlers];
            }
        }

        public void RegisterDispatcher(string name, PluginDispatcher handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            lock (_gate)
            {
                if (_open == 0)
                    return;

                if (!Dispatchers.TryBind(name, arguments => _host.InvokeDispatcher(_plugin, name, handler, arguments), out var error))
                    throw new InvalidOperationException(error);

                _dispatchers.Add(name);
            }
        }

        public void RegisterKeyword(string name, PluginKeywordHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            lock (_gate)
            {
                if (_open == 0)
                    return;

                if (name.Equals("plugin", StringComparison.Ordinal))
                    throw new InvalidOperationException("The plugin keyword is reserved.");

                PluginKeywords.Register(name, (value, warn) => _host.ApplyKeyword(_plugin, name, handler, value, warn));
                _keywords.Add(name);
            }
        }

        public void Subscribe(PluginEventHandler handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            lock (_gate)
            {
                if (_open == 0)
                    return;

                _handlers.Add(handler);
            }
        }

        public LayoutRegistration RegisterLayout(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            return new LayoutRegistration(false, "Plugin layouts are not supported.");
        }

        public void Close()
        {
            lock (_gate)
            {
                _open = 0;
                foreach (var name in _dispatchers)
                    Dispatchers.Unbind(name);

                _dispatchers.Clear();
                foreach (var name in _keywords)
                    PluginKeywords.Unregister(name);

                _keywords.Clear();
                _handlers.Clear();
            }
        }
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        public PluginLoadContext()
            : base(isCollectible: true)
        {
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == "Winland.PluginApi")
                return typeof(IPlugin).Assembly;

            return null;
        }
    }

    private sealed class WarningSink : IPluginWarnings
    {
        private readonly Lock _gate = new();
        private readonly List<string> _warnings = [];

        public void Warn(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            lock (_gate)
                _warnings.Add(message);
        }

        public IReadOnlyList<string> Snapshot()
        {
            lock (_gate)
                return [.. _warnings];
        }
    }

    private sealed record PluginListItem(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("failures")] int Failures);
}
