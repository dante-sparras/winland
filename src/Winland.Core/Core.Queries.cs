using System.Globalization;

namespace Winland;

public sealed partial class Core
{
    private string[] _configErrors = [];

    private CommandReply ExecuteLine(string? line) =>
        CommandProtocol.Parse(line) switch
        {
            CommandRejected rejected => new CommandReply(rejected.Error, View([])),
            CommandParsed parsed => Reply(parsed.Request),
            _ => throw new InvalidOperationException("No handler for this command."),
        };

    private CommandReply Reply(CommandRequest request)
    {
        var reply = request switch
        {
            DispatchRequest dispatch => DispatchReply(dispatch),
            ClientsRequest clients => Describe(state => CommandProtocol.FormatClients(state, clients.All)),
            WorkspacesRequest => Describe(CommandProtocol.FormatWorkspaces),
            WorkspaceRulesRequest => new CommandReply(WorkspaceRules.Format(_workspaceRules), View([])),
            MonitorsRequest monitors => Describe(state => CommandProtocol.FormatMonitors(state, monitors.All)),
            ActiveWindowRequest => Describe(state => CommandProtocol.FormatActiveWindow(state, ActiveWindow())),
            ActiveWorkspaceRequest => Describe(CommandProtocol.FormatActiveWorkspace),
            EventsRequest => new CommandReply("", View([])) { Stream = true },
            ReloadRequest => ReloadCommand(),
            PluginRequest plugin => PluginCommand(plugin),
            _ => ReplyExtra(request),
        };

        if (request.Flags.HasFlag(CommandFlags.Relayout))
            reply = reply with { Effect = Reflow(reply.Effect) };

        return reply;
    }

    private CommandReply DispatchReply(DispatchRequest dispatch)
    {
        var effect = Execute(dispatch.Name, dispatch.Arguments, dispatch.AllowsElevation);
        return new CommandReply(effect.Error ?? effect.Notice ?? "ok", effect);
    }

    private CommandReply Describe(Func<State, string> format)
    {
        var outcome = View([]);
        return new CommandReply(format(outcome.State), outcome);
    }

    private CommandReply ReplyExtra(CommandRequest request) => request switch
    {
        BindsRequest => Describe(_ => ExtraCommands.FormatBinds(_binds.Entries)),
        VersionRequest => new CommandReply(ExtraCommands.FormatVersion(), View([])),
        CursorPosRequest => new CommandReply(ExtraCommands.FormatCursor(Cursor?.Read() ?? new Point(0, 0)), View([])),
        ConfigErrorsRequest => new CommandReply(ExtraCommands.FormatErrors(_configErrors), View([])),
        GetOptionRequest option => new CommandReply(OptionCatalog.Describe(option.Name, _config), View([])),
        SystemInfoRequest => Describe(ExtraCommands.FormatSystem),
        LockedRequest => new CommandReply(ExtraCommands.FormatLocked(SessionLock?.Locked == true), View([])),
        LayoutsRequest => new CommandReply(ExtraCommands.FormatLayouts(), View([])),
        ClickKillRequest => new CommandReply("error: click to kill is not supported", View([])),
        RollingLogRequest => new CommandReply("error: rolling log is not supported", View([])),
        NotifyRequest or DismissNotifyRequest => new CommandReply("error: notifications are not supported", View([])),
        KeywordRequest keyword => ApplyKeyword(keyword),
        GetPropRequest prop => ReadProp(prop),
        SetPropRequest prop => WriteProp(prop),
        BatchRequest batch => RunBatch(batch.Payload),
        _ => throw new InvalidOperationException("No handler for this command."),
    };

    private Outcome Reflow(Outcome current)
    {
        if (_monitors.Count > 0)
        {
            var laid = SetMonitors(Reported());
            return laid with
            {
                Closes = [.. current.Closes, .. laid.Closes],
                Focus = current.Focus ?? laid.Focus,
                Launches = [.. current.Launches, .. laid.Launches],
                Dispatcher = current.Dispatcher,
                AllowsElevation = current.AllowsElevation,
                Consumed = current.Consumed,
                Kills = [.. current.Kills, .. laid.Kills],
                Exit = current.Exit || laid.Exit,
                DisplayPower = [.. current.DisplayPower, .. laid.DisplayPower],
                Events = CombineEvents(current.Events, laid.Events),
                Relays = current.Relays,
                ZOrder = current.ZOrder,
            };
        }

        IReadOnlyList<Placement> placements = [];
        var active = ActiveId();
        foreach (var desk in _workspaces)
        {
            Stamp(desk);
            var placed = desk.Layout.Apply(desk.Options);
            if (desk.Id == active)
                placements = placed;
        }

        var shown = View(
            placements,
            current.Show,
            current.Hide,
            current.Consumed,
            current.Dispatcher,
            current.Close,
            current.Focus,
            current.AllowsElevation);
        return shown with
        {
            Closes = current.Closes,
            Launches = [.. current.Launches, .. shown.Launches],
            Kills = current.Kills,
            Exit = current.Exit,
            DisplayPower = current.DisplayPower,
            Events = current.Events,
            Relays = current.Relays,
            ZOrder = current.ZOrder,
        };
    }

    private static IReadOnlyList<StreamEvent> CombineEvents(
        IReadOnlyList<StreamEvent> first,
        IReadOnlyList<StreamEvent> second)
    {
        if (first.Count == 0)
            return second;
        if (second.Count == 0)
            return first;
        return [.. first, .. second];
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

        if (parsed.Settings.Count == 0 && parsed.Binds.Count == 0)
            return new CommandReply("error: invalid keyword", View([]));

        foreach (var setting in parsed.Settings)
        {
            if (!LiveKeyword(setting.Keyword))
                return new CommandReply($"error: keyword '{setting.Keyword}' is not applied live", View([]));
        }

        // The pipe never launches elevated, so it may not add a bind that would.
        foreach (var directive in parsed.Binds)
        {
            if (directive is Bind bind && LaunchParser.IsExec(bind.Dispatcher) && ConfigElevation.Elevated(bind.Arguments))
                return new CommandReply("error: a bind added over the pipe cannot launch elevated", View([]));
        }

        var kept = _configErrors;
        var outcome = Apply(MergeLive(_config, parsed));
        _configErrors = kept;
        return new CommandReply("ok", outcome);
    }

    // A line that launches a program, loads code or a file, or sets the environment takes effect only when the
    // config is read. Every other line, an option or a rule, applies at once, as in the pinned release.
    private static bool LiveKeyword(string keyword) =>
        keyword is not ("exec" or "exec-once" or "execr" or "execr-once" or "exec-shutdown" or "env" or "source" or "plugin");

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
            WindowRules = [.. current?.WindowRules ?? [], .. incoming.WindowRules],
            WorkspaceRules = MergeWorkspaceRules(current?.WorkspaceRules ?? WorkspaceRule.None, incoming.WorkspaceRules),
            Sources = current?.Sources ?? [],
            Environment = current?.Environment ?? EnvironmentAssignment.None,
            Gestures = [.. current?.Gestures ?? [], .. incoming.Gestures],
            ElevationTrusted = current?.ElevationTrusted ?? false,
        };
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
        var commands = ExtraCommands.SplitBatch(payload);
        if (commands.Count == 0)
            return new CommandReply("", View([]));

        var parts = new string[commands.Count];
        Outcome? effect = null;
        for (var i = 0; i < commands.Count; i++)
        {
            var reply = ExecuteLine(commands[i]);
            parts[i] = reply.Text;
            effect = effect is null ? reply.Effect : Merge(effect, reply.Effect);
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
