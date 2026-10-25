using System.Globalization;
using static Winland.Domain.OrdinalText;

namespace Winland.Application;

/// <summary>
/// Mouse binds that move or resize a Client: <c>movewindow</c> and <c>resizewindow</c> bound with <c>bindm</c>.
/// The drag takes the Client under the pointer and focuses it. With <c>binds:drag_threshold</c> above 0
/// the Client is not picked up until the pointer has moved farther than that many pixels.
/// </summary>
public sealed partial class WindowManager
{
    private Outcome ApplyGesture(PointerGesture gesture)
    {
        switch (gesture.Phase)
        {
            case PointerDragPhase.Begin:
                // A drag already running keeps the pointer. A second drag button does not start another one.
                var dispatch = new BindDispatch(gesture.Dispatcher, gesture.Arguments);
                if (_interaction is not null || (TabUnder(dispatch, gesture.Position) ?? DragTarget(gesture.Position)) is not WindowId target)
                    return View([], consumed: true, dispatcher: gesture.Dispatcher);

                var tabbed = TakeTabOut(dispatch, gesture.Position, out var window);
                window ??= target;
                var begun = BeginGesture(window.Value, dispatch, gesture.Button, gesture.Position);
                var view = View(begun, consumed: true, dispatcher: gesture.Dispatcher, focus: window);
                return tabbed is null ? view : tabbed.Then(view);
            case PointerDragPhase.Move when _interaction is Gesture held:
                return MoveGesture(held, gesture.Position);
            case PointerDragPhase.End when _interaction is Gesture held && held.Button.Equals(gesture.Button, StringComparison.OrdinalIgnoreCase):
                return EndGesture(held, gesture.Position);
            default:
                return View([]);
        }
    }

    private IReadOnlyList<Placement> BeginGesture(WindowId window, BindDispatch drag, string button, Point pointer)
    {
        var desk = LayoutWorkspace(window);
        var layout = desk.Layout;
        Give(window, desk);
        var gesture = new Gesture(window, button, IsResize(drag.Dispatcher), drag.Dispatcher, pointer)
        {
            Cursor = DragCursor(desk, window, IsResize(drag.Dispatcher), pointer),
            WasTiled = LayoutWorkspace(window).ClientOf(window) is { Floating: false },
            KeepRatio = IsResize(drag.Dispatcher) && KeepsRatio(drag.Arguments),
            PickedUp = _config.BindOptions.DragThreshold <= 0,
        };

        _interaction = gesture;
        NoteBorderCursor(pointer);
        return gesture.PickedUp ? PickUp(window, gesture, pointer) : [];
    }

    // A Client picked up while fullscreen or maximized leaves that mode first. A floating one
    // is then put under the pointer by its middle. The drag starts from there.
    private IReadOnlyList<Placement> PickUp(WindowId window, Gesture gesture, Point pointer)
    {
        var desk = LayoutWorkspace(window);
        if (desk.ClientOf(window) is not { Fullscreen: not FullscreenMode.None } client)
            return desk.Layout.Drag(window, gesture.Step(PointerDragPhase.Begin, pointer));

        var placements = new List<Placement>(Restamp(desk, EndFullscreen(desk, window)));
        if (client.Floating && desk.Layout.TryBounds(window, out var restored))
        {
            placements.AddRange(desk.Layout.PlaceFloating(window, restored with { X = pointer.X - (restored.Width / 2), Y = pointer.Y - (restored.Height / 2) }));
        }

        placements.AddRange(desk.Layout.Drag(window, gesture.Step(PointerDragPhase.Begin, pointer)));
        return placements;
    }

    private Outcome MoveGesture(Gesture gesture, Point pointer)
    {
        var layout = LayoutWorkspace(gesture.Window).Layout;
        if (!gesture.PickedUp)
        {
            if (!PastThreshold(gesture.Origin, pointer, _config.BindOptions.DragThreshold))
                return View([], consumed: true, dispatcher: gesture.Dispatcher);

            // The drag starts where the pointer crossed the threshold, so the Client does not jump.
            gesture.PickedUp = true;
            var lifted = PickUp(gesture.Window, gesture, pointer);
            var dragged = layout.Drag(gesture.Window, gesture.Step(PointerDragPhase.Move, pointer));
            if (FollowDraggedMonitor(gesture) is { } carried)
                return carried with { Placements = [.. lifted, .. carried.Placements], Live = true };

            return View([.. lifted, .. dragged], consumed: true, dispatcher: gesture.Dispatcher) with { Live = true };
        }

        var moved = layout.Drag(gesture.Window, gesture.Step(PointerDragPhase.Move, pointer));
        if (FollowDraggedMonitor(gesture) is { } crossed)
            return crossed with { Live = true };

        return View(moved, consumed: true, dispatcher: gesture.Dispatcher) with { Live = true };
    }

    private Outcome EndGesture(Gesture gesture, Point pointer)
    {
        _interaction = null;
        // The Group under the pointer is found while the dragged window is still lifted.
        var drop = gesture.PickedUp && !gesture.Resize ? GroupDrop(gesture.Window, gesture.WasTiled, pointer) : null;
        if (drop is null && gesture.PickedUp && !gesture.Resize && DropOnMonitor(gesture, pointer) is { } carried)
            return carried;

        IReadOnlyList<Placement> ended = gesture.PickedUp
            ? LayoutWorkspace(gesture.Window).Layout.Drag(gesture.Window, gesture.Step(PointerDragPhase.End, pointer))
            : [];
        // A window that already joined another Workspace during the drag is focused there when it is dropped, as a drop
        // onto that Monitor did. A resize leaves the focused Monitor where it was.
        var followed = gesture.MovedWorkspace && !gesture.Resize && FocusDragged(gesture.Window);
        if (drop is { } into)
            return JoinDragged(gesture.Window, into, gesture.Dispatcher);

        return View(ended, consumed: true, dispatcher: gesture.Dispatcher, focus: followed ? gesture.Window : null);
    }

    private readonly record struct DropTarget(LiveWorkspace Desk, WindowGroup Group, int? Index);

    // A move that starts on a tab of a Group's bar takes that member out of the Group and drags it alone, as the pinned
    // release begins a window drag on its group bar. A Group of one, a press between tabs and a resize drag the window
    // under the pointer as before. Returns how the member left the Group, or null.
    private Outcome? TakeTabOut(BindDispatch drag, Point pointer, out WindowId? member)
    {
        member = TabUnder(drag, pointer);
        if (member is not WindowId window || WorkspaceContaining(window) is not LiveWorkspace desk || desk.GroupOf(window) is not WindowGroup group)
        {
            member = null;
            return null;
        }

        return OutOfGroup(desk, group, window, direction: null, drag.Dispatcher);
    }

    // The member whose tab a move drag starts on, in a Group of more than one, or null.
    private WindowId? TabUnder(BindDispatch drag, Point pointer)
    {
        if (IsResize(drag.Dispatcher))
            return null;

        foreach (var bar in GroupBars(Snapshot()))
        {
            if (!Inside(bar.Bounds, pointer))
                continue;

            var index = bar.TabAt(pointer.X - bar.Bounds.X, pointer.Y - bar.Bounds.Y);
            return index < 0 || bar.Tabs.Count < 2 ? null : bar.Tabs[index].Window;
        }

        return null;
    }

    // While a floating window is dragged, Winland gives it the Workspace the Monitor under its middle shows,
    // the special one when that is open, on each move. A lifted tile stays floating so the drag can continue, and drops
    // into that Layout when the button is released. Null when the middle is already on the window's own Workspace.
    private Outcome? FollowDraggedMonitor(Gesture gesture) => MoveWithMonitor(gesture, settle: false, pointer: default);

    // A release whose window was not moved during the drag still joins the Workspace under its middle, and a lifted tile
    // is dropped there. A move that already crossed does not come through here.
    private Outcome? DropOnMonitor(Gesture gesture, Point pointer) => MoveWithMonitor(gesture, settle: true, pointer);

    private Outcome? MoveWithMonitor(Gesture gesture, bool settle, Point pointer)
    {
        var window = gesture.Window;
        var source = LayoutWorkspace(window);
        if (source.ClientOf(window) is not { Floating: true } || !source.Layout.TryBounds(window, out var box))
            return null;

        var middle = new Point(box.X + (box.Width / 2), box.Y + (box.Height / 2));
        if (MonitorAt(middle) is not { } screen || (_workspaces.Shown.SpecialOn(screen.Id) ?? _workspaces.Shown.Shown(screen.Id)) is not WorkspaceId shown
            || _workspaces.Find(shown) is not LiveWorkspace target || target.Id == source.Id)
        {
            return null;
        }

        // Take forgets the drag. Hand it to the Workspace the window is joining first, so the grab offset survives.
        if (!settle)
            HandDrag(source.Layout, target.Layout);

        if (!source.Take(window, out var taken))
        {
            if (!settle)
                HandDrag(target.Layout, source.Layout);

            return null;
        }

        var tileBack = settle && gesture.WasTiled;
        var monitor = MonitorById(target.Monitor) is { Effective.Enabled: true } destination ? destination.Bounds : default;
        target.Receive(window, taken with { Floating = !tileBack, Bounds = box }, WorkArea(target, _area), monitor, pointer: tileBack ? pointer : null);
        CarryGroup(source, target, window);
        gesture.MovedWorkspace = true;
        FocusState.Remember(target, window);
        if (settle)
            Give(window, target);

        var placements = new List<Placement>();
        if (_workspaces.Shown.IsShown(source.Id))
            placements.AddRange(source.Layout.Visible());
        if (_workspaces.Shown.IsShown(target.Id))
            placements.AddRange(target.Layout.Visible());

        return View(placements, consumed: true, dispatcher: gesture.Dispatcher, focus: settle ? window : null);
    }

    // The monitor focus follows at the drop, not while the pointer is still dragging. True when this window is on a
    // Workspace other than the one the focused Monitor was showing.
    private bool FocusDragged(WindowId window)
    {
        if (WorkspaceContaining(window) is not LiveWorkspace target)
            return false;

        var focused = FocusedId();
        if (target.Monitor == focused && target.Id == _workspaces.Shown.ActiveOn(focused) && _workspaces.Shown.SpecialOn(focused) != target.Id)
            return false;

        Give(window, target);
        return true;
    }

    private static void HandDrag(Layout from, Layout to)
    {
        if (from is IDragOwner source && to is IDragOwner target)
            source.Drag.HandTo(target.Drag);
    }

    // The pointer shape while a drag holds a window, as Winland overrides it: grabbing for a move, and for a
    // resize the corner that is grabbed.
    private static string DragCursor(LiveWorkspace desk, WindowId window, bool resize, Point pointer)
    {
        if (!resize)
            return "grabbing";

        var bounds = desk.Layout.TryBounds(window, out var found) ? found : default;
        var (left, top) = BorderPointer.GrabbedCorner(pointer, bounds);
        return BorderPointer.Cursor(BorderPointer.GrabbedSide(left, top))!;
    }

    // group:drag_into_group, as Winland ends a window drag. A window dropped on a Group's tab bar joins that
    // Group at the tab under the pointer, and one dropped on a member joins after the current member. A dragged Group
    // brings every member. A floating window does not join a tiled Group, except on its tab bar with
    // group:merge_floated_into_tiled_on_groupbar.
    private DropTarget? GroupDrop(WindowId dragged, bool wasTiled, Point pointer)
    {
        if (_config.Groups.DragIntoGroup == 0 || WorkspaceContaining(dragged) is not LiveWorkspace from)
            return null;

        var own = from.GroupOf(dragged);
        foreach (var bar in GroupBars(Snapshot()))
        {
            if (bar.Shown == dragged || !Inside(bar.Bounds, pointer))
                continue;

            if (WorkspaceContaining(bar.Shown) is not LiveWorkspace there || there.GroupOf(bar.Shown) is not WindowGroup target)
                break;

            var floatedIntoTiled = there.ClientOf(bar.Shown) is { Floating: false } && !wasTiled;
            if (!CanDragInto(from, dragged, target) || floatedIntoTiled)
                break;

            return new DropTarget(there, target, bar.DropIndex(pointer));
        }

        if (_config.Groups.DragIntoGroup != 1
            || WindowUnderExcept(pointer, dragged) is not WindowId under
            || WorkspaceContaining(under) is not LiveWorkspace home
            || home.GroupOf(under) is not WindowGroup group
            || (home.ClientOf(under) is { Floating: false } && !wasTiled)
            || !CanDragInto(from, dragged, group))
            return null;

        return new DropTarget(home, group, null);
    }

    // A dragged window, or the whole Group it is in, joins any other Group. The callers hold back a floating window
    // over a tiled Group.
    private static bool CanDragInto(LiveWorkspace from, WindowId dragged, WindowGroup target) =>
        from.GroupOf(dragged) != target;

    private Outcome JoinDragged(WindowId dragged, DropTarget drop, string? dispatcher)
    {
        var from = LayoutWorkspace(dragged);
        var (there, target, index) = (drop.Desk, drop.Group, drop.Index);
        var covered = there.Covered();
        var members = from.GroupOf(dragged) is WindowGroup own ? [.. own.Members] : new List<WindowId> { dragged };
        for (var i = 0; i < members.Count; i++)
        {
            if (!TakeForGroup(from, members[i], out var taken))
                continue;

            var position = index is int at ? at + i : target.Current + 1;
            position = Math.Clamp(position, 0, target.Count);
            there.Join(target, members[i], taken, position);
            there.Show(target, position);
        }

        if (from != there)
        {
            Restamp(from, []);
            DropIfEmpty(from.Id);
            Focus(there.Monitor);
        }

        Restamp(there, []);
        Give(dragged, there);
        (_extraEvents ??= []).Add(new ClientIntoGroup(dragged));
        var outcome = Present(there, covered, there.Layout.Visible(), dispatcher: dispatcher, focus: dragged) with { Consumed = dispatcher is not null };
        return from != there && _workspaces.Find(from.Id) is LiveWorkspace left && _workspaces.Shown.IsShown(left.Id)
            ? outcome with { Placements = [.. left.Layout.Placed(), .. outcome.Placements] }
            : outcome;
    }

    // The Client under the pointer, leaving out the one being dragged, which the pointer holds.
    private WindowId? WindowUnderExcept(Point point, WindowId except)
    {
        foreach (var special in new[] { true, false })
        {
            var targets = Targets(point, special);
            targets.RemoveAll(target => target.Window == except);
            if (PointerFocus.At(targets, point) is WindowId found)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Whether a mouse bind's drag pressed at <paramref name="pointer"/> would take a window. The mouse hook asks before
    /// it keeps the press, since Winland lets the press through when no window is there. It never waits:
    /// with the core busy it answers true, and the press is kept.
    /// </summary>
    public bool HasDragTarget(Point pointer)
    {
        if (!_gate.TryEnter())
            return true;

        try
        {
            return DragTarget(pointer) is not null;
        }
        finally
        {
            _gate.Exit();
        }
    }

    // A press whose only action is a drag, which with no window under the pointer Winland lets through.
    private static bool OnlyDrag(BindDecision match) => DispatchesOf(match).All(dispatch => dispatch.Dispatcher.Length == 0 || IsDragDispatcher(dispatch));

    private static bool Inside(Rectangle box, Point point) =>
        point.X >= box.X && point.X < box.X + box.Width && point.Y >= box.Y && point.Y < box.Y + box.Height;

    // A window that closes ends any drag that holds it, as Winland ends its drag target on unmap.
    private void ForgetDrag(WindowId window)
    {
        if (_interaction?.Window == window)
            _interaction = null;
        if (_released?.Window == window)
            _released = null;
    }

    private static bool PastThreshold(Point origin, Point pointer, int threshold)
    {
        var dx = (long)pointer.X - origin.X;
        var dy = (long)pointer.Y - origin.Y;
        return (dx * dx) + (dy * dy) > (long)threshold * threshold;
    }

    // resizewindow 1 keeps the aspect ratio.
    private static bool KeepsRatio(string arguments) =>
        int.TryParse(arguments.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mode) && mode == 1;

    // The Client a mouse bind takes: the one under the pointer, or else one whose border is under it, as the pinned
    // release finds the window with its reserved extents.
    private WindowId? DragTarget(Point? point)
    {
        if (point is not Point at)
            return null;

        if (WindowUnder(at) is WindowId inside)
            return inside;

        foreach (var special in new[] { true, false })
        {
            var grown = new List<PointerFocus.Target>();
            foreach (var target in Targets(at, special))
            {
                var border = WorkspaceContaining(target.Window)?.Options.BorderFor(new ClientId(target.Window.Value)) ?? 0;
                var bounds = target.Bounds;
                grown.Add(target with { Bounds = new Rectangle(bounds.X - border, bounds.Y - border, bounds.Width + (2 * border), bounds.Height + (2 * border)) });
            }

            if (PointerFocus.At(grown, at) is WindowId edge)
                return edge;
        }

        return null;
    }

    private static bool TryDragHit(BindDecision match, out BindDispatch drag)
    {
        foreach (var dispatch in DispatchesOf(match))
        {
            if (IsDragDispatcher(dispatch))
            {
                drag = dispatch;
                return true;
            }
        }

        drag = default;
        return false;
    }

    private static bool IsDragDispatcher(BindDispatch dispatch) =>
        (Is(dispatch.Dispatcher, "movewindow") && dispatch.Arguments.Length == 0) || IsResize(dispatch.Dispatcher);

    private static bool IsResize(string dispatcher) =>
        Is(dispatcher, "resizewindow");

    private sealed class Gesture(WindowId window, string button, bool resize, string dispatcher, Point origin)
        : PointerInteraction(window)
    {
        public string Button { get; } = button;

        public bool Resize { get; } = resize;

        public string Dispatcher { get; } = dispatcher;

        public Point Origin { get; } = origin;

        public bool KeepRatio { get; init; }

        public bool WasTiled { get; init; }

        public bool PickedUp { get; set; }

        /// <summary>The window joined another Workspace while this drag held it.</summary>
        public bool MovedWorkspace { get; set; }

        /// <summary>The pointer shape while the drag holds the window.</summary>
        public string Cursor { get; init; } = "grabbing";

        public PointerDrag Step(PointerDragPhase phase, Point pointer) => new(Resize, phase, pointer, KeepRatio);
    }
}
