using System.Globalization;

namespace Winland.Application;

/// <summary>The pipe's requests: queries, <c>keyword</c> and <c>--batch</c>.</summary>
public sealed partial class WindowManager
{
    // The last config Winland read. A live keyword leaves it, so configerrors keeps the errors of the file.
    private ConfigDocument? _loaded;

    // A batch's flags are added to the flags of each of its commands.
    private CommandReply ExecuteLine(string? line, CommandFlags added = CommandFlags.None) =>
        ExecuteLine(CommandProtocol.Parse(line), added);

    private CommandReply ExecuteLine(CommandParse parse, CommandFlags added = CommandFlags.None) =>
        parse switch
        {
            CommandRejected rejected => new CommandReply(rejected.Error, View([])),
            CommandParsed parsed => Reply(parsed.Request with { Flags = parsed.Request.Flags | added }),
            _ => throw new InvalidOperationException("No handler for this command."),
        };

    // A query's reply, or null for a request that is not one. A query answers from the current State and changes nothing.
    private string? Read(CommandRequest request) =>
        request switch
        {
            ClientsRequest clients => Describe(state => Json(clients)
                ? CommandProtocol.FormatClients(state)
                : Text(CommandProtocol.FormatClientsText(state), "no open windows")),
            WorkspacesRequest workspaces => Describe(state => Json(workspaces) ? CommandProtocol.FormatWorkspaces(state) : CommandProtocol.FormatWorkspacesText(state)),
            MonitorsRequest monitors => Describe(state => Json(monitors)
                ? CommandProtocol.FormatMonitors(state, monitors.All)
                : CommandProtocol.FormatMonitorsText(state, monitors.All)),
            ActiveWindowRequest active => Describe(state => Json(active)
                ? CommandProtocol.FormatActiveWindow(state, ActiveWindow())
                : Text(CommandProtocol.FormatActiveWindowText(state, ActiveWindow()), "Invalid")),
            ActiveWorkspaceRequest activeWorkspace => Describe(state => Json(activeWorkspace)
                ? CommandProtocol.FormatActiveWorkspace(state)
                : CommandProtocol.FormatActiveWorkspaceText(state)),
            BindsRequest binds => Json(binds)
                ? CommandProtocol.FormatBinds(_config.Binds.Binds)
                : Text(CommandProtocol.FormatBindsText(_config.Binds.Binds), "no binds"),
            VersionRequest version => Json(version) ? CommandProtocol.FormatVersion() : CommandProtocol.FormatVersionText(),
            ConfigErrorsRequest errors => Json(errors)
                ? CommandProtocol.FormatErrors(_loaded?.Warnings ?? [])
                : Text(CommandProtocol.FormatErrorsText(_loaded?.Warnings ?? [], _loaded?.Path), "no config errors"),
            GetOptionRequest option => OptionTable.Describe(option.Name, _config) ?? "error: no such option",
            GetPropRequest prop => ReadProp(prop),
            _ => null,
        };

    // Each request's reply.
    private CommandReply Reply(CommandRequest request)
    {
        if (Read(request) is { } text)
            return Answer(text);

        return request switch
        {
            DispatchRequest dispatch => DispatchReply(dispatch),
            EventsRequest => Answer("") with { Stream = true },
            ReloadRequest => ReloadCommand(),
            ClickKillRequest => ArmClickKill(),
            KeywordRequest keyword => ApplyKeyword(keyword),
            SetPropRequest prop => WriteProp(prop),
            BatchRequest batch => RunBatch(batch.Payload, batch.Flags),
            _ => throw new InvalidOperationException("No handler for this command."),
        };
    }

    private CommandReply DispatchReply(DispatchRequest dispatch)
    {
        var effect = Execute(dispatch.Name, dispatch.Arguments);
        return new CommandReply(effect.Error ?? "ok", effect);
    }

    // Winland answers in JSON with j/ and in text without it.
    private static bool Json(CommandRequest request) => request.Flags.HasFlag(CommandFlags.Json);

    // Winland's text reply for a query that finds nothing, such as "no open windows". With j/ it stays the empty JSON.
    private static string Text(string reply, string empty) => reply.Length == 0 ? empty : reply;

    private CommandReply Answer(string text) => new(text, View([]));

    private string Describe(Func<State, string> format) => format(Snapshot());

    private CommandReply ApplyKeyword(KeywordRequest request)
    {
        if (request.Name.Length == 0 || request.Value.Length == 0)
            return new CommandReply("error: invalid keyword", View([]));

        if (!ConfigParser.AppliesLive(request.Name))
            return new CommandReply($"error: keyword '{request.Name}' is not applied live", View([]));

        // The line reads the config's variables and curves, and its keys in the layout the config was read in.
        var parsed = ConfigParser.Parse(
            request.Name + " = " + request.Value + "\n",
            layout: KeyLayoutSource?.Invoke(),
            dispatchers: DispatcherNames,
            context: _config.Document);
        if (parsed.Warnings.Count > 0)
            return new CommandReply("error: " + parsed.Warnings[0].Message, View([]));

        if (parsed.Settings.Count == 0 && parsed.Binds.Count == 0 && parsed.Gestures.Count == 0)
            return new CommandReply("error: invalid keyword", View([]));

        foreach (var setting in parsed.Settings)
        {
            if (!ConfigParser.AppliesLive(setting.Keyword))
                return new CommandReply($"error: keyword '{setting.Keyword}' is not applied live", View([]));
        }

        var outcome = Apply((_config.Document ?? ConfigDocument.Empty).Merge(parsed), loaded: false);
        return new CommandReply("ok", outcome);
    }

    private CommandReply RunBatch(string payload, CommandFlags flags)
    {
        var commands = CommandProtocol.SplitBatch(payload);
        if (commands.Count == 0)
            return new CommandReply("", View([]));

        var parts = new string[commands.Count];
        Outcome? effect = null;
        for (var i = 0; i < commands.Count; i++)
        {
            var parsed = CommandProtocol.Parse(commands[i]);
            var reply = parsed is CommandParsed { Request: BatchRequest }
                ? Answer("error: nested batch")
                : ExecuteLine(parsed, flags);
            parts[i] = reply.Text;
            effect = effect is null ? reply.Effect : effect.Then(reply.Effect);
        }

        return new CommandReply(string.Join("\n\n\n", parts), effect ?? View([]));
    }

    private bool TrySelect(string selector, out Client client, out LiveWorkspace? desk)
    {
        client = null!;
        desk = null;
        WindowId? window;
        if (TryAddress(selector, out var addressed))
            window = addressed;
        else
            window = WindowSelector.Find(selector, ActiveWindow(), Snapshot().Clients);

        if (window is not WindowId id)
            return false;

        foreach (var candidate in _workspaces)
        {
            foreach (var item in candidate.Layout.Clients)
            {
                if (item.Window != id)
                    continue;

                client = item;
                desk = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryAddress(string text, out WindowId window)
    {
        window = default;
        var hex = text.Trim();
        const string prefix = "address:";
        if (hex.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            hex = hex[prefix.Length..];

        if (!hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!long.TryParse(hex[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;

        window = new WindowId(value);
        return true;
    }
}
