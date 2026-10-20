namespace Winland.Plugins.Manager;

/// <summary>A <c>winlandctl plugin</c> command that the manager runs itself, without the pipe.</summary>
public abstract record ManagerCommand
{
    private ManagerCommand()
    {
    }

    /// <summary>Whether to show a notification in the running Winland when plugins load, as <c>--notify</c> asks.</summary>
    public bool Notify { get; init; }

    /// <summary>Whether to print each command the manager runs and what it printed, as <c>--verbose</c> asks.</summary>
    public bool Verbose { get; init; }

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

        // The verb is the first word that is not a flag, so a flag may come before it too.
        var at = 1;
        while (at < args.Count && args[at].StartsWith('-'))
            at += args[at] == "--hl-url" ? 2 : 1;

        if (at >= args.Count)
            return at == 1 ? null : new ManagerCommand.Invalid("usage: winlandctl plugin <command> [flags]");

        var verb = args[at].ToLowerInvariant();
        if (verb is not ("install" or "update" or "remove" or "enable" or "disable" or "reload"))
            return at == 1 ? Other(verb, args.Skip(2).ToArray()) : null;

        // As in the pinned plugin manager, a flag may come anywhere after the verb.
        var rest = new List<string>();
        var flags = new Flags();
        for (var i = 1; i < args.Count; i++)
        {
            if (i == at)
                continue;

            var arg = args[i];
            switch (arg)
            {
                case "--force" or "-f":
                    flags.Force = true;
                    break;
                case "--notify" or "-n":
                    flags.Notify = true;
                    break;
                case "--verbose" or "-v":
                    flags.Verbose = true;
                    break;
                case "--no-shallow" or "-s" or "--no-nix":
                    break;
                case "--hl-url":
                    if (++i >= args.Count)
                        return new ManagerCommand.Invalid("Missing argument for --hl-url");

                    break;
                default:
                    if (arg.StartsWith('-'))
                        return new ManagerCommand.Invalid($"Unrecognized option {arg}");

                    rest.Add(arg);
                    break;
            }
        }

        ManagerCommand command;
        switch (verb)
        {
            case "install":
                if (rest.Count is < 1 or > 2)
                    return new ManagerCommand.Invalid("usage: winlandctl plugin install <git-url> [revision]");

                command = new ManagerCommand.Install(rest[0], rest.Count == 2 ? rest[1] : null);
                break;
            case "update":
                if (rest.Count != 0)
                    return new ManagerCommand.Invalid("usage: winlandctl plugin update [--force]");

                command = new ManagerCommand.Update(flags.Force);
                break;
            case "remove":
                if (rest.Count != 1)
                    return new ManagerCommand.Invalid("usage: winlandctl plugin remove <git-url|name|author/name>");

                command = new ManagerCommand.Remove(rest[0]);
                break;
            case "enable" or "disable":
                if (rest.Count != 1)
                    return new ManagerCommand.Invalid($"usage: winlandctl plugin {verb} <name|author/name>");

                command = new ManagerCommand.Enable(rest[0], verb == "enable");
                break;
            default:
                if (rest.Count != 0)
                    return new ManagerCommand.Invalid("usage: winlandctl plugin reload [--force]");

                command = new ManagerCommand.Reload(flags.Force);
                break;
        }

        return command with { Notify = flags.Notify, Verbose = flags.Verbose };
    }

    private sealed class Flags
    {
        public bool Force { get; set; }

        public bool Notify { get; set; }

        public bool Verbose { get; set; }
    }

    private static ManagerCommand? Other(string verb, string[] rest)
    {
        switch (verb)
        {
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
