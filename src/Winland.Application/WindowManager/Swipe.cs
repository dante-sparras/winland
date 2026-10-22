namespace Winland.Application;

/// <summary>The workspace swipe: a touchpad gesture that slides between Workspaces.</summary>
public sealed partial class WindowManager
{
    private readonly SwipeRecognizer _swipeGesture = new();
    private WorkspaceSwipe? _workspaceSwipe;

    private Outcome BeginSwipe()
    {
        _swipeGesture.Begin();
        return View([]);
    }

    // The update that picks a workspace gesture starts the workspace swipe and is its first step.
    private Outcome UpdateSwipe(SwipeUpdated updated)
    {
        var step = _swipeGesture.Update(updated, _config.Gestures, _keyState.Held);
        if (step.Picked)
            StartWorkspaceSwipe();

        if (step.Along is not double along || _swipeGesture.Active is not { IsWorkspace: true } || _workspaceSwipe is not { } swipe)
            return View([]);

        return StepWorkspaceSwipe(swipe, swipe.Delta + (_config.Swipe.Invert ? -along : along));
    }

    private Outcome EndSwipe() =>
        _swipeGesture.End() is { IsWorkspace: true } && _workspaceSwipe is { } swipe ? FinishWorkspaceSwipe(swipe) : View([]);

    private void StartWorkspaceSwipe()
    {
        if (SessionLock?.Locked == true || _keyState.Inhibited || _workspaceSwipe is not null)
            return;

        var monitor = FocusedId();
        var onMonitor = 0;
        foreach (var desk in _workspaces)
        {
            if (desk.Monitor == monitor && !desk.Id.IsSpecial)
                onMonitor++;
        }

        // One Workspace on the Monitor has nowhere to go unless the swipe may create one.
        if (onMonitor < 2 && !_config.Swipe.CreateNew)
            return;

        BeginWorkspaceSwipe(monitor);
    }

    private void BeginWorkspaceSwipe(MonitorId monitor) =>
        _workspaceSwipe = new WorkspaceSwipe(ActiveOn(monitor), monitor);

    private Outcome StepWorkspaceSwipe(WorkspaceSwipe swipe, double delta)
    {
        var distance = Math.Max(1, _config.Swipe.Distance);
        var step = swipe.Delta - delta;
        swipe.Delta = delta;
        swipe.AverageSpeed = ((swipe.AverageSpeed * swipe.SpeedPoints) + Math.Abs(step)) / (swipe.SpeedPoints + 1);
        swipe.SpeedPoints++;

        var (left, right) = Neighbours(swipe.Monitor);
        var begin = swipe.Begin;
        if ((left is null || right is null || left == begin) && !_config.Swipe.CreateNew)
        {
            _workspaceSwipe = null;
            return ReturnSwipe(swipe);
        }

        swipe.Delta = Math.Clamp(swipe.Delta, -distance, distance);
        var beginRank = SwipeRank(begin);
        var emptyBegin = _workspaces.Find(begin) is not { } beginDesk || beginDesk.Layout.Clients.Count == 0;
        if ((left == begin && _config.Swipe.CreateNew && swipe.Delta < 0)
            || (swipe.Delta > 0 && emptyBegin && SwipeRank(right) <= beginRank)
            || (swipe.Delta < 0 && beginRank <= SwipeRank(left)))
        {
            swipe.Delta = 0;
            return PreviewSwipe(swipe, null);
        }

        if (_config.Swipe.DirectionLock)
        {
            var sign = swipe.Delta < 0 ? -1 : 1;
            if (swipe.LockedSign != 0 && swipe.LockedSign != sign)
                swipe.Delta = 0;
            else if (swipe.LockedSign == 0 && Math.Abs(swipe.Delta) > _config.Swipe.DirectionLockThreshold)
                swipe.LockedSign = sign;
        }

        var toward = swipe.Delta < 0 ? left : right;
        var away = swipe.Delta < 0 ? SwipeRank(left) > beginRank : SwipeRank(right) < beginRank;
        if (away || toward is not WorkspaceId target || _workspaces.Find(target) is null)
        {
            // Past the last Workspace the swipe only continues when it may create one.
            if (!_config.Swipe.CreateNew)
                swipe.Delta = 0;
            return PreviewSwipe(swipe, null);
        }

        if (!_config.Swipe.Forever || Math.Abs(swipe.Delta) < distance)
            return PreviewSwipe(swipe, target);

        var switched = FinishWorkspaceSwipe(swipe);
        BeginWorkspaceSwipe(swipe.Monitor);
        return switched;
    }

    private Outcome FinishWorkspaceSwipe(WorkspaceSwipe swipe)
    {
        var distance = Math.Max(1, _config.Swipe.Distance);
        var (left, right) = Neighbours(swipe.Monitor);
        var begin = swipe.Begin;

        // Swiping off the right end with create_new picks the id of a new Workspace now.
        if (SwipeRank(right) <= SwipeRank(begin) && _config.Swipe.CreateNew)
            right = Resolve("r+1", swipe.Monitor);

        _workspaceSwipe = null;

        var magnitude = Math.Abs(swipe.Delta);
        var slow = _config.Swipe.MinSpeedToForce == 0 || swipe.AverageSpeed < _config.Swipe.MinSpeedToForce;
        if ((magnitude < distance * _config.Swipe.CancelRatio && slow) || magnitude < 2)
            return ReturnSwipe(swipe);

        if ((swipe.Delta < 0 ? left : right) is not WorkspaceId target)
            return ReturnSwipe(swipe);

        if (FocusedId() != swipe.Monitor && MonitorById(swipe.Monitor) is not null)
            Focus(swipe.Monitor);

        LandSwipe();
        return SwitchTo(target, focus: null, consumed: false, dispatcher: "workspace");
    }

    private (WorkspaceId? Left, WorkspaceId? Right) Neighbours(MonitorId monitor)
    {
        var prefix = _config.Swipe.UseRelative ? "r" : "m";
        return (Resolve(prefix + "-1", monitor), Resolve(prefix + "+1", monitor));
    }

    private WorkspaceId? Resolve(string address, MonitorId monitor) =>
        WorkspaceAddress.TryResolve(address, Context(monitor), out var id) ? id : null;

    // Workspace ids order as numbers. A Workspace that is not numbered sorts below every number, and an
    // unresolved one is -1, the invalid id the comparisons were written against.
    private long SwipeRank(WorkspaceId? id)
    {
        if (id is not WorkspaceId workspace)
            return -1;

        if (workspace.TryGetNumber(out var number))
            return number;

        var listed = _workspaces.Listed(null);
        var named = 0;
        foreach (var each in listed)
        {
            if (!each.TryGetNumber(out _))
                named++;
        }

        var index = -1;
        for (var i = 0; i < listed.Count; i++)
        {
            if (listed[i] == workspace)
            {
                index = i;
                break;
            }
        }

        return index < 0 ? -1 : -2L - named + index;
    }

    private sealed class WorkspaceSwipe(WorkspaceId begin, MonitorId monitor)
    {
        public WorkspaceId Begin { get; } = begin;

        public MonitorId Monitor { get; } = monitor;

        public double Delta { get; set; }

        public double AverageSpeed { get; set; }

        public int SpeedPoints { get; set; }

        /// <summary>With <c>gestures:workspace_swipe_direction_lock</c>, the sign of the way the swipe first went far enough.</summary>
        public int LockedSign { get; set; }
    }
}
