namespace Winland.Plugins.Manager;

/// <summary>A plugin assembly built in staging, with the hash the user was shown.</summary>
public sealed record StagedPlugin(string Name, string Sha256, string Source);

/// <summary>A repository at one commit and the plugins built from it.</summary>
public sealed record StagedRepository(string Url, string Commit, IReadOnlyList<StagedPlugin> Plugins);

/// <summary>
/// What the elevated step does: install or replace repositories and remove others.
/// It travels on the command line of <c>winlandctl plugin apply</c>, so the elevated
/// process reads nothing a non-administrator could change after the user approved it,
/// except the staged assemblies, whose hashes it checks after copying them.
/// </summary>
public sealed record ApplyRequest(
    string PluginDirectory,
    string ApprovalsPath,
    string? ReportPipe,
    IReadOnlyList<StagedRepository> Install,
    IReadOnlyList<string> Remove)
{
    public const string Verb = "apply";

    public IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "plugin", Verb, "--plugin-dir", PluginDirectory, "--approvals", ApprovalsPath };
        if (ReportPipe is not null)
            args.AddRange(["--report-pipe", ReportPipe]);

        foreach (var repository in Install)
        {
            args.AddRange(["--repository", repository.Url, "--commit", repository.Commit]);
            foreach (var plugin in repository.Plugins)
                args.AddRange(["--plugin", plugin.Name, plugin.Sha256, plugin.Source]);
        }

        foreach (var url in Remove)
            args.AddRange(["--remove", url]);

        return args;
    }

    /// <summary>Reads the arguments after <c>plugin apply</c>. Returns null and an error when they are not valid.</summary>
    public static ApplyRequest? Parse(IReadOnlyList<string> args, out string error)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? directory = null;
        string? approvals = null;
        string? reportPipe = null;
        var install = new List<StagedRepository>();
        var remove = new List<string>();
        string? url = null;
        string? commit = null;
        var plugins = new List<StagedPlugin>();
        error = "";

        void Flush()
        {
            if (url is not null)
                install.Add(new StagedRepository(url, commit ?? "", plugins.ToArray()));

            url = null;
            commit = null;
            plugins.Clear();
        }

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            var left = args.Count - i - 1;
            switch (arg)
            {
                case "--plugin-dir" when left >= 1:
                    directory = args[++i];
                    break;
                case "--approvals" when left >= 1:
                    approvals = args[++i];
                    break;
                case "--report-pipe" when left >= 1 && ApplyReport.IsName(args[i + 1]):
                    reportPipe = args[++i];
                    break;
                case "--repository" when left >= 1:
                    Flush();
                    url = args[++i];
                    break;
                case "--commit" when left >= 1 && url is not null:
                    commit = args[++i];
                    break;
                case "--plugin" when left >= 3 && url is not null:
                    plugins.Add(new StagedPlugin(args[i + 1], args[i + 2].ToLowerInvariant(), args[i + 3]));
                    i += 3;
                    break;
                case "--remove" when left >= 1:
                    remove.Add(args[++i]);
                    break;
                default:
                    error = $"Unexpected argument '{arg}'.";
                    return null;
            }
        }

        Flush();
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(approvals))
        {
            error = "The plugin directory and the approvals file are required.";
            return null;
        }

        if (install.Count == 0 && remove.Count == 0)
        {
            error = "Nothing to install or remove.";
            return null;
        }

        return new ApplyRequest(directory, approvals, reportPipe, install, remove);
    }
}
