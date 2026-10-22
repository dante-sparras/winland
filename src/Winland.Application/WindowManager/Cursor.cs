namespace Winland.Application;

/// <summary>Where the cursor goes after a focus change or a switch, under the <c>cursor</c> options.</summary>
public sealed partial class WindowManager
{
    private Point? _pointer;
    private readonly Dictionary<WindowId, Point> _cursorOnWindow = [];
    private bool _defaultMonitorDone;

    internal WarpTo? SpecialWarp(Rectangle bounds, Point? relative)
    {
        lock (_gate)
            return CursorWarp.Place(_config.Cursor.WarpOnSpecial, _config.Cursor.NoWarps, _config.Cursor.PersistentWarps, bounds, relative);
    }

    private Outcome FinishCursor(Captured before, Outcome outcome, Input? input)
    {
        if (input is PointerMoved moved)
            _pointer = moved.Position;

        var workspaceChanged = outcome.State.ActiveWorkspace != before.State.ActiveWorkspace;
        var monitorChanged = outcome.State.ActiveMonitor != before.State.ActiveMonitor;
        var specialToggled = SpecialToggled(before.State, outcome.State, outcome.Dispatcher);
        if (workspaceChanged || monitorChanged || specialToggled)
            RememberLeaving(before);

        var warp = TakeRequestedWarp() ?? SwitchWarp(before, outcome);
        // A change that lands on another Monitor follows cursor:warp_on_monitor_change, or warp_on_change_workspace when
        // that is -1, and one on the same Monitor follows warp_on_change_workspace.
        if (warp is null && monitorChanged)
            warp = Plan(outcome.State, _config.Cursor.MonitorMode()) ?? EmptyMonitorMiddle(outcome);
        else if (warp is null && workspaceChanged)
            warp = Plan(outcome.State, _config.Cursor.WarpOnWorkspace);
        if (warp is null && specialToggled)
            warp = PlanFocused(outcome.State, _config.Cursor.WarpOnSpecial);
        if (warp is null && _crossMonitorWarp is WarpTo crossed && !_config.Cursor.NoWarps)
        {
            warp = crossed;
            _pointer = crossed.Position;
        }

        _crossMonitorWarp = null;
        if (warp is null)
            warp = DefaultMonitor(outcome);

        return outcome with
        {
            Warp = warp,
            Cursor = _config.Cursor.Behavior(),
            ResizeCursor = _resizeCursor,
        };
    }

    // movefocus, cyclenext, focusurgentorlast and focuscurrentorlast move the cursor to the middle of the window they
    // focus, or to its last point there with cursor:persistent_warps, as Winland's switch to a window does.
    // Leaving a fullscreen window for another on its Workspace does not, and cursor:no_warps stops it.
    private WarpTo? SwitchWarp(Captured before, Outcome outcome)
    {
        if (!TraitsOf(outcome.Dispatcher).HasFlag(DispatcherTraits.WarpsToFocused)
            || _active is not WindowId window
            || window == before.Active)
            return null;

        Client? target = null;
        Client? left = null;
        foreach (var client in outcome.State.Clients)
        {
            if (client.Window == window)
                target = client;
        }

        foreach (var client in before.State.Clients)
        {
            if (client.Window == before.Active)
                left = client;
        }

        if (target is null || left is { Fullscreen: not FullscreenMode.None, LayoutFullscreen: false } && left.Workspace == target.Workspace)
            return null;

        RememberLeaving(before);
        Point? relative = _cursorOnWindow.Remove(window, out var saved) ? saved : null;
        var warp = CursorWarp.Place(1, _config.Cursor.NoWarps, _config.Cursor.PersistentWarps, target.Bounds, relative);
        if (warp is WarpTo to)
            _pointer = to.Position;

        return warp;
    }

    private void RememberLeaving(Captured before)
    {
        if (_config.Cursor.NoWarps || _pointer is not Point pointer || before.Active is not WindowId window)
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

        var warp = CursorWarp.Place(mode, _config.Cursor.NoWarps, _config.Cursor.PersistentWarps, client.Bounds, relative);
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

        var warp = CursorWarp.Place(mode, _config.Cursor.NoWarps, _config.Cursor.PersistentWarps, client.Bounds, relative);
        if (warp is WarpTo to)
            _pointer = to.Position;
        return warp;
    }

    // focusmonitor, and movefocus onto another Monitor, put the pointer in the middle of a Monitor with nothing to
    // focus, as Winland does, unless cursor:no_warps is set.
    private WarpTo? EmptyMonitorMiddle(Outcome outcome)
    {
        if (_config.Cursor.NoWarps || !TraitsOf(outcome.Dispatcher).HasFlag(DispatcherTraits.WarpsToEmptyMonitor) || FocusedClient(outcome.State) is not null)
            return null;

        foreach (var screen in outcome.State.Screens)
        {
            if (screen.Id != outcome.State.ActiveMonitor || !screen.Monitor.Enabled)
                continue;

            var monitor = screen.Monitor;
            var point = new Point(monitor.X + (monitor.Width / 2), monitor.Y + (monitor.Height / 2));
            _pointer = point;
            return new WarpTo(point);
        }

        return null;
    }

    private WarpTo? DefaultMonitor(Outcome outcome)
    {
        if (_defaultMonitorDone || _config.Cursor.DefaultMonitor.Length == 0)
            return null;

        foreach (var monitor in outcome.State.Monitors)
        {
            if (!monitor.Enabled || !CursorWarp.Names(monitor, _config.Cursor.DefaultMonitor))
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
