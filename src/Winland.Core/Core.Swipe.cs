namespace Winland;

public sealed partial class Core
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

    // How far the fingers have gone, along their direction, since a gesture that acts on release was picked.
    private double _releaseDistance;

    /// <summary><c>gestures:workspace_swipe_touch</c>, which the touch screen reader reads with no lock.</summary>
    public bool TouchSwipe => _swipe.Touch;

    private void ReadGestures(ConfigDocument document)
    {
        _gestures = document.Gestures;
        _swipe = GestureOptions.From(document);
    }

    private Outcome BeginSwipe()
    {
        if (_activeGesture is not null)
            return View([]);

        _pinching = false;
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

        Outcome? begun = null;
        if (_activeGesture is null && !PickGesture(updated.Fingers, out begun))
        {
            _gestureFindFailed = true;
            return View([]);
        }

        return UpdateActive(updated, begun);
    }

    // One step of the gesture a swipe or pinch picked. The update that picked it is its first step.
    private Outcome UpdateActive(SwipeUpdated updated, Outcome? begun)
    {
        if (_activeGesture is { IsFunction: true } function)
            return CallFunction(function, begun is null ? GesturePhase.Update : GesturePhase.Start, updated);

        if (_activeGesture is { IsScrollMove: true } scrollMove)
        {
            var stepped = StepScrollMove(scrollMove, updated);
            return begun is null ? stepped : Merge(begun, stepped);
        }

        if (_activeGesture is { IsMove: true } or { IsResize: true })
        {
            var stepped = StepWindowGesture(_activeGesture.Scale * updated.DeltaX, _activeGesture.Scale * updated.DeltaY);
            return begun is null ? stepped : Merge(begun, stepped);
        }

        if (_activeGesture is { IsClose: true } closing)
        {
            var preview = StepCloseSwipe(closing.Scale * Travel(updated.DeltaX, updated.DeltaY));
            return begun is null ? preview : Merge(begun, preview);
        }

        if (_activeGesture is { IsOnRelease: true } onRelease)
        {
            _releaseDistance += onRelease.Scale * Travel(updated.DeltaX, updated.DeltaY);
            return begun ?? View([]);
        }

        if (_activeGesture is { IsSpecial: true } special)
        {
            StepSpecialSwipe(special.Scale * Travel(updated.DeltaX, updated.DeltaY));
            return begun ?? View([]);
        }

        if (_activeGesture is not { IsWorkspace: true } gesture || _workspaceSwipe is not { } swipe)
            return View([]);

        var along = gesture.Scale * Travel(updated.DeltaX, updated.DeltaY);
        return StepWorkspaceSwipe(swipe, swipe.Delta + (_swipe.Invert ? -along : along));
    }

    private Outcome EndSwipe(bool cancelled, long timeMs)
    {
        // An update after the fingers lift belongs to no swipe until the next one begins.
        _gestureFindFailed = true;
        if (_activeGesture is not { } gesture)
            return View([]);

        _activeGesture = null;
        if (gesture.IsFunction)
        {
            var end = new GestureCall(gesture.Argument, GesturePhase.End, gesture.Direction, timeMs, Cancelled: cancelled);
            return View([]) with { GestureCalls = [end] };
        }

        if (gesture.IsScrollMove)
            return EndScrollMove(cancelled);

        if (gesture.IsClose)
            return EndCloseSwipe();

        if (gesture.IsMove || gesture.IsResize)
            return EndWindowGesture();

        if (gesture.IsOnRelease)
            return ReleaseGesture(gesture, _releaseDistance);

        if (gesture.IsSpecial)
            return EndSpecialSwipe();

        return _workspaceSwipe is { } swipe ? FinishWorkspaceSwipe(swipe) : View([]);
    }

    private bool PickGesture(int fingers, out Outcome? begun)
    {
        begun = null;
        if (_pinching)
            return false;

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

            begun = Activate(gesture, direction);
            return true;
        }

        return false;
    }

    // Starts the gesture a swipe or pinch picked. A workspace swipe reads distance along its configured direction, as
    // Winland's direction sensitive gestures do, and every other gesture along the one the fingers took.
    private Outcome? Activate(TrackpadGesture gesture, GestureDirection direction)
    {
        _activeGesture = gesture;
        _releaseDistance = 0;
        _gestureDirection = gesture.IsWorkspace ? gesture.Direction : direction;
        if (gesture.IsWorkspace)
            StartWorkspaceSwipe();
        else if (gesture.IsSpecial)
            return BeginSpecialSwipe(gesture);
        else if (gesture.IsScrollMove)
            BeginScrollMove();
        else if (gesture.IsFunction)
            return View([]);
        else if (gesture.IsClose)
            BeginCloseSwipe();
        else if (gesture.IsMove || gesture.IsResize)
            return BeginWindowGesture(gesture);

        return null;
    }

    // A luafunction gesture hands the Lua config each phase of the swipe. The update that picks the gesture starts it
    // and is its first update too, and each phase has the direction the fingers took.
    private Outcome CallFunction(TrackpadGesture gesture, GesturePhase phase, SwipeUpdated updated)
    {
        GestureCall Call(GesturePhase each) =>
            new(gesture.Argument, each, _gestureDirection, updated.TimeMs, updated.Fingers, updated.DeltaX, updated.DeltaY);

        IReadOnlyList<GestureCall> calls = phase == GesturePhase.Start
            ? [Call(GesturePhase.Start), Call(GesturePhase.Update)]
            : [Call(GesturePhase.Update)];
        return View([]) with { GestureCalls = calls };
    }

    // The dispatcher, close, float and fullscreen gestures act once the fingers lift, as Winland ends them:
    // a dispatcher always runs, and close, float and fullscreen act on the focused window when the fingers went far
    // enough along their direction. float takes float, tile or toggle, and fullscreen takes fullscreen or maximize.
    private Outcome ReleaseGesture(TrackpadGesture gesture, double distance)
    {
        if (distance < gesture.ReleaseDistance)
            return View([]);

        switch (gesture.Action)
        {
            case TrackpadGesture.DispatcherAction:
                return gesture.Argument.Length == 0 ? View([]) : ExecuteTriggered(gesture.Argument, gesture.DispatchArguments, trigger: null);
            case TrackpadGesture.CloseAction:
                return ActiveWindow() is null ? View([]) : ExecuteTriggered("killactive", "", trigger: null);
            case TrackpadGesture.FloatAction:
                var mode = gesture.Argument.ToLowerInvariant();
                var floating = mode.StartsWith("float", StringComparison.Ordinal) ? "setfloating"
                    : mode.StartsWith("tile", StringComparison.Ordinal) ? "settiled"
                    : "togglefloating";
                return ExecuteTriggered(floating, "", trigger: null);
            default:
                var maximize = gesture.Argument.StartsWith("maximize", StringComparison.OrdinalIgnoreCase);
                return ExecuteTriggered("fullscreen", maximize ? "1" : "0", trigger: null);
        }
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
        return SwitchTo(Alias(target), focus: null, consumed: false, dispatcher: "workspace");
    }

    private (WorkspaceId? Left, WorkspaceId? Right) Neighbours(MonitorId monitor)
    {
        var prefix = _swipe.UseRelative ? "r" : "m";
        return (Resolve(prefix + "-1", monitor), Resolve(prefix + "+1", monitor));
    }

    private WorkspaceId? Resolve(string address, MonitorId monitor) =>
        WorkspaceAddress.TryResolve(address, Context(monitor), out var id) ? id : null;

    // Workspace ids order as numbers. A named Workspace sorts below every number, newest first, and an
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
