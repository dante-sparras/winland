using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winland.Core;

public static partial class CommandProtocol
{
    public const string PipeName = "winland";

    public static CommandParse Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return new CommandRejected("error: unknown request");

        var text = line.Trim();
        var flags = CommandFlagReader.Strip(ref text);
        if (text.Length == 0)
            return new CommandRejected("error: unknown request");

        if (ExtraCommands.TryParse(text, flags, out var extra))
            return extra;

        var verbEnd = IndexOfWhitespace(text);
        var verb = verbEnd < 0 ? text : text[..verbEnd];
        var rest = verbEnd < 0 ? "" : text[(verbEnd + 1)..].TrimStart();

        if (verb.Equals("dispatch", StringComparison.OrdinalIgnoreCase))
        {
            if (rest.Length == 0)
                return new CommandRejected("error: missing dispatcher");

            var nameEnd = IndexOfWhitespace(rest);
            var name = nameEnd < 0 ? rest : rest[..nameEnd];
            var arguments = nameEnd < 0 ? "" : rest[(nameEnd + 1)..].TrimStart();
            return Attach(new DispatchRequest(name, arguments), flags);
        }

        if (verb.Equals("monitors", StringComparison.OrdinalIgnoreCase))
        {
            if (rest.Length == 0)
                return Attach(new MonitorsRequest(), flags);

            if (rest.Equals("all", StringComparison.OrdinalIgnoreCase))
                return Attach(new MonitorsRequest(All: true), flags);

            return new CommandRejected("error: unknown request");
        }

        if (rest.Length > 0)
            return new CommandRejected("error: unknown request");

        if (verb.Equals("clients", StringComparison.OrdinalIgnoreCase))
            return Attach(new ClientsRequest(flags.HasFlag(CommandFlags.All)), flags);

        if (verb.Equals("workspaces", StringComparison.OrdinalIgnoreCase))
            return Attach(new WorkspacesRequest(), flags);

        if (verb.Equals("workspacerules", StringComparison.OrdinalIgnoreCase))
            return Attach(new WorkspaceRulesRequest(), flags);

        if (verb.Equals("activeworkspace", StringComparison.OrdinalIgnoreCase))
            return Attach(new ActiveWorkspaceRequest(), flags);

        if (verb.Equals("activewindow", StringComparison.OrdinalIgnoreCase))
            return Attach(new ActiveWindowRequest(), flags);

        if (verb.Equals("events", StringComparison.OrdinalIgnoreCase))
            return Attach(new EventsRequest(), flags);

        if (verb.Equals("reload", StringComparison.OrdinalIgnoreCase))
            return Attach(new ReloadRequest(), flags);

        return new CommandRejected("error: unknown request");
    }

    private static CommandParse Attach(CommandRequest request, CommandFlags flags) =>
        new CommandParsed(request with { Flags = flags });

    private static readonly string[] NoStrings = [];

    private static readonly WorkspaceJson NoSpecial = new("", null, "", "", "");
}

public abstract record CommandParse;

public sealed record CommandParsed(CommandRequest Request) : CommandParse;

public sealed record CommandRejected(string Error) : CommandParse;

public abstract record CommandRequest
{
    public CommandFlags Flags { get; init; }
}

public sealed record DispatchRequest(string Name, string Arguments) : CommandRequest;

public sealed record ClientsRequest(bool All = false) : CommandRequest;

public sealed record WorkspacesRequest : CommandRequest;

public sealed record WorkspaceRulesRequest : CommandRequest;

public sealed record MonitorsRequest(bool All = false) : CommandRequest;

public sealed record ActiveWorkspaceRequest : CommandRequest;

public sealed record ActiveWindowRequest : CommandRequest;

public sealed record EventsRequest : CommandRequest;

public sealed record ReloadRequest : CommandRequest;

public readonly record struct CommandReply(string Text, Outcome Effect)
{
    public bool Stream { get; init; }
}
