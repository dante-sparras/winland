namespace Winland.Application;

/// <summary>The pointer: key and mouse binds, focus follows mouse, and resizing by the border.</summary>
public sealed partial class WindowManager
{
    private string? _resizeCursor;

    private Outcome RunKey(Input input)
    {
        var match = _keyState.Decide(_config.Binds, input);

        // The mouse dispatcher bound to a key drags the window under the pointer while the key is held, as in the pinned
        // release, and the key's release drops it.
        if (input is KeyPressed pressed && _interaction is null && KeyDrag(match) is { } drag && DragTarget(_pointer) is WindowId window)
        {
            var begun = BeginGesture(window, drag, pressed.Key, _pointer!.Value);
            var ran = RunBind(match) with { Focus = window };
            return begun.Count == 0 ? ran : ran with { Placements = begun };
        }

        if (input is KeyPressed && _interaction is null && KeyDrag(match) is not null && OnlyDrag(match))
            return RunBind(match) with { Consumed = false };

        if (input is KeyReleased released && _interaction is Gesture gesture && gesture.Button.Equals(released.Key, StringComparison.OrdinalIgnoreCase))
        {
            var ended = EndGesture(gesture, _pointer ?? gesture.Origin);
            NoteBorderCursor(_pointer ?? gesture.Origin);
            var after = RunBind(match);
            return ended.Then(after) with { Consumed = true, Dispatcher = after.Dispatcher };
        }

        return RunBind(match);
    }

    private static BindDispatch? KeyDrag(BindDecision match)
    {
        foreach (var dispatch in DispatchesOf(match))
        {
            if (dispatch.Drag && IsDragDispatcher(dispatch))
                return dispatch;
        }

        return null;
    }

    private Outcome PressPointer(PointerButtonPressed pressed)
    {
        var match = _keyState.Decide(_config.Binds, pressed);

        // A drag already running keeps the pointer. A second drag button does not start another one.
        if (_interaction is null && TryDragHit(match, out var drag) && (TabUnder(drag, pressed.Position) ?? DragTarget(pressed.Position)) is WindowId target)
        {
            var tabbed = TakeTabOut(drag, pressed.Position, out var window);
            window ??= target;
            var begun = BeginGesture(window.Value, drag, pressed.Button, pressed.Position);
            var ran = RunBind(match) with { Focus = window };
            ran = tabbed is null ? ran : tabbed.Then(ran);
            return begun.Count == 0 ? ran : ran with { Placements = Outcome.Union(ran.Placements, begun, ran.State) };
        }

        if (_interaction is null && !match.Consumed && TryBorderTarget(pressed.Position, out var hit, out var bounds, out var floating, out _))
        {
            Outcome? ran = match.Dispatcher is null && match.Dispatches.Count == 0 ? null : RunBind(match);
            var border = StartBorder(pressed, hit, bounds, floating);
            if (ran is null || ran.Placements.Count == 0 || border.Placements.Count > 0)
                return border;

            return border with { Placements = ran.Placements };
        }

        NoteBorderCursor(pressed.Position);
        if (_interaction is null && TryDragHit(match, out _) && OnlyDrag(match))
            return RunBind(match) with { Consumed = false };

        return RunBind(match);
    }

    private Outcome ReleasePointer(PointerButtonReleased released)
    {
        Outcome? ended = null;
        if (_interaction is BorderDrag border && border.Button.Equals(released.Button, StringComparison.OrdinalIgnoreCase))
        {
            _interaction = null;
            ended = EndBorder(border, released.Position);
        }
        else if (_interaction is Gesture gesture && gesture.Button.Equals(released.Button, StringComparison.OrdinalIgnoreCase))
        {
            ended = EndGesture(gesture, released.Position);
        }

        NoteBorderCursor(released.Position);
        var ran = RunBind(_keyState.Decide(_config.Binds, released));
        if (ended is null)
            return ran;

        var merged = ended.Then(ran);
        return merged with
        {
            Consumed = ended.Consumed || ran.Consumed,
            Dispatcher = ran.Dispatcher,
        };
    }

    private long? _lastMoveAt;
    private double _traveled;
    private WindowId? _lastMouse;

    private Outcome MovePointer(PointerMoved moved)
    {
        _ = _keyState.Decide(_config.Binds, moved);
        NoteBorderCursor(moved.Position);
        if (_interaction is BorderDrag border)
            return MoveBorder(border, moved.Position);

        if (_interaction is Gesture gesture)
        {
            _lastMoveAt = When(moved.TimeMs);
            return MoveGesture(gesture, moved.Position);
        }

        return View([], focus: Follow(moved));
    }

    private WindowId? Follow(PointerMoved moved)
    {
        var now = When(moved.TimeMs);
        if (_pointer is Point held && held == moved.Position)
        {
            _lastMoveAt = now;
            return null;
        }

        var traveled = Advance(moved.Position, now);
        if (_config.Session.MouseMoveFocusesMonitor)
            FocusMonitorAt(moved.Position);

        if (_keyState.ButtonsHeld)
            return null;

        var under = WindowUnder(moved.Position, shrink: true);
        var decision = PointerFocus.Choose(
            _config.Input.FollowMouse,
            _config.Input.MouseRefocus,
            _config.Input.FloatSwitchOverrideFocus,
            traveled,
            _config.Input.FollowMouseThreshold,
            under,
            Floats(under),
            _focus.Focused,
            Floats(_focus.Focused),
            _lastMouse);
        if (decision.RememberMouse)
            _lastMouse = decision.LastMouse;

        // A window with no_follow_mouse is still remembered as the one under the pointer, but moving onto it does not
        // focus it. A refocus, after a window closes or moves, still does.
        if (decision.Focus is WindowId found && Own(found, WindowProp.NoFollowMouse) == true)
            return null;

        return TakePointerFocus(decision.Focus);
    }

    private WindowId? TakePointerFocus(WindowId? window)
    {
        if (window is not WindowId next || next == _focus.Focused || HeldByStay(next) || HasNoFocus(next))
            return null;

        if (WorkspaceContaining(next) is not LiveWorkspace desk || !IsShown(desk.Id))
            return null;

        NoteFocus(next);
        if (_focus.Focused != next)
            return null;

        desk.Layout.NotePointerFocus(next);
        Because(FocusReason.FollowMouse);
        return next;
    }

    private double Advance(Point position, long now)
    {
        var step = 0d;
        if (_pointer is Point previous)
            step = Distance(previous, position) / ScaleOf(position);

        var recent = _lastMoveAt is long seen && now >= seen && now - seen < 500;
        _lastMoveAt = now;
        if (_config.Input.FollowMouse == 1 && recent)
            _traveled += step;
        else
            _traveled = 0;

        return _traveled;
    }

    private void FocusMonitorAt(Point point)
    {
        if (_monitors.Count == 0)
            return;

        var screen = MonitorAt(point);
        if (screen is null || screen.Id == FocusedId())
            return;

        Focus(screen.Id);
    }

    private WindowId? WindowUnder(Point? point, bool shrink)
    {
        if (point is not Point at)
            return null;

        // A special workspace open over the normal one answers first, floating and tiled alike.
        var inset = shrink ? MonitorPixels.Physical(_config.Input.FollowMouseShrink, ScaleOf(at)) : 0;
        return PointerFocus.At(Targets(at, special: true), at, inset, _focus.Focused)
            ?? PointerFocus.At(Targets(at, special: false), at, inset, _focus.Focused);
    }

    private List<PointerFocus.Target> Targets(Point point, bool special)
    {
        LiveMonitor? screen = null;
        if (_monitors.Count > 0)
        {
            screen = MonitorAt(point);
            if (screen is null)
                return [];
        }

        var targets = new List<PointerFocus.Target>();
        foreach (var desk in _workspaces)
        {
            if (desk.Id.IsSpecial != special || !IsShown(desk.Id))
                continue;

            if (screen is not null && desk.Monitor != screen.Id)
                continue;

            var showing = desk.Showing();
            if (CoveringClient(desk, showing) is { } covering)
            {
                // A maximized or fullscreen Client owns the pointer on its Workspace. The tiles behind it
                // take no focus, and a maximized one answers only where it is drawn.
                var slot = desk.Layout.TryBounds(covering.Window, out var found) ? found : covering.Bounds;
                targets.Add(new PointerFocus.Target(covering.Window, covering.Floating, PlacedBounds(covering, desk, slot)));
                continue;
            }

            foreach (var client in desk.Layout.Clients)
            {
                if (client.Minimized || !showing.Contains(client.Window) || HasNoFocus(client.Window))
                    continue;

                if (!desk.Layout.TryBounds(client.Window, out var bounds))
                    continue;

                targets.Add(new PointerFocus.Target(client.Window, client.Floating, bounds));
            }
        }

        // Floating windows are tried from the top of the stacking order down, as Winland finds the topmost.
        var tiles = targets.Where(target => !target.Floating);
        var floats = targets.Where(target => target.Floating).OrderBy(target => _stacking.IndexOf(target.Window));
        return [.. tiles, .. floats];
    }

    private LiveMonitor? MonitorAt(Point point)
    {
        if (_monitors.Count == 0)
            return null;

        return Match(screen => screen.Covers(point));
    }

    private double ScaleOf(Point point)
    {
        var scale = MonitorAt(point)?.Effective.Scale ?? 1;
        return scale is > 0 and < 1000 && double.IsFinite(scale) ? scale : 1;
    }

    private bool Floats(WindowId? window) =>
        window is WindowId id && Find(id) is { Floating: true };

    private long When(long? time) => time ?? Clock();

    private static double Distance(Point from, Point to)
    {
        var dx = (double)to.X - from.X;
        var dy = (double)to.Y - from.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private Outcome StartBorder(PointerButtonPressed pressed, WindowId window, Rectangle bounds, bool floating)
    {
        var options = ActiveOptions();
        var (left, top) = BorderPointer.GrabbedCorner(options.ResizeCorner, floating, pressed.Position, bounds);
        _interaction = new BorderDrag(window, pressed.Button, floating, pressed.Position, bounds, left, top);
        var desk = LayoutWorkspace(window);
        _focus.Take(window, desk);
        desk.Layout.NoteFocus(window);
        NoteBorderCursor(pressed.Position);
        IReadOnlyList<Placement> begun = floating
            ? []
            : desk.Layout.Drag(window, new PointerDrag(true, PointerDragPhase.Begin, pressed.Position));
        return View(begun, consumed: true, dispatcher: "resizewindow", focus: window);
    }

    private Outcome MoveBorder(BorderDrag border, Point pointer)
    {
        var desk = LayoutWorkspace(border.Window);
        if (border.Floating)
        {
            var next = BorderPointer.Resize(border.Start, border.Origin, pointer, border.Left, border.Top);
            next = SnapBox(border.Window, next, SnapEdges.FromGrab(border.Left, border.Top), resize: true);
            return View(desk.Layout.PlaceFloating(border.Window, next), consumed: true, dispatcher: "resizewindow") with { Live = true };
        }

        return View(
            desk.Layout.Drag(border.Window, new PointerDrag(true, PointerDragPhase.Move, pointer)),
            consumed: true,
            dispatcher: "resizewindow") with { Live = true };
    }

    private Outcome EndBorder(BorderDrag border, Point pointer)
    {
        var desk = LayoutWorkspace(border.Window);
        if (!border.Floating)
        {
            return View(
                desk.Layout.Drag(border.Window, new PointerDrag(true, PointerDragPhase.End, pointer)),
                consumed: true,
                dispatcher: "resizewindow");
        }

        return View(desk.Layout.Visible(), consumed: true, dispatcher: "resizewindow");
    }

    private void NoteBorderCursor(Point pointer)
    {
        if (_interaction is Gesture gesture)
        {
            _resizeCursor = gesture.Cursor;
            return;
        }

        var options = ActiveOptions();
        if (!options.ResizeOnBorder || !options.HoverIconOnBorder)
        {
            _resizeCursor = null;
            return;
        }

        if (_interaction is BorderDrag border)
        {
            _resizeCursor = BorderPointer.Cursor(BorderPointer.GrabbedSide(border.Left, border.Top));
            return;
        }

        _resizeCursor = TryBorderTarget(pointer, out _, out _, out _, out var side)
            ? BorderPointer.Cursor(side)
            : null;
    }

    // The Workspace the pointer is over: the special workspace open on the Monitor under it, else that Monitor's, else
    // the focused one when no Monitor is under it.
    private LiveWorkspace PointerWorkspace(Point pointer)
    {
        if (MonitorAt(pointer) is { } screen && (SpecialShownOn(screen) ?? ShownOn(screen)) is WorkspaceId shown && _workspaces.Find(shown) is LiveWorkspace desk)
            return desk;

        return CurrentWorkspace();
    }

    private bool TryBorderTarget(Point pointer, out WindowId window, out Rectangle bounds, out bool floating, out BorderSide side)
    {
        window = default;
        bounds = default;
        floating = false;
        side = BorderSide.None;
        var options = ActiveOptions();
        return TryRingTarget(pointer, options, BorderPointer.Reach(options), out window, out bounds, out floating, out side);
    }

    /// <summary>
    /// The window whose ring of <c>reach</c> pixels around its tile holds the pointer, with the tile, and the side the
    /// pointer is on. The pointer inside a window's tile is that window's own, so it has no ring.
    /// </summary>
    private bool TryRingTarget(Point pointer, LayoutOptions options, int reach, out WindowId window, out Rectangle bounds, out bool floating, out BorderSide side)
    {
        window = default;
        bounds = default;
        floating = false;
        side = BorderSide.None;
        if (reach <= 0)
            return false;

        // The pointer meets the special workspace open on the Monitor under it before that Monitor's Workspace, as the
        // Winland looks on the Monitor under the pointer. A maximized or fullscreen Client takes no border resize
        // and hides the tiles behind it, but a floating window allowed over it keeps its ring.
        var desk = PointerWorkspace(pointer);
        var showing = desk.Showing();
        var covering = CoveringClient(desk, showing);
        var clients = new List<Client>(desk.Layout.Clients.Count);
        foreach (var client in desk.Layout.Clients)
        {
            if (client.Minimized || !showing.Contains(client.Window))
                continue;

            if (covering is not null && (!client.Floating || client.Window == covering.Window || !AllowedOverFullscreen(desk, client)))
                continue;

            clients.Add(client);
        }

        // The floating windows are tried from the top of the stacking order down.
        clients = [.. clients.Where(client => !client.Floating), .. clients.Where(client => client.Floating).OrderBy(client => _stacking.IndexOf(client.Window))];

        // Floating windows lie above the tiles, so a floating ring over a tile is still that window's grab.
        if (TryRing(desk, clients, pointer, reach, options, floatingOnly: true, out var inside, out window, out bounds, out side))
        {
            floating = true;
            return true;
        }

        if (inside)
            return false;

        return TryRing(desk, clients, pointer, reach, options, floatingOnly: false, out _, out window, out bounds, out side);
    }

    private static bool TryRing(
        LiveWorkspace desk,
        IReadOnlyList<Client> clients,
        Point pointer,
        int reach,
        LayoutOptions options,
        bool floatingOnly,
        out bool inside,
        out WindowId window,
        out Rectangle bounds,
        out BorderSide side)
    {
        inside = false;
        for (var i = clients.Count - 1; i >= 0; i--)
        {
            var client = clients[i];
            if (client.Minimized || client.Floating != floatingOnly || !desk.Layout.TryBounds(client.Window, out bounds))
                continue;

            // The topmost window under the pointer takes it: inside its rectangle the press is that window's own.
            if (BorderPointer.Contains(bounds, pointer))
            {
                inside = true;
                break;
            }

            if (!BorderPointer.InGrab(bounds, pointer, reach))
                continue;

            window = client.Window;
            side = BorderPointer.Side(bounds, pointer, BorderPointer.Corner(options));
            return true;
        }

        window = default;
        bounds = default;
        side = BorderSide.None;
        return false;
    }

    private Rectangle SnapBox(WindowId self, Rectangle proposed, SnapEdges edges, bool resize)
    {
        var options = ActiveOptions();
        var desk = LayoutWorkspace(self);
        var others = new List<Rectangle>();
        foreach (var client in desk.Layout.Clients)
        {
            if (client.Minimized || client.Window == self)
                continue;

            if (desk.Layout.TryBounds(client.Window, out var bounds))
                others.Add(bounds);
        }

        return FloatingSnap.Apply(proposed, edges, resize, others, WorkArea(desk, _area), FocusedMonitorBounds(), options);
    }

    private LayoutOptions ActiveOptions()
    {
        var screen = MonitorById(FocusedWorkspace().Monitor);
        if (screen is { Effective.Enabled: true })
            return _config.Layout.ScaledBy(screen.Effective.Scale);

        return _config.Layout;
    }

    private sealed class BorderDrag(WindowId window, string button, bool floating, Point origin, Rectangle start, bool left, bool top)
        : PointerInteraction(window)
    {
        public string Button { get; } = button;

        public bool Floating { get; } = floating;

        public Point Origin { get; } = origin;

        public Rectangle Start { get; } = start;

        public bool Left { get; } = left;

        public bool Top { get; } = top;
    }
}
