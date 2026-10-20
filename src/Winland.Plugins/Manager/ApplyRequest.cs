namespace Winland.Plugins.Manager;

/// <summary>A plugin assembly built in staging, with the hash the user was shown.</summary>
public sealed record StagedPlugin(string Name, string Sha256, string Source);

/// <summary>A repository at one commit and the plugins built from it.</summary>
public sealed record StagedRepository(string Url, string Commit, IReadOnlyList<StagedPlugin> Plugins)
{
    /// <summary>The manifest's repository name. The elevated step does not need it, so it is not passed on.</summary>
    public string? Name { get; init; }
}

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

    /// <summary>The SID of the user who asked for the step, which owns <see cref="ReportPipe"/>.</summary>
    public string? ReportOwner { get; init; }

    public IReadOnlyList<string> ToArguments()
    {
        var args = new List<string> { "plugin", Verb, "--plugin-dir", PluginDirectory, "--approvals", ApprovalsPath };
        if (ReportPipe is not null)
            args.AddRange(["--report-pipe", ReportPipe]);

        if (ReportOwner is not null)
            args.AddRange(["--report-owner", ReportOwner]);

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
        string? reportOwner = null;
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
                case "--report-owner" when left >= 1 && IsSid(args[i + 1]):
                    reportOwner = args[++i];
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

        return new ApplyRequest(directory, approvals, reportPipe, install, remove) { ReportOwner = reportOwner };
    }

    // S-1- followed by digits and dashes, as a SID is written.
    private static bool IsSid(string value) =>
        value.Length is > 4 and <= 184
        && value.StartsWith("S-1-", StringComparison.Ordinal)
        && !value.AsSpan(4).ContainsAnyExcept("0123456789-");
}
