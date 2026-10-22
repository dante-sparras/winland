using System.Globalization;

namespace Winland.Application;

/// <summary>The pipe's requests: queries, <c>keyword</c> and <c>--batch</c>.</summary>
public sealed partial class WindowManager
{
    private string[] _configErrors = [];

    private CommandReply ExecuteLine(string? line) =>
        CommandProtocol.Parse(line) switch
        {
            CommandRejected rejected => new CommandReply(rejected.Error, View([])),
            CommandParsed parsed => Reply(parsed.Request),
            _ => throw new InvalidOperationException("No handler for this command."),
        };

    // Each request's reply. A query answers with the current State and changes nothing.
    private CommandReply Reply(CommandRequest request)
    {
        var reply = request switch
        {
            DispatchRequest dispatch => DispatchReply(dispatch),
            ClientsRequest clients => Describe(state => Json(clients)
                ? CommandProtocol.FormatClients(state, clients.All)
                : Text(CommandProtocol.FormatClientsText(state, clients.All), "no open windows")),
            WorkspacesRequest workspaces => Describe(state => Json(workspaces) ? CommandProtocol.FormatWorkspaces(state) : CommandProtocol.FormatWorkspacesText(state)),
            WorkspaceRulesRequest => Answer(WorkspaceRules.Format(_config.WorkspaceRules)),
            MonitorsRequest monitors => Describe(state => Json(monitors)
                ? CommandProtocol.FormatMonitors(state, monitors.All)
                : CommandProtocol.FormatMonitorsText(state, monitors.All)),
            ActiveWindowRequest active => Describe(state => Json(active)
                ? CommandProtocol.FormatActiveWindow(state, ActiveWindow())
                : Text(CommandProtocol.FormatActiveWindowText(state, ActiveWindow()), "Invalid")),
            ActiveWorkspaceRequest activeWorkspace => Describe(state => Json(activeWorkspace)
                ? CommandProtocol.FormatActiveWorkspace(state)
                : CommandProtocol.FormatActiveWorkspaceText(state)),
            EventsRequest => Answer("") with { Stream = true },
            ReloadRequest => ReloadCommand(),
            BindsRequest => Describe(_ => CommandProtocol.FormatBinds(_config.Binds.Entries)),
            VersionRequest => Answer(CommandProtocol.FormatVersion()),
            CursorPosRequest cursor => Answer(CommandProtocol.FormatCursor(Cursor?.Read() ?? new Point(0, 0), Json(cursor))),
            ConfigErrorsRequest => Answer(CommandProtocol.FormatErrors(_configErrors)),
            GetOptionRequest option => Answer(OptionTable.Describe(option.Name, _config) ?? "no such option"),
            LockedRequest => Answer(CommandProtocol.FormatLocked(SessionLock?.Locked == true)),
            LayoutsRequest => Answer(CommandProtocol.FormatLayouts()),
            AnimationsRequest animations => Answer(_config.Animations.Describe(Json(animations))),
            SubmapRequest submap => Answer(CommandProtocol.FormatSubmap(_keyState.Submap, Json(submap))),
            ClickKillRequest => ArmClickKill(),
            KeywordRequest keyword => ApplyKeyword(keyword),
            GetPropRequest prop => ReadProp(prop),
            SetPropRequest prop => WriteProp(prop),
            BatchRequest batch => RunBatch(batch.Payload),
            _ => throw new InvalidOperationException("No handler for this command."),
        };

        if (request.Flags.HasFlag(CommandFlags.Relayout))
            reply = reply with { Effect = Reflow(reply.Effect) };

        return reply;
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

    private CommandReply Describe(Func<State, string> format)
    {
        var outcome = View([]);
        return new CommandReply(format(outcome.State), outcome);
    }

    // A keyword that changes the layout lays every Workspace out again. The result follows the keyword's own outcome.
    private Outcome Reflow(Outcome current)
    {
        Outcome laid;
        if (_monitors.Count > 0)
            laid = SetMonitors(Reported());
        else
        {
            IReadOnlyList<Placement> placements = [];
            var active = ActiveId();
            foreach (var desk in _workspaces)
            {
                Stamp(desk);
                var placed = desk.Layout.Apply(desk.Options);
                if (desk.Id == active)
                    placements = placed;
            }

            laid = View(placements);
        }

        return current.Then(laid) with { Consumed = current.Consumed, Dispatcher = current.Dispatcher };
    }

    private void NoteConfigErrors(ConfigDocument? document)
    {
        if (document is null || document.Warnings.Count == 0)
        {
            _configErrors = [];
            return;
        }

        var errors = new string[document.Warnings.Count];
        for (var i = 0; i < document.Warnings.Count; i++)
        {
            var warning = document.Warnings[i];
            errors[i] = warning.Line > 0
                ? string.Create(CultureInfo.InvariantCulture, $"line {warning.Line}: {warning.Message}")
                : warning.Message;
        }

        _configErrors = errors;
    }

    private CommandReply ApplyKeyword(KeywordRequest request)
    {
        if (request.Name.Length == 0 || request.Value.Length == 0)
            return new CommandReply("error: invalid keyword", View([]));

        if (!LiveKeyword(request.Name))
            return new CommandReply($"error: keyword '{request.Name}' is not applied live", View([]));

        var parsed = ConfigParser.Parse(request.Name + " = " + request.Value + "\n");
        if (parsed.Warnings.Count > 0)
            return new CommandReply("error: " + parsed.Warnings[0].Message, View([]));

        if (parsed.Settings.Count == 0 && parsed.Binds.Count == 0 && parsed.Gestures.Count == 0)
            return new CommandReply("error: invalid keyword", View([]));

        foreach (var setting in parsed.Settings)
        {
            if (!LiveKeyword(setting.Keyword))
                return new CommandReply($"error: keyword '{setting.Keyword}' is not applied live", View([]));
        }

        var kept = _configErrors;
        // A live keyword is not a load of the file, so it leaves the notification where the last load put it.
        var outcome = Apply(MergeLive(_config.Document, parsed), loaded: false);
        _configErrors = kept;
        return new CommandReply("ok", outcome);
    }

    // A line that launches a program or loads a file takes effect only when the config is read. Every other line, an
    // option or a rule, applies at once.
    private static bool LiveKeyword(string keyword) =>
        keyword is not ("exec" or "exec-once" or "execr" or "execr-once" or "exec-shutdown" or "source");

    // An option, a keyword with a category such as general:gaps_in, replaces its old value. A keyword that may
    // repeat, such as monitor, workspace or windowrulev2, adds its line after the ones already read.
    private static ConfigDocument MergeLive(ConfigDocument? current, ConfigDocument incoming)
    {
        var settings = new List<Setting>();
        if (current is not null)
        {
            foreach (var setting in current.Settings)
            {
                var replaced = false;
                foreach (var update in incoming.Settings)
                {
                    if (update.Keyword == setting.Keyword && setting.Keyword.Contains(':', StringComparison.Ordinal))
                    {
                        replaced = true;
                        break;
                    }
                }

                if (!replaced)
                    settings.Add(setting);
            }
        }

        foreach (var setting in incoming.Settings)
            settings.Add(setting);

        var binds = new List<BindDirective>();
        if (current is not null)
        {
            foreach (var bind in current.Binds)
                binds.Add(bind);
        }

        foreach (var bind in incoming.Binds)
            binds.Add(bind);

        return new ConfigDocument(settings, binds, [])
        {
            WindowRules = IntegrateWindowRules(current?.WindowRules, incoming.WindowRules),
            WorkspaceRules = MergeWorkspaceRules(current?.WorkspaceRules ?? WorkspaceRule.None, incoming.WorkspaceRules),
            Sources = current?.Sources ?? [],
            Gestures = [.. current?.Gestures ?? [], .. incoming.Gestures],
            Path = current?.Path,
        };
    }

    private static List<WindowRule> IntegrateWindowRules(IReadOnlyList<WindowRule>? current, IReadOnlyList<WindowRule> incoming)
    {
        var rules = current is null ? [] : new List<WindowRule>(current);
        foreach (var rule in incoming)
            rules.Add(rule);

        return rules;
    }

    // A rule for a Workspace that already has one merges into it, as a second line in the config does.
    private static List<WorkspaceRule> MergeWorkspaceRules(IReadOnlyList<WorkspaceRule> current, IReadOnlyList<WorkspaceRule> incoming)
    {
        var rules = new List<WorkspaceRule>(current);
        foreach (var rule in incoming)
            WorkspaceRules.ReplaceOrAdd(rules, rule);

        return rules;
    }

    private CommandReply RunBatch(string payload)
    {
        var commands = CommandProtocol.SplitBatch(payload);
        if (commands.Count == 0)
            return new CommandReply("", View([]));

        var parts = new string[commands.Count];
        Outcome? effect = null;
        for (var i = 0; i < commands.Count; i++)
        {
            var reply = ExecuteLine(commands[i]);
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
