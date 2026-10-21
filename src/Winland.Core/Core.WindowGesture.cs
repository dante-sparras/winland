namespace Winland;

/// <summary>
/// The <c>move</c> and <c>resize</c> gestures, as Winland plays them on the focused window. A floating
/// window moves with the fingers, and any window resizes from the corner nearest the pointer, as a mouse drag would.
/// A tiled window shifts with the fingers, at most 100 pixels, and when they lift moves one place in the direction
/// they went.
/// </summary>
public sealed partial class Core
{
    // How far a tiled window shifts per unit of finger travel, and at most.
    private const double TiledShiftFactor = 0.5;
    private const double TiledShiftLimit = 100;

    private WindowGesture? _windowGesture;

    private Outcome BeginWindowGesture(TrackpadGesture gesture)
    {
        _windowGesture = null;
        if (ActiveWindow() is not WindowId window || Find(window) is not { } client || WorkspaceContaining(window) is not { } desk
            || !desk.Layout.TryBounds(window, out var slot))
            return View([]);

        var rest = PlacedBounds(client, desk, slot);
        var resize = gesture.IsResize;
        if (!resize && !client.Floating)
        {
            _windowGesture = new WindowGesture(window, Drags: false, rest, default);
            return View([]);
        }

        // A drag this gesture starts holds the pointer it moves, from where the real one is.
        var pointer = _pointer ?? new Point(rest.X + (rest.Width / 2), rest.Y + (rest.Height / 2));
        if (_gesture is not null)
            return View([]);

        var begun = BeginGesture(window, new BindDispatch(resize ? "resizewindow" : "movewindow", ""), GestureButton, pointer);
        _windowGesture = new WindowGesture(window, Drags: true, rest, pointer);
        return View(begun, focus: window);
    }

    private Outcome StepWindowGesture(double dx, double dy)
    {
        if (_windowGesture is not { } held || Find(held.Window) is null)
            return View([]);

        held.TravelX += dx;
        held.TravelY += dy;
        if (held.Drags)
        {
            if (_gesture?.Window != held.Window)
                return View([]);

            return MoveGesture(held.Pointer());
        }

        var (shiftX, shiftY) = held.Shift();
        var at = held.Rest with { X = held.Rest.X + shiftX, Y = held.Rest.Y + shiftY };
        return View([]) with { Motions = [new Motion(held.Window, at) { From = at, Warp = true, Rest = held.Rest }] };
    }

    private Outcome EndWindowGesture()
    {
        var held = _windowGesture;
        _windowGesture = null;
        if (held is null || Find(held.Window) is null)
            return View([]);

        if (held.Drags)
            return _gesture?.Window == held.Window && _gesture.Button == GestureButton ? EndGesture(held.Pointer()) : View([]);

        var (shiftX, shiftY) = held.Shift();
        var at = held.Rest with { X = held.Rest.X + shiftX, Y = held.Rest.Y + shiftY };
        if (Math.Sqrt((held.TravelX * held.TravelX) + (held.TravelY * held.TravelY)) < 0.1)
            return View([]) with { Motions = [new Motion(held.Window, held.Rest) { From = at, Warp = true }] };

        // The window moves one place, and its move animates from where the fingers left it.
        var direction = Math.Abs(held.TravelX) > Math.Abs(held.TravelY)
            ? held.TravelX > 0 ? "r" : "l"
            : held.TravelY > 0 ? "d" : "u";
        _swipeLanding = new Dictionary<WindowId, Rectangle> { [held.Window] = at };
        var moved = ExecuteTriggered("movewindow", direction, trigger: null);
        if (moved.Placements.Count > 0)
            return moved;

        // Nothing moved, so the window goes back where it was.
        _swipeLanding = null;
        return moved with { Motions = [new Motion(held.Window, held.Rest) { From = at, Warp = true }, .. moved.Motions] };
    }

    // The button name a drag started by a gesture is held under, which no mouse button has.
    private const string GestureButton = "gesture";

    private sealed class WindowGesture(WindowId window, bool Drags, Rectangle rest, Point origin)
    {
        public WindowId Window { get; } = window;

        /// <summary>The gesture drives a mouse drag, rather than shifting a tiled window.</summary>
        public bool Drags { get; } = Drags;

        public Rectangle Rest { get; } = rest;

        public double TravelX { get; set; }

        public double TravelY { get; set; }

        /// <summary>The pointer the drag follows: where the real one was, moved by the fingers.</summary>
        public Point Pointer() => new(origin.X + (int)Math.Round(TravelX), origin.Y + (int)Math.Round(TravelY));

        /// <summary>How far a tiled window is shifted now.</summary>
        public (int X, int Y) Shift() => (Clamped(TravelX), Clamped(TravelY));

        private static int Clamped(double travel) =>
            (int)Math.Round(Math.Clamp(travel * TiledShiftFactor, -TiledShiftLimit, TiledShiftLimit));
    }
}
