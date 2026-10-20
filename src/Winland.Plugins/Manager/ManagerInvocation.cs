namespace Winland.Plugins.Manager;

/// <summary>A <c>winlandctl plugin</c> command that the manager runs itself, without the pipe.</summary>
public abstract record ManagerCommand
{
    private ManagerCommand()
    {
    }

    public sealed record Install(string Url, string? Revision) : ManagerCommand;

    public sealed record Update(bool Force) : ManagerCommand;

    public sealed record Remove(string Identifier) : ManagerCommand;

    public sealed record Enable(string Identifier, bool Enabled) : ManagerCommand;

    public sealed record Reload(bool Force) : ManagerCommand;

    public sealed record List : ManagerCommand;

    public sealed record Apply(ApplyRequest Request) : ManagerCommand;

    public sealed record Invalid(string Message) : ManagerCommand;
}

public static class ManagerInvocation
{
    /// <summary>
    /// Reads a manager command. Returns null for anything else, including <c>plugin list</c>,
    /// <c>plugin load</c> and <c>plugin unload</c>, which go to the pipe.
    /// </summary>
    public static ManagerCommand? Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count < 2 || !args[0].Equals("plugin", StringComparison.OrdinalIgnoreCase))
            return null;

        var verb = args[1].ToLowerInvariant();
        var rest = args.Skip(2).ToArray();
        switch (verb)
        {
            case "install":
                if (rest.Length is < 1 or > 2)
                    return new ManagerCommand.Invalid("usage: winlandctl plugin install <git-url> [revision]");

                return new ManagerCommand.Install(rest[0], rest.Length == 2 ? rest[1] : null);
            case "update":
                if (rest.Length == 0)
                    return new ManagerCommand.Update(false);

                if (rest.Length == 1 && rest[0] is "--force" or "-f")
                    return new ManagerCommand.Update(true);

                return new ManagerCommand.Invalid("usage: winlandctl plugin update [--force]");
            case "remove":
                if (rest.Length != 1)
                    return new ManagerCommand.Invalid("usage: winlandctl plugin remove <git-url|name|author/name>");

                return new ManagerCommand.Remove(rest[0]);
            case "enable" or "disable":
                if (rest.Length != 1)
                    return new ManagerCommand.Invalid($"usage: winlandctl plugin {verb} <name|author/name>");

                return new ManagerCommand.Enable(rest[0], verb == "enable");
            case "reload":
                if (rest.Length == 0)
                    return new ManagerCommand.Reload(false);

                if (rest.Length == 1 && rest[0] is "--force" or "-f")
                    return new ManagerCommand.Reload(true);

                return new ManagerCommand.Invalid("usage: winlandctl plugin reload [--force]");
            case "list" when rest.Length == 1 && rest[0] == "--installed":
                return new ManagerCommand.List();
            case ApplyRequest.Verb:
                var request = ApplyRequest.Parse(rest, out var error);
                return request is null ? new ManagerCommand.Invalid(error) : new ManagerCommand.Apply(request);
            default:
                return null;
        }
    }
}
