namespace Winland.Application;

public sealed partial class WindowManager
{
    // A swipe picks its gesture once it has moved this far on one axis.
    private const double SwipePickDistance = 5;

    private IReadOnlyList<TrackpadGesture> _gestures = [];
    private GestureOptions _swipe = GestureOptions.Documented;
    private TrackpadGesture? _activeGesture;
    private GestureDirection _gestureDirection;
    private bool _gestureFindFailed;
    private double _swipeTotalX;
    private double _swipeTotalY;
    private WorkspaceSwipe? _workspaceSwipe;
    private int _swipeInitialDirection;

    private void ReadGestures(ConfigDocument document)
    {
        _gestures = document.Gestures;
        _swipe = GestureOptions.From(document);
    }

    private Outcome BeginSwipe()
    {
        if (_activeGesture is not null)
            return View([]);

        _gestureFindFailed = false;
        _swipeTotalX = 0;
        _swipeTotalY = 0;
        return View([]);
    }

    private Outcome UpdateSwipe(SwipeUpdated updated)
    {
        if (_gestureFindFailed)
            return View([]);

        _swipeTotalX += updated.DeltaX;
        _swipeTotalY += updated.DeltaY;
        if (_activeGesture is null && Math.Abs(_swipeTotalX) < SwipePickDistance && Math.Abs(_swipeTotalY) < SwipePickDistance)
            return View([]);

        if (_activeGesture is null && !PickGesture(updated.Fingers))
        {
            _gestureFindFailed = true;
            return View([]);
        }

        return UpdateActive(updated);
    }

    // One step of the workspace swipe a swipe picked. The update that picked it is its first step.
    private Outcome UpdateActive(SwipeUpdated updated)
    {
        if (_activeGesture is not { IsWorkspace: true } gesture || _workspaceSwipe is not { } swipe)
            return View([]);

        var along = gesture.Scale * TrackpadGesture.Along(_gestureDirection, updated.DeltaX, updated.DeltaY);
        return StepWorkspaceSwipe(swipe, swipe.Delta + (_swipe.Invert ? -along : along));
    }

    private Outcome EndSwipe()
    {
        // An update after the fingers lift belongs to no swipe until the next one begins.
        _gestureFindFailed = true;
        var gesture = _activeGesture;
        _activeGesture = null;
        return gesture is { IsWorkspace: true } && _workspaceSwipe is { } swipe ? FinishWorkspaceSwipe(swipe) : View([]);
    }

    private bool PickGesture(int fingers)
    {
        var axis = Math.Abs(_swipeTotalX) > Math.Abs(_swipeTotalY) ? GestureDirection.Horizontal : GestureDirection.Vertical;
        var direction = axis == GestureDirection.Horizontal
            ? _swipeTotalX < 0 ? GestureDirection.Left : GestureDirection.Right
            : _swipeTotalY < 0 ? GestureDirection.Up : GestureDirection.Down;
        var held = _keyState.Held;
        foreach (var gesture in _gestures)
        {
            if (gesture.Direction != axis && gesture.Direction != direction && gesture.Direction != GestureDirection.Swipe)
                continue;

            if (gesture.Fingers != fingers || gesture.Modifiers != held)
                continue;

            Activate(gesture);
            return true;
        }

        return false;
    }

    // A workspace swipe reads distance along its configured direction.
    private void Activate(TrackpadGesture gesture)
    {
        _activeGesture = gesture;
        _gestureDirection = gesture.Direction;
        StartWorkspaceSwipe();
    }

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
        if (onMonitor < 2 && !_swipe.CreateNew)
            return;

        BeginWorkspaceSwipe(monitor);
    }

    private void BeginWorkspaceSwipe(MonitorId monitor) =>
        _workspaceSwipe = new WorkspaceSwipe(ActiveOn(monitor), monitor);

    private Outcome StepWorkspaceSwipe(WorkspaceSwipe swipe, double delta)
    {
        var distance = Math.Max(1, _swipe.Distance);
        var step = swipe.Delta - delta;
        swipe.Delta = delta;
        swipe.AverageSpeed = ((swipe.AverageSpeed * swipe.SpeedPoints) + Math.Abs(step)) / (swipe.SpeedPoints + 1);
        swipe.SpeedPoints++;

        var (left, right) = Neighbours(swipe.Monitor);
        var begin = swipe.Begin;
        if ((left is null || right is null || left == begin) && !_swipe.CreateNew)
        {
            _workspaceSwipe = null;
            return ReturnSwipe(swipe);
        }

        swipe.Delta = Math.Clamp(swipe.Delta, -distance, distance);
        var beginRank = SwipeRank(begin);
        var emptyBegin = FindWorkspace(begin) is not { } beginDesk || beginDesk.Layout.Clients.Count == 0;
        if ((left == begin && _swipe.CreateNew && swipe.Delta < 0)
            || (swipe.Delta > 0 && emptyBegin && SwipeRank(right) <= beginRank)
            || (swipe.Delta < 0 && beginRank <= SwipeRank(left)))
        {
            swipe.Delta = 0;
            return PreviewSwipe(swipe, null);
        }

        if (_swipe.DirectionLock)
        {
            var sign = swipe.Delta < 0 ? -1 : 1;
            if (_swipeInitialDirection != 0 && _swipeInitialDirection != sign)
                swipe.Delta = 0;
            else if (_swipeInitialDirection == 0 && Math.Abs(swipe.Delta) > _swipe.DirectionLockThreshold)
                _swipeInitialDirection = sign;
        }

        var toward = swipe.Delta < 0 ? left : right;
        var away = swipe.Delta < 0 ? SwipeRank(left) > beginRank : SwipeRank(right) < beginRank;
        if (away || toward is not WorkspaceId target || FindWorkspace(target) is null)
        {
            // Past the last Workspace the swipe only continues when it may create one.
            if (!_swipe.CreateNew)
                swipe.Delta = 0;
            return PreviewSwipe(swipe, null);
        }

        if (!_swipe.Forever || Math.Abs(swipe.Delta) < distance)
            return PreviewSwipe(swipe, target);

        var switched = FinishWorkspaceSwipe(swipe);
        BeginWorkspaceSwipe(swipe.Monitor);
        return switched;
    }

    private Outcome FinishWorkspaceSwipe(WorkspaceSwipe swipe)
    {
        var distance = Math.Max(1, _swipe.Distance);
        var (left, right) = Neighbours(swipe.Monitor);
        var begin = swipe.Begin;

        // Swiping off the right end with create_new picks the id of a new Workspace now.
        if (SwipeRank(right) <= SwipeRank(begin) && _swipe.CreateNew)
            right = Resolve("r+1", swipe.Monitor);

        _workspaceSwipe = null;
        _swipeInitialDirection = 0;

        var magnitude = Math.Abs(swipe.Delta);
        var slow = _swipe.MinSpeedToForce == 0 || swipe.AverageSpeed < _swipe.MinSpeedToForce;
        if ((magnitude < distance * _swipe.CancelRatio && slow) || magnitude < 2)
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
        var prefix = _swipe.UseRelative ? "r" : "m";
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

        var listed = Listed(null);
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
    }
}
