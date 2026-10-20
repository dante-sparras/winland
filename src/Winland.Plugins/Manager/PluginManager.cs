namespace Winland.Plugins.Manager;

/// <summary>
/// <c>winlandctl plugin install|update|remove|list --installed</c>. It clones and builds as the
/// current user, shows what it built, and hands the copy and the approval to one elevated step.
/// </summary>
public sealed class PluginManager(
    PluginManagerPaths paths,
    IProcessRunner runner,
    IElevator elevator,
    IRunningPlugins running,
    TextWriter output,
    TextWriter error)
{
    private readonly PluginStaging _staging = new(paths, runner);

    public int Install(string url, string? revision)
    {
        if (RefuseElevated("install") || !ValidArgument(url, "URL") || (revision is not null && !ValidArgument(revision, "revision")))
            return 1;

        if (!TryReadInstalled(out var installed))
            return 1;

        if (installed.Any(entry => SameUrl(entry.Repository, url)))
        {
            error.WriteLine($"Repository '{url}' is already installed. Use 'winlandctl plugin update'.");
            return 1;
        }

        var outcome = _staging.Stage(url, revision, installedCommit: null, force: true, output);
        if (outcome is not StageOutcome.Built built)
            return Report(outcome);

        foreach (var plugin in built.Repository.Plugins)
        {
            var owner = installed.FirstOrDefault(entry => PluginNames.Same(entry.Name, plugin.Name));
            if (owner is not null)
            {
                var from = owner.Repository is null ? "by hand" : $"from '{owner.Repository}'";
                error.WriteLine($"Plugin '{plugin.Name}' is already approved {from}. Remove it first.");
                return 1;
            }
        }

        if (!Apply([built.Repository], []))
            return 1;

        // As in the pinned plugin manager, a new plugin starts disabled.
        Remember(built.Repository);
        foreach (var plugin in built.Repository.Plugins)
            output.WriteLine($"Enable it with 'winlandctl plugin enable {plugin.Name}', or add 'plugin = {Target(plugin.Name)}' to the config.");

        return 0;
    }

    public int Update(bool force)
    {
        if (RefuseElevated("update") || !TryReadInstalled(out var installed))
            return 1;

        var repositories = installed
            .Where(entry => entry.Repository is not null)
            .GroupBy(entry => entry.Repository!, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (repositories.Length == 0)
        {
            output.WriteLine("No plugin repositories are installed.");
            return 0;
        }

        var failed = false;
        var staged = new List<StagedRepository>();
        foreach (var repository in repositories)
        {
            var commit = repository.First().Commit;
            var outcome = _staging.Stage(repository.Key, revision: null, commit, force, output);
            switch (outcome)
            {
                case StageOutcome.Built built:
                    staged.Add(built.Repository);
                    break;
                case StageOutcome.UpToDate:
                    output.WriteLine($"{repository.Key} is up to date.");
                    break;
                default:
                    Report(outcome);
                    failed = true;
                    break;
            }
        }

        if (staged.Count == 0)
            return failed ? 1 : 0;

        if (!Apply(staged, []))
            return 1;

        foreach (var repository in staged)
            Remember(repository);

        // A plugin the manifest no longer lists was removed with its file and approval, so a
        // copy still loaded in Winland is unloaded too.
        foreach (var repository in staged)
        {
            var dropped = installed.Where(entry => SameUrl(entry.Repository, repository.Url)
                && !repository.Plugins.Any(plugin => PluginNames.Same(plugin.Name, entry.Name)));
            foreach (var entry in dropped)
            {
                if (running.Unload(entry.Name))
                    output.WriteLine($"Unloaded plugin '{entry.Name}', which the manifest no longer lists.");
            }
        }

        // The host reads an assembly into memory, so the file can be replaced while it is loaded.
        foreach (var plugin in staged.SelectMany(repository => repository.Plugins))
        {
            if (!running.Unload(plugin.Name))
                continue;

            var target = Target(plugin.Name);
            if (running.Load(target))
            {
                output.WriteLine($"Reloaded plugin '{plugin.Name}'.");
                continue;
            }

            error.WriteLine($"Plugin '{plugin.Name}' was unloaded but did not load again. Load it with 'winlandctl plugin load {target}'.");
            failed = true;
        }

        return failed ? 1 : 0;
    }

    public int Remove(string identifier)
    {
        if (!ValidArgument(identifier, "repository or plugin") || !TryReadInstalled(out var installed))
            return 1;

        var named = Identify(identifier, Repositories(installed)) is ManagedRepository repository
            ? installed.FirstOrDefault(entry => SameUrl(entry.Repository, repository.Url))
            : installed.FirstOrDefault(entry => PluginNames.Same(entry.Name, identifier));
        if (named is null)
        {
            error.WriteLine($"No installed repository or plugin is named '{identifier}'.");
            return 1;
        }

        if (named.Repository is null)
        {
            error.WriteLine($"Plugin '{named.Name}' was approved by hand. Remove it from '{paths.Approvals}' instead.");
            return 1;
        }

        var url = named.Repository;
        output.WriteLine($"Removing {url}");
        if (!Apply([], [url]))
            return 1;

        foreach (var entry in installed.Where(entry => SameUrl(entry.Repository, url)))
        {
            if (running.Unload(entry.Name))
                output.WriteLine($"Unloaded plugin '{entry.Name}'.");
        }

        _staging.Clean(url);
        Forget(url);
        return 0;
    }

    /// <summary>
    /// Enables or disables an installed plugin, named alone or as <c>author/name</c>, then loads and
    /// unloads plugins in the running Winland as <see cref="Reload"/> does.
    /// </summary>
    public int Enable(string identifier, bool enabled)
    {
        var verb = enabled ? "enable" : "disable";
        if (!ValidArgument(identifier, "plugin") || !TryReadInstalled(out var installed))
            return 1;

        var repositories = Repositories(installed).ToList();
        var slash = identifier.IndexOf('/', StringComparison.Ordinal);
        var author = slash < 0 ? null : identifier[..slash];
        var name = slash < 0 ? identifier : identifier[(slash + 1)..];
        var index = repositories.FindIndex(repository => (author is null || repository.Author == author)
            && repository.Enabled.Keys.Any(plugin => PluginNames.Same(plugin, name)));
        if (identifier.Contains(':', StringComparison.Ordinal) || index < 0)
        {
            error.WriteLine($"Couldn't {verb} plugin (missing?)");
            return 1;
        }

        var found = repositories[index];
        var key = found.Enabled.Keys.First(plugin => PluginNames.Same(plugin, name));
        var flags = new Dictionary<string, bool>(found.Enabled, StringComparer.OrdinalIgnoreCase) { [key] = enabled };
        repositories[index] = found with { Enabled = flags };
        if (!TryWriteState(repositories))
            return 1;

        output.WriteLine(enabled ? $"Enabled {key}" : $"Disabled {key}");
        return EnsureLoadState(installed, repositories, force: false);
    }

    /// <summary>
    /// Unloads every plugin loaded in the running Winland that is not enabled, and loads every enabled one that is
    /// not loaded, as the pinned plugin manager's <c>reload</c> does. With <paramref name="force"/> every loaded
    /// plugin is unloaded and every enabled one loaded again.
    /// </summary>
    public int Reload(bool force)
    {
        if (!TryReadInstalled(out var installed))
            return 1;

        return EnsureLoadState(installed, Repositories(installed), force);
    }

    public int List()
    {
        if (!TryReadInstalled(out var installed))
            return 1;

        if (installed.Count == 0)
        {
            output.WriteLine("No plugins are installed.");
            return 0;
        }

        var repositories = Repositories(installed);
        foreach (var repository in installed.GroupBy(entry => entry.Repository, StringComparer.OrdinalIgnoreCase))
        {
            if (repository.Key is null)
                output.WriteLine("Approved by hand");
            else
                output.WriteLine($"Repository {repository.Key} (commit {repository.First().Commit ?? "unknown"})");

            var managed = repository.Key is null ? null : repositories.FirstOrDefault(entry => SameUrl(entry.Url, repository.Key));
            foreach (var plugin in repository)
            {
                var enabled = managed is null ? "" : managed.Enabled.GetValueOrDefault(plugin.Name) ? "  enabled" : "  disabled";
                output.WriteLine($"  {plugin.Name}{enabled}  {plugin.File ?? "-"}  sha256 {plugin.Sha256}");
            }
        }

        return 0;
    }

    private int EnsureLoadState(IReadOnlyList<InstalledPlugin> installed, IReadOnlyList<ManagedRepository> repositories, bool force)
    {
        var loaded = running.Loaded();
        if (loaded is null)
        {
            error.WriteLine("Winland is not running, so no plugin was loaded or unloaded.");
            return 1;
        }

        bool Enabled(string name) =>
            repositories.Any(repository => repository.Enabled.Any(pair => pair.Value && PluginNames.Same(pair.Key, name)));

        // As in the pinned plugin manager, a loaded plugin that is not enabled is unloaded, whatever loaded it.
        var failed = false;
        foreach (var name in loaded)
        {
            if (!force && Enabled(name))
                continue;

            if (running.Unload(name))
            {
                output.WriteLine($"Unloaded {name}");
                continue;
            }

            error.WriteLine($"Could not unload {name}.");
            failed = true;
        }

        foreach (var plugin in installed.Where(entry => entry.Repository is not null && Enabled(entry.Name)))
        {
            if (!force && loaded.Any(name => PluginNames.Same(name, plugin.Name)))
                continue;

            if (running.Load(Target(plugin.Name)))
            {
                output.WriteLine($"Loaded {plugin.Name}");
                continue;
            }

            error.WriteLine($"Could not load {plugin.Name}.");
            failed = true;
        }

        return failed ? 1 : 0;
    }

    // Each installed repository with its name, its author and which of its plugins are enabled. A repository
    // installed before the state was kept is named after its URL and has every plugin disabled.
    private IReadOnlyList<ManagedRepository> Repositories(IReadOnlyList<InstalledPlugin> installed)
    {
        var known = ReadState();
        return installed
            .Where(entry => entry.Repository is not null)
            .GroupBy(entry => entry.Repository!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var state = known.FirstOrDefault(entry => SameUrl(entry.Url, group.Key));
                var enabled = group.ToDictionary(
                    entry => entry.Name,
                    entry => state?.Enabled.GetValueOrDefault(entry.Name) ?? false,
                    StringComparer.OrdinalIgnoreCase);
                return new ManagedRepository(group.Key, state?.Name ?? ManagerState.NameOf(group.Key), ManagerState.AuthorOf(group.Key), enabled);
            })
            .ToArray();
    }

    // A URL, an author/name or a repository name, as the pinned plugin manager reads a repository.
    private static ManagedRepository? Identify(string identifier, IReadOnlyList<ManagedRepository> repositories)
    {
        if (identifier.Contains(':', StringComparison.Ordinal))
            return repositories.FirstOrDefault(repository => SameUrl(repository.Url, identifier));

        var slash = identifier.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
        {
            var author = identifier[..slash];
            var name = identifier[(slash + 1)..];
            return repositories.FirstOrDefault(repository => repository.Author == author && repository.Name == name);
        }

        return repositories.FirstOrDefault(repository => repository.Name == identifier);
    }

    private void Remember(StagedRepository staged)
    {
        var known = ReadState();
        var before = known.FirstOrDefault(entry => SameUrl(entry.Url, staged.Url));
        var enabled = staged.Plugins.ToDictionary(
            plugin => plugin.Name,
            plugin => before?.Enabled.GetValueOrDefault(plugin.Name) ?? false,
            StringComparer.OrdinalIgnoreCase);
        var name = staged.Name ?? ManagerState.NameOf(staged.Url);
        TryWriteState([.. known.Where(entry => !SameUrl(entry.Url, staged.Url)), new ManagedRepository(staged.Url, name, ManagerState.AuthorOf(staged.Url), enabled)]);
    }

    private void Forget(string url) =>
        TryWriteState(ReadState().Where(entry => !SameUrl(entry.Url, url)).ToArray());

    private IReadOnlyList<ManagedRepository> ReadState()
    {
        try
        {
            return ManagerState.Read(StatePath);
        }
        catch (InvalidDataException exception)
        {
            error.WriteLine(exception.Message);
            return [];
        }
    }

    private bool TryWriteState(IReadOnlyList<ManagedRepository> repositories)
    {
        try
        {
            ManagerState.Write(StatePath, repositories);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Could not write the plugin state '{StatePath}': {exception.Message}");
            return false;
        }
    }

    private string StatePath => Path.Combine(paths.Staging, ManagerState.FileName);

    private bool Apply(IReadOnlyList<StagedRepository> install, IReadOnlyList<string> remove)
    {
        foreach (var repository in install)
        {
            output.WriteLine($"Repository: {repository.Url}");
            output.WriteLine($"Commit:     {repository.Commit}");
            foreach (var plugin in repository.Plugins)
            {
                output.WriteLine($"Plugin:     {plugin.Name}");
                output.WriteLine($"  Built:    {plugin.Source}");
                output.WriteLine($"  SHA-256:  {plugin.Sha256}");
            }
        }

        var request = new ApplyRequest(paths.PluginDirectory, paths.Approvals, null, install, remove);
        output.WriteLine("Confirm the Windows prompt to approve this change.");
        var run = elevator.Run(request.ToArguments());
        foreach (var line in run.Lines)
            (run.ExitCode == 0 ? output : error).WriteLine(line);

        if (!run.Started)
        {
            error.WriteLine(run.Error ?? "The approval was cancelled.");
            error.WriteLine("Nothing was changed.");
            return false;
        }

        if (run.ExitCode != 0 && run.Lines.Count == 0)
            error.WriteLine($"The elevated step failed with exit code {run.ExitCode} and sent no report.");

        return run.ExitCode == 0;
    }

    private bool RefuseElevated(string verb)
    {
        if (!elevator.IsElevated)
            return false;

        error.WriteLine($"Run 'winlandctl plugin {verb}' from a shell that is not elevated. Building runs the repository's code, and that must not run as an administrator.");
        return true;
    }

    private bool ValidArgument(string value, string what)
    {
        if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith('-') && value.IndexOfAny(['\r', '\n', '"']) < 0)
            return true;

        error.WriteLine($"'{value}' is not a valid {what}.");
        return false;
    }

    private bool TryReadInstalled(out IReadOnlyList<InstalledPlugin> installed)
    {
        try
        {
            installed = InstalledPlugins.Read(paths.Approvals);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine(exception.Message);
            installed = [];
            return false;
        }
    }

    private int Report(StageOutcome outcome)
    {
        if (outcome is StageOutcome.Failed failed)
        {
            foreach (var message in failed.Messages)
                error.WriteLine(message);
        }

        return 1;
    }

    private string Target(string name) => Path.Combine(paths.PluginDirectory, name + ".dll");

    private static bool SameUrl(string? left, string right) =>
        left is not null && left.Equals(right, StringComparison.OrdinalIgnoreCase);
}
