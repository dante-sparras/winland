using System.Globalization;
using static Winland.OrdinalText;

namespace Winland;

internal sealed partial class MasterLayout
{
    private static readonly (string Word, Direction Direction)[] Directions =
    [
        ("l", Direction.Left),
        ("left", Direction.Left),
        ("r", Direction.Right),
        ("right", Direction.Right),
        ("u", Direction.Up),
        ("up", Direction.Up),
        ("t", Direction.Up),
        ("top", Direction.Up),
        ("d", Direction.Down),
        ("down", Direction.Down),
        ("b", Direction.Down),
        ("bottom", Direction.Down),
    ];

    private MouseGrab? _grab;

    public WindowId? MoveFocus(WindowId active, string arguments)
    {
        if (!TryDirection(First(arguments), out var direction))
            return null;
        if (!TryBox(active, out var origin, out var floating))
            return null;

        return Neighbor(origin, direction, active, floating)
            ?? Neighbor(origin, direction, active, !floating);
    }

    public void NoteFocus(WindowId window)
    {
        if (FindClient(Id(window)) >= 0)
            _focused = window;
    }

    public IReadOnlyList<Placement> MoveWindow(WindowId window, string arguments)
    {
        if (!TryDirection(First(arguments), out var direction))
            return [];
        if (Minimized(Id(window)))
            return [];

        if (_floating.TryGetValue(Id(window), out var floating))
            return MoveFloating(window, floating, direction);

        var tile = Find(window);
        if (tile is null)
            return [];

        var neighbor = Neighbor(tile.Box, direction, window, floating: false);
        if (neighbor is not WindowId other || Find(other) is not Tile destination)
            return [];

        SwapIds(tile, destination);
        Recalculate();
        return Placed();
    }

    public IReadOnlyList<Placement> ResizeActive(WindowId window, string arguments)
    {
        if (!TryResize(arguments, out var x, out var y, out var exact))
            return [];
        if (Minimized(Id(window)))
            return [];

        if (_floating.TryGetValue(Id(window), out var floating))
            return ResizeFloating(window, floating, x, y, exact);

        var tile = Find(window);
        if (tile is null)
            return [];

        // exact names the window's size, which is the tile less its gaps and border.
        var shown = Present(tile.Box);
        var dx = exact ? x - shown.Width : x;
        var dy = exact ? y - shown.Height : y;
        ResizeTile(tile, dx, dy);
        return Placed();
    }

    public IReadOnlyList<Placement> ToggleFloating(WindowId window)
    {
        var id = Id(window);
        var index = FindClient(id);
        if (index < 0 || _clients[index].Minimized)
            return [];

        if (_floating.ContainsKey(id))
        {
            _floating.Remove(id);
            MarkFloating(id, false);
            var tile = new Tile { Id = id, MasterFraction = _master.Mfact };
            Adopt(tile, firstMap: true, dragging: false, pointer: null);
            _focused = window;
            return Visible();
        }

        var existing = Find(window);
        if (existing is null)
            return [];

        var tileBox = existing.Box;
        Detach(existing);
        var floated = FloatingBounds(tileBox);
        _floating[id] = floated;
        MarkFloating(id, true);
        var placements = new List<Placement>(Placed()) { new(id, floated) };
        return placements;
    }

    public IReadOnlyList<Placement> Drag(WindowId window, PointerDrag drag) =>
        drag.Phase switch
        {
            PointerDragPhase.Begin => BeginDrag(window, drag.Pointer, drag.Resize, drag.KeepRatio),
            PointerDragPhase.Move => MoveDrag(drag.Pointer, drag.Resize),
            PointerDragPhase.End => EndDrag(drag.Pointer),
            _ => [],
        };

    private IReadOnlyList<Placement> BeginDrag(WindowId window, Point pointer, bool resize, bool keepRatio)
    {
        var id = Id(window);
        if (!TryBox(window, out var bounds, out var floating) || Minimized(id))
        {
            _grab = null;
            return [];
        }

        var (left, top) = BorderPointer.GrabbedCorner(_layout.ResizeCorner, floating, pointer, bounds);
        _grab = new MouseGrab(id, window, resize, floating, pointer, bounds)
        {
            HotX = pointer.X - bounds.X,
            HotY = pointer.Y - bounds.Y,
            Left = left,
            Top = top,
            KeepRatio = keepRatio,
        };
        return [];
    }

    private IReadOnlyList<Placement> MoveDrag(Point pointer, bool resize)
    {
        if (_grab is not MouseGrab grab || grab.Resize != resize)
            return [];

        var dx = pointer.X - grab.Last.X;
        var dy = pointer.Y - grab.Last.Y;
        grab.Last = pointer;
        if (resize)
        {
            if (grab.WasFloating && _floating.ContainsKey(grab.Id))
            {
                var resized = BorderPointer.Resize(grab.Start, grab.Grab, pointer, grab.Left, grab.Top, grab.KeepRatio);
                resized = SnapFloating(grab.Id, resized, SnapEdges.FromGrab(grab.Left, grab.Top), resize: true);
                _floating[grab.Id] = resized;
                return Visible();
            }

            var tile = Find(grab.Window);
            if (tile is null)
                return [];

            ResizeTile(tile, dx, dy, (grab.Left, grab.Top));
            return Placed();
        }

        if (!grab.WasFloating && !grab.Lifted)
            Lift(grab);

        if (!_floating.ContainsKey(grab.Id))
            return [];

        var next = new Rectangle(pointer.X - grab.HotX, pointer.Y - grab.HotY, grab.Start.Width, grab.Start.Height);
        next = SnapFloating(grab.Id, next, SnapEdges.All, resize: false);
        _floating[grab.Id] = next;
        return Visible();
    }

    public IReadOnlyList<Placement> PlaceFloating(WindowId window, Rectangle bounds)
    {
        var id = Id(window);
        if (!_floating.ContainsKey(id) || bounds.Width < 1 || bounds.Height < 1)
            return [];

        _floating[id] = bounds;
        return Visible();
    }

    private Rectangle SnapFloating(ClientId self, Rectangle proposed, SnapEdges edges, bool resize)
    {
        if (!_layout.SnapEnabled)
            return proposed;

        var others = new List<Rectangle>();
        foreach (var client in _clients)
        {
            if (client.Id == self || client.Minimized)
                continue;

            if (TryBox(client.Window, out var bounds, out _))
                others.Add(bounds);
        }

        var monitor = _monitor.Width > 0 && _monitor.Height > 0 ? _monitor : _area;
        return FloatingSnap.Apply(proposed, edges, resize, others, _area, monitor, _layout);
    }

    private IReadOnlyList<Placement> EndDrag(Point pointer)
    {
        if (_grab is not MouseGrab grab)
            return [];

        _grab = null;
        if (!grab.Lifted || !_floating.ContainsKey(grab.Id))
        {
            if (grab.WasFloating && _floating.ContainsKey(grab.Id))
                return Visible();

            return [];
        }

        _floating.Remove(grab.Id);
        MarkFloating(grab.Id, false);
        var tile = new Tile { Id = grab.Id, MasterFraction = _master.Mfact };
        Adopt(tile, firstMap: false, dragging: true, pointer: pointer);
        _focused = grab.Window;
        return Visible();
    }

    private void Lift(MouseGrab grab)
    {
        var tile = Find(grab.Window);
        if (tile is null)
            return;

        var box = Present(tile.Box);
        Detach(tile);
        MarkFloating(grab.Id, true);
        _floating[grab.Id] = box;
        grab.Lifted = true;
        grab.Start = box;
        grab.HotX = box.Width / 2;
        grab.HotY = box.Height / 2;
    }

    private Rectangle FloatingBounds(Rectangle tile)
    {
        var width = 640;
        var height = 400;
        if (Math.Abs(width - tile.Width) < 5 && Math.Abs(height - tile.Height) < 5)
        {
            width += 10;
            height += 10;
        }

        var floated = new Rectangle(
            tile.X + (tile.Width / 2) - (width / 2),
            tile.Y + (tile.Height / 2) - (height / 2),
            width,
            height);
        return FitInWorkArea(floated, _layout.FloatingWorkArea(_area));
    }

    private IReadOnlyList<Placement> MoveFloating(WindowId window, Rectangle box, Direction direction)
    {
        var frame = _layout.FloatingWorkArea(_area);
        var moved = direction switch
        {
            Direction.Left => box with { X = frame.X },
            Direction.Right => box with { X = frame.X + frame.Width - box.Width },
            Direction.Up => box with { Y = frame.Y },
            Direction.Down => box with { Y = frame.Y + frame.Height - box.Height },
            _ => box,
        };
        if (moved == box)
            return [];

        _floating[Id(window)] = moved;
        return [new Placement(Id(window), moved)];
    }

    private IReadOnlyList<Placement> ResizeFloating(WindowId window, Rectangle box, int x, int y, bool exact)
    {
        var deltaX = exact ? x - box.Width : x;
        var deltaY = exact ? y - box.Height : y;
        var resized = new Rectangle(box.X - (deltaX / 2), box.Y - (deltaY / 2), box.Width + deltaX, box.Height + deltaY);
        if (resized.Width < 1 || resized.Height < 1)
            return [];

        _floating[Id(window)] = resized;
        return [new Placement(Id(window), resized)];
    }

    private bool TryBox(WindowId window, out Rectangle box, out bool floating)
    {
        var id = Id(window);
        if (_floating.TryGetValue(id, out box))
        {
            floating = true;
            return true;
        }

        floating = false;
        var tile = Find(window);
        if (tile is null)
        {
            box = default;
            return false;
        }

        box = tile.Box;
        return true;
    }

    private WindowId? Neighbor(Rectangle origin, Direction direction, WindowId except, bool floating)
    {
        WindowId? best = null;
        var bestOverlap = 1.0;
        foreach (var client in _clients)
        {
            if (client.Window == except || client.Minimized || client.Floating != floating)
                continue;
            if (!TryBox(client.Window, out var box, out _))
                continue;
            if (!Overlap(origin, box, direction, out var overlap) || overlap <= bestOverlap)
                continue;

            bestOverlap = overlap;
            best = client.Window;
        }

        return best;
    }

    private static bool Overlap(Rectangle origin, Rectangle other, Direction direction, out double overlap)
    {
        var adjacent = direction switch
        {
            Direction.Left => Adjacent(origin.X, origin.X + origin.Width, other.X, other.X + other.Width),
            Direction.Right => Adjacent(other.X, other.X + other.Width, origin.X, origin.X + origin.Width),
            Direction.Up => Adjacent(origin.Y, origin.Y + origin.Height, other.Y, other.Y + other.Height),
            Direction.Down => Adjacent(other.Y, other.Y + other.Height, origin.Y, origin.Y + origin.Height),
            _ => false,
        };
        overlap = adjacent
            ? direction is Direction.Left or Direction.Right
                ? Shared(origin.Y, origin.Height, other.Y, other.Height)
                : Shared(origin.X, origin.Width, other.X, other.Width)
            : 0;
        return overlap > 1;
    }

    private static bool Adjacent(int aMin, int aMax, int bMin, int bMax)
    {
        var delta = aMin - bMax;
        if (Math.Abs(delta) <= 2)
            return true;
        if (delta >= 0)
            return false;

        var overlap = -delta;
        var sizeA = aMax - aMin;
        var sizeB = bMax - bMin;
        if ((bMin <= aMin && bMax >= aMax) || (aMin <= bMin && aMax >= bMax))
            return false;

        return overlap <= Math.Min(sizeA, sizeB) * 0.5;
    }

    private static double Shared(int start, int size, int otherStart, int otherSize) =>
        Math.Max(0, Math.Min(start + size, otherStart + otherSize) - Math.Max(start, otherStart));

    private static Rectangle FitInWorkArea(Rectangle box, Rectangle work)
    {
        var x = Math.Max(box.X, work.X);
        var y = Math.Max(box.Y, work.Y);
        if (x + box.Width > work.X + work.Width)
            x = work.X + work.Width - box.Width;
        if (y + box.Height > work.Y + work.Height)
            y = work.Y + work.Height - box.Height;

        return new Rectangle(x, y, box.Width, box.Height);
    }

    private static bool Sticks(int edge, int other) => Math.Abs(edge - other) <= 1;

    private static bool TryDirection(string text, out Direction direction)
    {
        foreach (var (word, parsed) in Directions)
        {
            if (text.Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                direction = parsed;
                return true;
            }
        }

        direction = Direction.Left;
        return false;
    }

    private static bool TryResize(string arguments, out int x, out int y, out bool exact)
    {
        x = 0;
        y = 0;
        exact = false;
        var tokens = Tokens(arguments);
        var index = 0;
        if (tokens.Length > 0 && Is(tokens[0], "exact"))
        {
            exact = true;
            index = 1;
        }

        if (tokens.Length - index < 2)
            return false;

        return int.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
            && int.TryParse(tokens[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
    }

    private static string First(string arguments)
    {
        var tokens = Tokens(arguments);
        return tokens.Length == 0 ? "" : tokens[0];
    }

    private enum Direction
    {
        Left,
        Right,
        Up,
        Down,
    }

    private sealed class MouseGrab(ClientId id, WindowId window, bool resize, bool wasFloating, Point grab, Rectangle start)
    {
        public ClientId Id { get; } = id;

        public WindowId Window { get; } = window;

        public bool Resize { get; } = resize;

        public bool WasFloating { get; } = wasFloating;

        public bool Lifted { get; set; }

        public Point Grab { get; } = grab;

        public Point Last { get; set; } = grab;

        public Rectangle Start { get; set; } = start;

        public int HotX { get; set; }

        public int HotY { get; set; }

        public bool Left { get; init; }

        public bool Top { get; init; }

        public bool KeepRatio { get; init; }
    }
}
