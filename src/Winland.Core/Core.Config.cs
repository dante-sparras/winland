namespace Winland;

public sealed partial class Core
{
    // A loaded config goes through the same path as a reload, so it applies every part of the config.
    private Outcome Load(ConfigLoaded loaded) => Apply(loaded.Config);

    private void InstallBinds(ConfigDocument document) =>
        _binds = BindTable.From(document);

    private CommandReply ReloadCommand()
    {
        if (ReadConfig is null)
            return new CommandReply("error: config is not available", View([]));

        ConfigRead read;
        try
        {
            read = ReadConfig();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            read = new ConfigRead(null, new ConfigWarning(0, exception.Message, null));
        }

        if (read.Document is not ConfigDocument document)
        {
            var warning = read.Failure ?? new ConfigWarning(0, "Could not read config.", null);
            var message = warning.Message.StartsWith("error:", StringComparison.Ordinal)
                ? warning.Message
                : "error: " + warning.Message;
            _configEvents = [new ConfigWarned(warning.Line, warning.Message, warning.Source)];
            return new CommandReply(message, View([]));
        }

        var outcome = Apply(document);
        try
        {
            AfterReload?.Invoke(document);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _ = exception;
        }

        return new CommandReply("ok", outcome);
    }

    private Outcome Apply(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _config = document;
        _workspaceRules = document.WorkspaceRules;
        NoteConfigErrors(document);
        _rules = document.WindowRules;
        _session = SessionOptions.From(document);
        _environment = document.Environment;
        InstallBinds(document);
        _options = LayoutOptions.From(document);
        _scrolling = ScrollingOptions.From(document);
        _master = MasterOptions.From(document);
        _decoration = DecorationOptions.From(document);
        ReadAnimations(document);
        _groupOptions = GroupOptions.From(document);
        ReadCursor(document);
        ReadGestures(document);
        ReadBinds(document);
        var active = FocusedWorkspace();
        var covered = active.Covered();
        foreach (var desk in _workspaces)
            MatchLayout(desk);
        Outcome outcome;
        if (_monitors.Count == 0)
        {
            EnsurePersistent();
            IReadOnlyList<Placement> placements = [];
            foreach (var desk in _workspaces)
            {
                Stamp(desk);
                var placed = desk.Layout.Apply(desk.Options);
                if (desk.Id == ActiveId())
                    placements = placed;
            }

            DropEmpty();
            outcome = Present(active, covered, placements);
        }
        else
        {
            outcome = SetMonitors(Reported());
            DropEmpty();
        }

        var events = new List<StreamEvent>(document.Warnings.Count + 1);
        foreach (var warning in document.Warnings)
            events.Add(new ConfigWarned(warning.Line, warning.Message, warning.Source));

        events.Add(new ConfigReloaded());
        _configEvents = events;
        return outcome;
    }

    private void MatchLayout(LiveWorkspace desk)
    {
        var created = LayoutFactory.Create(_config);
        if (created.GetType() == desk.Layout.GetType())
        {
            desk.Layout.Apply(_master);
            desk.Layout.Configure(_scrolling);
            return;
        }

        if (desk.Layout.Clients.Count == 0)
        {
            desk.Replace(created);
            created.Apply(_master);
            created.Configure(_scrolling);
            return;
        }

        var held = new List<(WindowId Window, Taken Taken)>();
        foreach (var client in desk.Layout.Clients.ToArray())
        {
            if (desk.Take(client.Window, out var taken))
                held.Add((client.Window, taken));
        }

        var focus = desk.Focus;
        desk.Replace(created);
        created.Apply(_master);
        created.Configure(_scrolling);
        Stamp(desk);
        var screen = MonitorById(desk.Monitor);
        var area = WorkArea(desk, _area);
        created.Apply(desk.Options);
        if (area.Width > 0 && area.Height > 0)
            created.Fit(area);

        var monitor = screen is { Effective.Enabled: true } ? screen.Bounds : default;
        foreach (var (window, taken) in held)
            desk.Receive(window, taken, area, monitor);

        if (focus is WindowId focused)
            created.NoteFocus(focused);
    }
}
