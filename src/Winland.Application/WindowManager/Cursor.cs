namespace Winland.Application;

/// <summary>Where the cursor goes after a focus change or a switch, unless <c>cursor:no_warps</c> is set.</summary>
public sealed partial class WindowManager
{
    private Point? _pointer;

    private Outcome FinishCursor(Captured before, Outcome outcome, Input? input)
    {
        if (input is PointerMoved moved)
            _pointer = moved.Position;

        var monitorChanged = outcome.State.ActiveMonitor != before.State.ActiveMonitor;
        var warp = TakeRequestedWarp() ?? SwitchWarp(before, outcome);
        if (warp is null && monitorChanged)
            warp = EmptyMonitorMiddle(outcome);
        if (warp is null && _crossMonitorWarp is WarpTo crossed && !_config.Cursor.NoWarps)
        {
            warp = crossed;
            _pointer = crossed.Position;
        }

        _crossMonitorWarp = null;

        return outcome with
        {
            Warp = warp,
            Cursor = _config.Cursor.Behavior(),
            ResizeCursor = _resizeCursor,
        };
    }

    // movefocus, cyclenext, focusurgentorlast and focuscurrentorlast move the cursor to the middle of the window they
    // focus, as Winland's switch to a window does.
    // Leaving a fullscreen window for another on its Workspace does not, and cursor:no_warps stops it.
    private WarpTo? SwitchWarp(Captured before, Outcome outcome)
    {
        if (!TraitsOf(outcome.Dispatcher).HasFlag(DispatcherTraits.WarpsToFocused)
            || _focus.Focused is not WindowId window
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

        if (_config.Cursor.NoWarps)
            return null;

        var bounds = target.Bounds;
        var point = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        _pointer = point;
        return new WarpTo(point);
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
