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
                if (WindowUnder(gesture.Position, shrink: false) is not WindowId window)
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
            KeepRatio = IsResize(drag.Dispatcher) && KeepsRatio(drag.Arguments),
            PickedUp = _binds.DragThreshold <= 0,
        };

        return _gesture.PickedUp ? layout.Drag(window, _gesture.Step(PointerDragPhase.Begin, pointer)) : [];
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
            _ = layout.Drag(gesture.Window, gesture.Step(PointerDragPhase.Begin, pointer));
        }

        return View(layout.Drag(gesture.Window, gesture.Step(PointerDragPhase.Move, pointer)), consumed: true, dispatcher: gesture.Dispatcher);
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

    private static bool PastThreshold(Point origin, Point pointer, int threshold)
    {
        var dx = (long)pointer.X - origin.X;
        var dy = (long)pointer.Y - origin.Y;
        return (dx * dx) + (dy * dy) > (long)threshold * threshold;
    }

    // resizewindow 1 keeps the aspect ratio. 2 never keeps it, which is also what no argument does,
    // because no window rule asks to keep the aspect ratio.
    private static bool KeepsRatio(string arguments) =>
        int.TryParse(arguments.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mode) && mode == 1;

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

        public PointerDrag Step(PointerDragPhase phase, Point pointer) => new(Resize, phase, pointer, KeepRatio);
    }
}
