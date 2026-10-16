namespace Winland;

public sealed partial class Core
{
    private readonly record struct Captured(State State, WindowId? Active);

    private Captured Capture() => new(Snapshot(), ActiveWindow());

    private Outcome WithEvents(Outcome outcome, Captured before, Input? input)
    {
        outcome = FinishCursor(before, outcome, input);
        var events = Diff(before.State, outcome.State, before.Active, ActiveWindow());
        if (_configEvents is not null)
        {
            events.AddRange(_configEvents);
            _configEvents = null;
        }

        if (_extraEvents is { Count: > 0 })
        {
            events.AddRange(_extraEvents);
            _extraEvents = null;
        }

        if (_noted is not null)
        {
            events.AddRange(_noted);
            _noted = null;
        }

        return events.Count == 0 ? outcome : outcome with { Events = events };
    }

    private static List<StreamEvent> Diff(State before, State after, WindowId? activeBefore, WindowId? activeAfter)
    {
        var events = new List<StreamEvent>();
        AddWorkspaceEvents(events, before, after);
        var previous = ByWindow(before);
        var next = ByWindow(after);
        foreach (var pair in previous)
        {
            if (!next.ContainsKey(pair.Key))
                events.Add(new ClientClosed(pair.Key));
        }

        foreach (var pair in next)
        {
            if (!previous.TryGetValue(pair.Key, out var prior))
            {
                events.Add(new ClientOpened(pair.Key, pair.Value.Workspace, pair.Value.Class, pair.Value.Title));
                if (pair.Value.Fullscreen != FullscreenMode.None)
                    events.Add(new FullscreenChanged(true));
                continue;
            }

            if (prior.Workspace != pair.Value.Workspace)
                events.Add(new ClientMoved(pair.Key, pair.Value.Workspace));

            if (prior.Floating != pair.Value.Floating)
                events.Add(new FloatingChanged(pair.Key, pair.Value.Floating));

            if (prior.Minimized != pair.Value.Minimized)
                events.Add(new ClientMinimized(pair.Key, pair.Value.Minimized));

            if (prior.Fullscreen != pair.Value.Fullscreen)
                events.Add(new FullscreenChanged(pair.Value.Fullscreen != FullscreenMode.None));
        }

        if (after.ActiveMonitor != before.ActiveMonitor)
        {
            var name = MonitorName(after, after.ActiveMonitor);
            if (name.Length > 0)
                events.Add(new MonitorFocused(name, WorkspaceOn(after, after.ActiveMonitor)));
        }

        if (after.ActiveWorkspace != before.ActiveWorkspace)
            events.Add(new WorkspaceActivated(after.ActiveWorkspace));

        if (activeAfter != activeBefore)
            events.Add(new FocusChanged(activeAfter));

        AddSpecialEvents(events, before, after);
        AddMonitorEvents(events, before, after);
        return events;
    }

    private static void AddWorkspaceEvents(List<StreamEvent> events, State before, State after)
    {
        var previous = new Dictionary<WorkspaceId, Workspace>(before.Workspaces.Count);
        foreach (var workspace in before.Workspaces)
            previous[workspace.Id] = workspace;

        var next = new HashSet<WorkspaceId>();
        foreach (var workspace in after.Workspaces)
            next.Add(workspace.Id);

        foreach (var workspace in after.Workspaces)
        {
            if (!previous.ContainsKey(workspace.Id))
                events.Add(new WorkspaceCreated(workspace.Id));
        }

        foreach (var workspace in before.Workspaces)
        {
            if (next.Contains(workspace.Id))
                continue;

            events.Add(new WorkspaceDestroyed(workspace.Id, CommandProtocol.WorkspaceNumber(before, workspace.Id)));
        }

        foreach (var workspace in after.Workspaces)
        {
            if (!previous.TryGetValue(workspace.Id, out var prior) || prior.Monitor == workspace.Monitor)
                continue;

            var name = MonitorName(after, workspace.Monitor);
            if (name.Length == 0)
                continue;

            events.Add(new WorkspaceMoved(workspace.Id, name));
        }
    }

    private static string MonitorName(State state, MonitorId id)
    {
        foreach (var screen in state.Screens)
        {
            if (screen.Id == id)
                return screen.Monitor.Name;
        }

        return "";
    }

    private static WorkspaceId WorkspaceOn(State state, MonitorId id)
    {
        foreach (var screen in state.Screens)
        {
            if (screen.Id == id)
                return screen.ActiveWorkspace;
        }

        return state.ActiveWorkspace;
    }

    private static void AddMonitorEvents(List<StreamEvent> events, State before, State after)
    {
        var nextNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var screen in after.Screens)
            nextNames.Add(screen.Monitor.Name);

        foreach (var screen in before.Screens)
        {
            if (nextNames.Contains(screen.Monitor.Name))
                continue;

            events.Add(new MonitorRemoved(screen.Monitor.Name));
            events.Add(new MonitorRemovedV2(screen.Id.Value, screen.Monitor.Name, screen.Monitor.Description));
        }

        var previous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var screen in before.Screens)
            previous.Add(screen.Monitor.Name);

        foreach (var screen in after.Screens)
        {
            if (previous.Contains(screen.Monitor.Name))
                continue;

            events.Add(new MonitorAdded(screen.Monitor.Name));
            events.Add(new MonitorAddedV2(screen.Id.Value, screen.Monitor.Name, screen.Monitor.Description));
        }
    }

    private static Dictionary<WindowId, Client> ByWindow(State state)
    {
        var map = new Dictionary<WindowId, Client>(state.Clients.Count);
        foreach (var client in state.Clients)
            map[client.Window] = client;

        return map;
    }
}
