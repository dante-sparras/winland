namespace Winland.Application;

/// <summary>Loading and reloading the config, and the options the input hooks read without the lock.</summary>
public sealed partial class WindowManager
{
    /// <summary>The <c>debug</c> options of the config read last, which say where Winland's log goes.</summary>
    public DebugOptions Debug => _config.Debug;

    // The notification the last load with config errors showed. A clean load clears it. A live keyword leaves it.
    private ConfigNotice? _notice;
    private int _noticeGeneration;

    // A loaded config goes through the same path as a reload, so it applies every part of the config.
    private Outcome Load(ConfigLoaded loaded) => Apply(loaded.Config);

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

    private Outcome NoteAccent(AccentChanged changed)
    {
        _accent = changed.Color;
        _config = _config with { Decoration = _config.Decoration.WithAccent(_accent) };
        return View([]);
    }

    // Puts a config in force. Every option is in the one record, so a reader never sees part of two configs.
    private void Use(AppliedConfig config)
    {
        _config = config;
        ScrollCurve = config.Input.ScrollCurve;
    }

    // loaded is false for a live keyword. That is not a read of the file, so the notification stays.
    private Outcome Apply(ConfigDocument document, bool loaded = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        var tiledBefore = _config.Session.SizeLimitsTiled;
        Use(AppliedConfig.From(document, _accent));
        NoteConfigErrors(document);
        if (loaded)
            ShowConfigErrors(document);
        if (_config.Session.SizeLimitsTiled != tiledBefore)
            RefreshTiledLimits();

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

        // A reload applies the monitor rules again, and Winland lays the monitors out after it.
        events.Add(new MonitorLayoutChanged());
        _configEvents = events;
        return outcome;
    }

    // A config read or reloaded with warnings replaces the notification with the count and the first warning,
    // including a reload of the same warnings. A load with none takes the notification away.
    private void ShowConfigErrors(ConfigDocument document)
    {
        var next = ConfigNotice.For(document.Warnings, document.Path, _noticeGeneration + 1);
        if (next is null)
        {
            _notice = null;
            return;
        }

        _noticeGeneration = next.Generation;
        _notice = next;
    }

    private void MatchLayout(LiveWorkspace desk)
    {
        var created = LayoutFactory.Create(_config.Document, RuleFor(desk)?.Layout);
        if (created.GetType() == desk.Layout.GetType() && LayoutFactory.NameOf(created) == LayoutFactory.NameOf(desk.Layout))
        {
            desk.Layout.Apply(_config.Master);
            desk.Layout.Configure(_config.Scrolling);
            RankFocus(desk.Layout);
            return;
        }

        AdoptLayout(desk, created);
    }

    // Moves the Workspace onto <paramref name="created"/>, keeping each Client's floating state.
    private void AdoptLayout(LiveWorkspace desk, Layout created)
    {
        RankFocus(created);
        if (desk.Layout.Clients.Count == 0)
        {
            desk.Replace(created);
            created.Apply(_config.Master);
            created.Configure(_config.Scrolling);
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
        created.Apply(_config.Master);
        created.Configure(_config.Scrolling);
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

    /// <summary>The button scrolling the mouse hook carries out. It reads it with no lock.</summary>
    public ButtonScrollOptions ButtonScroll => _config.Input.ButtonScroll;

    /// <summary><c>input:rotation</c>, in degrees, which the mouse hook reads with no lock. A touchpad move is not turned.</summary>
    public int PointerRotation => _config.Input.Rotation;

    /// <summary>The <c>input:scroll_points</c> curve the mouse hook carries out, or null. It reads it with no lock.</summary>
    public ScrollCurve? ScrollCurve { get; private set; }

    /// <summary><c>input:emulate_discrete_scroll</c>, which the mouse hook reads with no lock.</summary>
    public int DiscreteScrollMode => _config.Input.EmulateDiscreteScroll;
}
