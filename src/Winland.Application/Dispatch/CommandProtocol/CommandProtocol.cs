namespace Winland.Application;

/// <summary>The pipe's requests: every verb, how its line reads, and the request it makes.</summary>
public static partial class CommandProtocol
{
    public const string PipeName = "winland";

    private const string Unknown = "error: unknown request";

    // Every verb the pipe answers. Each reads the rest of the line after the verb and returns its request, or null
    // when the rest does not fit.
    private static readonly Dictionary<string, Func<string, CommandFlags, CommandRequest?>> Verbs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["dispatch"] = (rest, _) => Dispatch(rest),
            ["monitors"] = (rest, _) => rest.Length == 0 ? new MonitorsRequest()
                : rest.Equals("all", StringComparison.OrdinalIgnoreCase) ? new MonitorsRequest(All: true)
                : null,
            ["clients"] = (rest, flags) => Bare(rest, new ClientsRequest(flags.HasFlag(CommandFlags.All))),
            ["workspaces"] = (rest, _) => Bare(rest, new WorkspacesRequest()),
            ["workspacerules"] = (rest, _) => Bare(rest, new WorkspaceRulesRequest()),
            ["activeworkspace"] = (rest, _) => Bare(rest, new ActiveWorkspaceRequest()),
            ["activewindow"] = (rest, _) => Bare(rest, new ActiveWindowRequest()),
            ["events"] = (rest, _) => Bare(rest, new EventsRequest()),
            ["reload"] = (rest, _) => Bare(rest, new ReloadRequest()),
            ["binds"] = (rest, _) => Bare(rest, new BindsRequest()),
            ["version"] = (rest, _) => Bare(rest, new VersionRequest()),
            ["cursorpos"] = (rest, _) => Bare(rest, new CursorPosRequest()),
            ["configerrors"] = (rest, _) => Bare(rest, new ConfigErrorsRequest()),
            ["locked"] = (rest, _) => Bare(rest, new LockedRequest()),
            ["layouts"] = (rest, _) => Bare(rest, new LayoutsRequest()),
            ["submap"] = (rest, _) => Bare(rest, new SubmapRequest()),
            ["animations"] = (rest, _) => Bare(rest, new AnimationsRequest()),
            ["kill"] = (rest, _) => Bare(rest, new ClickKillRequest()),
            ["getoption"] = (rest, _) => new GetOptionRequest(SplitOnce(rest).First),
            ["keyword"] = (rest, _) =>
            {
                var (name, value) = SplitOnce(rest);
                return new KeywordRequest(name, value);
            },
            ["getprop"] = (rest, _) =>
            {
                var (selector, property, _) = SplitProp(rest);
                return new GetPropRequest(selector, property);
            },
            ["setprop"] = (rest, _) =>
            {
                var (selector, property, value) = SplitProp(rest);
                return new SetPropRequest(selector, property, value);
            },
        };

    public static CommandParse Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return new CommandRejected(Unknown);

        var text = line.Trim();
        var flags = CommandFlagReader.Strip(ref text);
        if (text.Length == 0)
            return new CommandRejected(Unknown);

        if (text.StartsWith("[[BATCH]]", StringComparison.Ordinal))
            return new CommandParsed(new BatchRequest(text["[[BATCH]]".Length..]) { Flags = flags });

        var (verb, rest) = SplitOnce(text);
        if (!Verbs.TryGetValue(verb, out var read))
            return new CommandRejected(Unknown);

        return read(rest, flags) is CommandRequest request
            ? new CommandParsed(request with { Flags = flags })
            : new CommandRejected(rest.Length == 0 && verb.Equals("dispatch", StringComparison.OrdinalIgnoreCase)
                ? "error: missing dispatcher"
                : Unknown);
    }

    private static DispatchRequest? Dispatch(string rest)
    {
        if (rest.Length == 0)
            return null;

        var (name, arguments) = SplitOnce(rest);
        return new DispatchRequest(name, arguments);
    }

    // A verb that takes nothing after it.
    private static CommandRequest? Bare(string rest, CommandRequest request) => rest.Length == 0 ? request : null;

    // The first word, and the rest with its leading space trimmed.
    private static (string First, string After) SplitOnce(string text)
    {
        var end = IndexOfWhitespace(text);
        return end < 0 ? (text, "") : (text[..end], text[(end + 1)..].TrimStart());
    }

    // getprop and setprop: a selector, a property, and for setprop the value after them.
    private static (string Selector, string Property, string Value) SplitProp(string rest)
    {
        var (selector, tail) = SplitOnce(rest);
        var (property, value) = SplitOnce(tail);
        return (selector, property, value);
    }

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

public sealed record BindsRequest : CommandRequest;

public sealed record VersionRequest : CommandRequest;

public sealed record CursorPosRequest : CommandRequest;

public sealed record ConfigErrorsRequest : CommandRequest;

public sealed record GetOptionRequest(string Name) : CommandRequest;

public sealed record LockedRequest : CommandRequest;

public sealed record LayoutsRequest : CommandRequest;

public sealed record SubmapRequest : CommandRequest;

public sealed record AnimationsRequest : CommandRequest;

public sealed record ClickKillRequest : CommandRequest;

public sealed record KeywordRequest(string Name, string Value) : CommandRequest;

public sealed record GetPropRequest(string Selector, string Property) : CommandRequest;

public sealed record SetPropRequest(string Selector, string Property, string Value) : CommandRequest;

public sealed record BatchRequest(string Payload) : CommandRequest;

public readonly record struct CommandReply(string Text, Outcome Effect)
{
    public bool Stream { get; init; }
}
