namespace Winland.Application;

/// <summary>Saves where hidden Clients are, so a restart after a crash finds them again.</summary>
public sealed partial class WindowManager
{
    private readonly Dictionary<WindowId, RecoveredClient> _restored = [];

    // Where each tile a restart found was, until the tiles are arranged as they were.
    private readonly Dictionary<WindowId, Rectangle> _restoredTiles = [];

    // The Workspaces a restart found shown, from the Clients that were not hidden.
    private readonly HashSet<WorkspaceId> _restoredShown = [];

    public RecoverySnapshot Recovery()
    {
        lock (_gate)
            return CaptureRecovery();
    }

    public void Restore(RecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            _restored.Clear();
            _restoredTiles.Clear();
            _restoredShown.Clear();
            foreach (var client in snapshot.Clients)
            {
                ArgumentNullException.ThrowIfNull(client);
                if (client.Window.Value == 0 || client.Workspace.Name.Length == 0)
                    continue;

                _restored[client.Window] = client;
                if (client is { Floating: false, Bounds: Rectangle tile })
                    _restoredTiles[client.Window] = tile;
                if (!client.Hidden && !client.Workspace.IsSpecial)
                    _restoredShown.Add(client.Workspace);
            }
        }
    }

    // Each Monitor shows the Workspace it showed, and each Workspace's tiles go back where they were. A hidden
    // Workspace is placed when it is shown. The focus stays on the Monitor that has it.
    private Outcome ArrangeRestored()
    {
        var placements = new List<Placement>();
        var show = new List<WindowId>();
        var hide = new List<WindowId>();
        var focused = FocusedId();
        foreach (var workspace in _restoredShown)
        {
            if (_workspaces.Find(workspace) is null || _workspaces.Shown.IsShown(workspace))
                continue;

            var switched = SwitchTo(workspace, focus: null, consumed: false, dispatcher: null);
            placements.AddRange(switched.Placements);
            show.AddRange(switched.Show);
            hide.AddRange(switched.Hide);
        }

        Focus(focused);
        _restoredShown.Clear();
        foreach (var desk in _workspaces)
        {
            var arranged = desk.Layout.Arrange(_restoredTiles);
            if (_workspaces.Shown.IsShown(desk.Id))
                placements.AddRange(arranged);
        }

        _restoredTiles.Clear();

        // A tile the switch placed and the arrangement placed again goes where it was arranged.
        var last = new Dictionary<ClientId, Placement>();
        foreach (var placement in placements)
        {
            last.Remove(placement.Client);
            last[placement.Client] = placement;
        }

        return View([.. last.Values], show, [.. hide.Except(show)]);
    }

    private RecoverySnapshot CaptureRecovery()
    {
        var state = Snapshot();
        var monitors = new Dictionary<WorkspaceId, MonitorId>(state.Workspaces.Count);
        foreach (var workspace in state.Workspaces)
            monitors[workspace.Id] = workspace.Monitor;

        var concealed = new HashSet<WindowId>();
        foreach (var hidden in state.Hidden)
            concealed.Add(hidden.Window);

        var clients = new RecoveredClient[state.Clients.Count];
        for (var i = 0; i < state.Clients.Count; i++)
        {
            var client = state.Clients[i];
            var monitor = monitors.TryGetValue(client.Workspace, out var found) ? found : state.ActiveMonitor;
            var identity = "";
            foreach (var screen in _monitors)
            {
                if (screen.Id != monitor)
                    continue;

                identity = ReturnKey(screen.Reported);
                break;
            }

            clients[i] = new RecoveredClient(
                client.Window,
                client.Workspace,
                monitor,
                concealed.Contains(client.Window),
                client.Class,
                client.Title,
                client.ProcessId,
                Floating: client.Floating,
                Bounds: SlotOf(client),
                Pinned: client.Pinned,
                Fullscreen: client.Fullscreen,
                FullscreenClient: client.FullscreenClient,
                PinHeld: client.PinFullscreened)
            {
                MonitorIdentity = identity,
            };
        }

        return new RecoverySnapshot(clients);
    }

    // A covering Client's bounds are the area it covers. Its own slot is what it goes back to.
    private Rectangle SlotOf(Client client) =>
        client.Fullscreen != FullscreenMode.None
            && WorkspaceContaining(client.Window) is LiveWorkspace desk
            && desk.Layout.TryBounds(client.Window, out var slot)
            ? slot
            : client.Bounds;

    // What a snapshot recorded about a Client wins over the window rules, which decided it the first time.
    private static WindowDecision Recovered(WindowDecision decision, RecoveredClient? restored)
    {
        if (restored is null)
            return decision;

        var floating = restored.Floating ?? decision.Floating;
        var fullscreen = decision.FullscreenRequest;
        if (restored.Fullscreen is FullscreenMode internalMode)
        {
            var clientMode = restored.FullscreenClient ?? internalMode;
            fullscreen = internalMode == FullscreenMode.None && clientMode == FullscreenMode.None
                ? null
                : new FullscreenRule(internalMode, clientMode);
        }

        return decision with
        {
            Floating = floating,
            Pin = restored.Pinned ?? decision.Pin,
            FullscreenRequest = fullscreen,
        };
    }

    private RecoveredClient? FindRestored(WindowId window) =>
        _restored.TryGetValue(window, out var client) ? client : null;

    private void ForgetRestored(WindowId window) => _restored.Remove(window);

    private bool RestoredMonitorExists(RecoveredClient? restored)
    {
        if (restored is null)
            return false;

        if (restored.MonitorIdentity.Length > 0)
            return MonitorByIdentity(restored.MonitorIdentity) is not null || _monitors.Count == 0;

        return _monitors.Count == 0 || MonitorById(restored.Monitor) is not null;
    }

    private MonitorId RestoredMonitor(RecoveredClient? restored, LiveMonitor? screen)
    {
        if (restored is { } remembered)
        {
            if (remembered.MonitorIdentity.Length > 0 && MonitorByIdentity(remembered.MonitorIdentity) is { } match)
                return match.Id;

            if (remembered.MonitorIdentity.Length == 0 && (_monitors.Count == 0 || MonitorById(remembered.Monitor) is not null))
                return remembered.Monitor;
        }

        return screen?.Id ?? FocusedId();
    }

    private LiveMonitor? MonitorByIdentity(string identity)
    {
        foreach (var screen in _monitors)
        {
            if (string.Equals(ReturnKey(screen.Reported), identity, StringComparison.OrdinalIgnoreCase))
                return screen;
        }

        return null;
    }
}
