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
            var owner = installed.FirstOrDefault(entry => entry.Name == plugin.Name);
            if (owner is not null)
            {
                var from = owner.Repository is null ? "by hand" : $"from '{owner.Repository}'";
                error.WriteLine($"Plugin '{plugin.Name}' is already approved {from}. Remove it first.");
                return 1;
            }
        }

        if (!Apply([built.Repository], []))
            return 1;

        foreach (var plugin in built.Repository.Plugins)
        {
            var path = Target(plugin.Name);
            output.WriteLine($"Load it with 'winlandctl plugin load {path}', or add 'plugin = {path}' to the config.");
        }

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

        // A plugin the manifest no longer lists was removed with its file and approval, so a
        // copy still loaded in Winland is unloaded too.
        foreach (var repository in staged)
        {
            var dropped = installed.Where(entry => SameUrl(entry.Repository, repository.Url)
                && !repository.Plugins.Any(plugin => plugin.Name == entry.Name));
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

        var named = installed.FirstOrDefault(entry => SameUrl(entry.Repository, identifier))
            ?? installed.FirstOrDefault(entry => entry.Name == identifier);
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
        return 0;
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

        foreach (var repository in installed.GroupBy(entry => entry.Repository, StringComparer.OrdinalIgnoreCase))
        {
            if (repository.Key is null)
                output.WriteLine("Approved by hand");
            else
                output.WriteLine($"Repository {repository.Key} (commit {repository.First().Commit ?? "unknown"})");

            foreach (var plugin in repository)
                output.WriteLine($"  {plugin.Name}  {plugin.File ?? "-"}  sha256 {plugin.Sha256}");
        }

        return 0;
    }

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
