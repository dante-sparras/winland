using System.Globalization;
using static Winland.OrdinalText;

namespace Winland;

/// <summary>
/// Mouse binds that move or resize a Client: <c>movewindow</c> and <c>resizewindow</c> bound with <c>bindm</c>.
/// The drag takes the Client under the pointer and focuses it. With <c>binds:drag_threshold</c> above 0
/// the Client is not picked up until the pointer has moved farther than that many pixels.
/// </summary>
public sealed partial class Core
{
    private Outcome ApplyGesture(PointerGesture gesture)
    {
        switch (gesture.Phase)
        {
            case PointerDragPhase.Begin:
                // A drag already running keeps the pointer. A second drag button does not start another one.
                if (_gesture is not null || DragTarget(gesture.Position) is not WindowId window)
                    return View([], consumed: true, dispatcher: gesture.Dispatcher);

                var begun = BeginGesture(window, new BindDispatch(gesture.Dispatcher, gesture.Arguments), gesture.Button, gesture.Position);
                return View(begun, consumed: true, dispatcher: gesture.Dispatcher, focus: window);
            case PointerDragPhase.Move when _gesture is not null:
                return MoveGesture(gesture.Position);
            case PointerDragPhase.End when _gesture is not null && _gesture.Button.Equals(gesture.Button, StringComparison.OrdinalIgnoreCase):
                return EndGesture(gesture.Position);
            default:
                return View([]);
        }
    }

    private IReadOnlyList<Placement> BeginGesture(WindowId window, BindDispatch drag, string button, Point pointer)
    {
        NoteFocus(window);
        var layout = LayoutWorkspace(window).Layout;
        layout.NoteFocus(window);
        _gesture = new Gesture(window, button, IsResize(drag.Dispatcher), drag.Dispatcher, pointer)
        {
            WasTiled = LayoutWorkspace(window).ClientOf(window) is { Floating: false },
            KeepRatio = IsResize(drag.Dispatcher) && KeepsRatio(window, drag.Arguments),
            PickedUp = _binds.DragThreshold <= 0,
            CenterOnLift = _dragCenterWindow,
        };

        return _gesture.PickedUp ? PickUp(window, _gesture, pointer) : [];
    }

    // A Client picked up while fullscreen or maximized leaves that mode first, as in the pinned release. A floating one
    // is then put under the pointer: by its middle with binds:drag_center_window, and otherwise at the same fraction
    // of the window as where the fullscreen surface was grabbed. The drag starts from there.
    private IReadOnlyList<Placement> PickUp(WindowId window, Gesture gesture, Point pointer)
    {
        var desk = LayoutWorkspace(window);
        if (desk.ClientOf(window) is not { Fullscreen: not FullscreenMode.None } client)
            return desk.Layout.Drag(window, gesture.Step(PointerDragPhase.Begin, pointer));

        var before = PlacedBounds(client, desk, desk.Layout.TryBounds(window, out var slot) ? slot : client.Bounds);
        var placements = new List<Placement>(Restamp(desk, EndFullscreen(desk, window)));
        if (client.Floating && desk.Layout.TryBounds(window, out var restored))
        {
            var hotX = gesture.CenterOnLift || before.Width <= 0 ? restored.Width / 2 : (int)Math.Round(Math.Clamp((pointer.X - before.X) / (double)before.Width, 0, 1) * restored.Width);
            var hotY = gesture.CenterOnLift || before.Height <= 0 ? restored.Height / 2 : (int)Math.Round(Math.Clamp((pointer.Y - before.Y) / (double)before.Height, 0, 1) * restored.Height);
            placements.AddRange(desk.Layout.PlaceFloating(window, restored with { X = pointer.X - hotX, Y = pointer.Y - hotY }));
        }

        placements.AddRange(desk.Layout.Drag(window, gesture.Step(PointerDragPhase.Begin, pointer)));
        return placements;
    }

    private Outcome MoveGesture(Point pointer)
    {
        var gesture = _gesture!;
        var layout = LayoutWorkspace(gesture.Window).Layout;
        if (!gesture.PickedUp)
        {
            if (!PastThreshold(gesture.Origin, pointer, _binds.DragThreshold))
                return View([], consumed: true, dispatcher: gesture.Dispatcher);

            // The drag starts where the pointer crossed the threshold, so the Client does not jump.
            gesture.PickedUp = true;
            var lifted = PickUp(gesture.Window, gesture, pointer);
            return View([.. lifted, .. layout.Drag(gesture.Window, gesture.Step(PointerDragPhase.Move, pointer))], consumed: true, dispatcher: gesture.Dispatcher) with { Live = true };
        }

        return View(layout.Drag(gesture.Window, gesture.Step(PointerDragPhase.Move, pointer)), consumed: true, dispatcher: gesture.Dispatcher) with { Live = true };
    }

    private Outcome EndGesture(Point pointer)
    {
        var gesture = _gesture!;
        _gesture = null;
        // The Group under the pointer is found while the dragged window is still lifted, as in the pinned release.
        var drop = gesture.PickedUp && !gesture.Resize ? GroupDrop(gesture.Window, gesture.WasTiled, pointer) : null;
        IReadOnlyList<Placement> ended = gesture.PickedUp
            ? LayoutWorkspace(gesture.Window).Layout.Drag(gesture.Window, gesture.Step(PointerDragPhase.End, pointer))
            : [];
        if (drop is { } into)
            return JoinDragged(gesture.Window, into, gesture.Dispatcher);

        return View(ended, consumed: true, dispatcher: gesture.Dispatcher);
    }

    private readonly record struct DropTarget(LiveWorkspace Desk, WindowGroup Group, int? Index);

    // group:drag_into_group, as the pinned release ends a window drag. A window dropped on a Group's tab bar joins that
    // Group at the tab under the pointer, and one dropped on a member joins after the current member. A dragged Group
    // brings every member. A floating window does not join a tiled Group, except on its tab bar with
    // group:merge_floated_into_tiled_on_groupbar.
    private DropTarget? GroupDrop(WindowId dragged, bool wasTiled, Point pointer)
    {
        if (_groupOptions.DragIntoGroup == 0 || WorkspaceContaining(dragged) is not LiveWorkspace from)
            return null;

        var own = from.GroupOf(dragged);
        foreach (var bar in GroupBars(Snapshot()))
        {
            if (bar.Shown == dragged || !Inside(bar.Bounds, pointer))
                continue;

            if (WorkspaceContaining(bar.Shown) is not LiveWorkspace there || there.GroupOf(bar.Shown) is not WindowGroup target)
                break;

            var floatedIntoTiled = there.ClientOf(bar.Shown) is { Floating: false } && !wasTiled;
            if (!CanDragInto(from, dragged, target)
                || (floatedIntoTiled && !_groupOptions.MergeFloatedIntoTiledOnGroupbar)
                || (own is not null && !_groupOptions.MergeGroupsOnGroupbar))
                break;

            var relative = pointer.X - bar.Bounds.X - (bar.TabWidth / 2);
            var step = bar.TabWidth + bar.Style.GapsIn;
            var index = (relative < 0 || step <= 0 ? -1 : relative / step) + 1;
            return new DropTarget(there, target, index);
        }

        if (_groupOptions.DragIntoGroup != 1
            || WindowUnderExcept(pointer, dragged) is not WindowId under
            || WorkspaceContaining(under) is not LiveWorkspace home
            || home.GroupOf(under) is not WindowGroup group
            || (home.ClientOf(under) is { Floating: false } && !wasTiled)
            || !CanDragInto(from, dragged, group))
            return null;

        return new DropTarget(home, group, null);
    }

    // The pinned release's canBeGroupedInto for a drag: no lock on either Group or on all Groups, no rule that denies
    // the window, and a dragged Group only with group:merge_groups_on_drag.
    private bool CanDragInto(LiveWorkspace from, WindowId dragged, WindowGroup target)
    {
        var own = from.GroupOf(dragged);
        return !_groupsLocked
            && !target.Locked
            && own?.Locked != true
            && !target.Denied
            && own != target
            && CanJoin(RulesOf(dragged), target)
            && (own is null || _groupOptions.MergeGroupsOnDrag);
    }

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

            var position = index is int at ? at + i : _groupOptions.InsertAfterCurrent ? target.Current + 1 : target.Count;
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
        TakeGroupFocus(there, dragged);
        (_extraEvents ??= []).Add(new ClientIntoGroup(dragged));
        var outcome = Present(there, covered, there.Layout.Visible(), dispatcher: dispatcher, focus: dragged) with { Consumed = dispatcher is not null };
        return from != there && FindWorkspace(from.Id) is LiveWorkspace left && IsShown(left.Id)
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
            if (PointerFocus.At(targets, point, 0, null) is WindowId found)
                return found;
        }

        return null;
    }

    private static bool Inside(Rectangle box, Point point) =>
        point.X >= box.X && point.X < box.X + box.Width && point.Y >= box.Y && point.Y < box.Y + box.Height;

    // A window that closes ends any drag that holds it, as the pinned release ends its drag target on unmap.
    private void ForgetDrag(WindowId window)
    {
        if (_nativeMove == window)
            _nativeMove = null;
        if (_nativeSize == window)
            _nativeSize = null;
        if (_gesture?.Window == window)
            _gesture = null;
        if (_border?.Window == window)
            _border = null;
        if (_released?.Window == window)
            _released = null;
    }

    private static bool PastThreshold(Point origin, Point pointer, int threshold)
    {
        var dx = (long)pointer.X - origin.X;
        var dy = (long)pointer.Y - origin.Y;
        return (dx * dx) + (dy * dy) > (long)threshold * threshold;
    }

    // resizewindow 1 keeps the aspect ratio and 2 never keeps it. Without either, the window's keep_aspect_ratio rule or
    // property decides, as in the pinned release.
    private bool KeepsRatio(WindowId window, string arguments)
    {
        var mode = int.TryParse(arguments.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        return mode == 1 || (mode != 2 && OwnProp(window, "keep_aspect_ratio") is true);
    }

    // The Client a mouse bind takes: the one under the pointer, or else one whose border is under it, as the pinned
    // release finds the window with its reserved extents.
    private WindowId? DragTarget(Point? point)
    {
        if (point is not Point at)
            return null;

        if (WindowUnder(at, shrink: false) is WindowId inside)
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

            if (PointerFocus.At(grown, at, 0, null) is WindowId edge)
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
    {
        public WindowId Window { get; } = window;

        public string Button { get; } = button;

        public bool Resize { get; } = resize;

        public string Dispatcher { get; } = dispatcher;

        public Point Origin { get; } = origin;

        public bool KeepRatio { get; init; }

        public bool WasTiled { get; init; }

        public bool PickedUp { get; set; }

        public bool CenterOnLift { get; init; } = true;

        public PointerDrag Step(PointerDragPhase phase, Point pointer) => new(Resize, phase, pointer, KeepRatio, CenterOnLift);
    }
}
