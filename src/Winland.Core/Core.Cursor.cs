namespace Winland;

public sealed partial class Core
{
    private CursorOptions _cursor = CursorOptions.Documented;
    private volatile InputOptions _input = InputOptions.Documented;
    private Point? _pointer;
    private readonly Dictionary<WindowId, Point> _cursorOnWindow = [];
    private bool _defaultMonitorDone;

    internal WarpTo? SpecialWarp(Rectangle bounds, Point? relative)
    {
        lock (_gate)
            return CursorWarp.Place(_cursor.WarpOnSpecial, _cursor.NoWarps, _cursor.PersistentWarps, bounds, relative);
    }

    private void ReadCursor(ConfigDocument document)
    {
        _cursor = CursorOptions.From(document);
        _input = InputOptions.From(document);
    }

    private Outcome FinishCursor(Captured before, Outcome outcome, Input? input)
    {
        if (input is PointerMoved moved)
            _pointer = moved.Position;

        var workspaceChanged = outcome.State.ActiveWorkspace != before.State.ActiveWorkspace;
        var monitorChanged = outcome.State.ActiveMonitor != before.State.ActiveMonitor;
        if (workspaceChanged || monitorChanged)
            RememberLeaving(before);

        WarpTo? warp = null;
        if (workspaceChanged)
            warp = Plan(outcome.State, _cursor.WarpOnWorkspace);
        if (warp is null && monitorChanged)
            warp = Plan(outcome.State, _cursor.MonitorMode());
        if (warp is null && SpecialToggled(before.State, outcome.State, outcome.Dispatcher))
            warp = PlanFocused(outcome.State, _cursor.WarpOnSpecial);
        if (warp is null)
            warp = DefaultMonitor(outcome);

        return outcome with
        {
            Warp = warp,
            InputSettings = _input.Request(),
            Cursor = _cursor.Behavior(),
            ResizeCursor = _resizeCursor,
        };
    }

    private void RememberLeaving(Captured before)
    {
        if (_cursor.NoWarps || _pointer is not Point pointer || before.Active is not WindowId window)
            return;

        Client? client = null;
        foreach (var candidate in before.State.Clients)
        {
            if (candidate.Window != window)
                continue;

            client = candidate;
            break;
        }

        if (client is null)
            return;

        _cursorOnWindow[window] = new Point(pointer.X - client.Bounds.X, pointer.Y - client.Bounds.Y);
    }

    private static bool SpecialToggled(State before, State after, string? dispatcher)
    {
        if (dispatcher is null || !dispatcher.Equals("togglespecialworkspace", StringComparison.OrdinalIgnoreCase))
            return false;

        if (before.Screens.Count == 0 && after.Screens.Count == 0)
            return before.SpecialWorkspace != after.SpecialWorkspace;

        if (before.Screens.Count != after.Screens.Count)
            return before.SpecialWorkspace != after.SpecialWorkspace;

        for (var i = 0; i < before.Screens.Count; i++)
        {
            if (before.Screens[i].SpecialWorkspace != after.Screens[i].SpecialWorkspace)
                return true;
        }

        return false;
    }

    private WarpTo? PlanFocused(State state, int mode)
    {
        if (mode <= 0 || _active is not WindowId window)
            return null;

        Client? client = null;
        foreach (var candidate in state.Clients)
        {
            if (candidate.Window != window)
                continue;

            client = candidate;
            break;
        }

        if (client is null)
            return null;

        Point? relative = null;
        if (_cursorOnWindow.Remove(window, out var saved))
            relative = saved;

        var warp = CursorWarp.Place(mode, _cursor.NoWarps, _cursor.PersistentWarps, client.Bounds, relative);
        if (warp is WarpTo to)
            _pointer = to.Position;
        return warp;
    }

    private WarpTo? Plan(State state, int mode)
    {
        if (mode <= 0)
            return null;

        var client = FocusedClient(state);
        if (client is null)
            return null;

        Point? relative = null;
        if (_cursorOnWindow.Remove(client.Window, out var saved))
            relative = saved;

        var warp = CursorWarp.Place(mode, _cursor.NoWarps, _cursor.PersistentWarps, client.Bounds, relative);
        if (warp is WarpTo to)
            _pointer = to.Position;
        return warp;
    }

    private WarpTo? DefaultMonitor(Outcome outcome)
    {
        if (_defaultMonitorDone || _cursor.DefaultMonitor.Length == 0)
            return null;

        foreach (var monitor in outcome.State.Monitors)
        {
            if (!monitor.Enabled || !CursorWarp.Names(monitor, _cursor.DefaultMonitor))
                continue;

            _defaultMonitorDone = true;
            var point = new Point(monitor.X + (monitor.Width / 2), monitor.Y + (monitor.Height / 2));
            _pointer = point;
            return new WarpTo(point);
        }

        return null;
    }

    private static Client? FocusedClient(State state)
    {
        WindowId? last = null;
        foreach (var workspace in state.Workspaces)
        {
            if (workspace.Id != state.ActiveWorkspace)
                continue;

            last = workspace.LastWindow;
            break;
        }

        if (last is not WindowId window)
            return null;

        foreach (var client in state.Clients)
        {
            if (client.Window == window && client.Workspace == state.ActiveWorkspace)
                return client;
        }

        return null;
    }
}
