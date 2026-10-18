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
        IReadOnlyList<Placement> ended = gesture.PickedUp
            ? LayoutWorkspace(gesture.Window).Layout.Drag(gesture.Window, gesture.Step(PointerDragPhase.End, pointer))
            : [];
        return View(ended, consumed: true, dispatcher: gesture.Dispatcher);
    }

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

        public bool PickedUp { get; set; }

        public bool CenterOnLift { get; init; } = true;

        public PointerDrag Step(PointerDragPhase phase, Point pointer) => new(Resize, phase, pointer, KeepRatio, CenterOnLift);
    }
}
