namespace Winland;

public sealed partial class Core
{
    private readonly Dictionary<WindowId, RecoveredClient> _restored = [];

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
            foreach (var client in snapshot.Clients)
            {
                ArgumentNullException.ThrowIfNull(client);
                if (client.Window.Value == 0 || client.Workspace.Name.Length == 0)
                    continue;

                _restored[client.Window] = client;
            }
        }
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
            clients[i] = new RecoveredClient(
                client.Window,
                client.Workspace,
                monitor,
                concealed.Contains(client.Window),
                client.Class,
                client.Title,
                client.ProcessId);
        }

        return new RecoverySnapshot(clients);
    }

    private RecoveredClient? FindRestored(WindowId window) =>
        _restored.TryGetValue(window, out var client) ? client : null;

    private void ForgetRestored(WindowId window) => _restored.Remove(window);

    private bool RestoredScreenExists(RecoveredClient? restored) =>
        restored is not null && ScreenById(restored.Monitor) is not null;

    private MonitorId RestoredMonitor(RecoveredClient? restored, Screen? screen)
    {
        if (restored is { } remembered && (_screens.Count == 0 || ScreenById(remembered.Monitor) is not null))
            return remembered.Monitor;

        return screen?.Id ?? FocusedId();
    }
}
