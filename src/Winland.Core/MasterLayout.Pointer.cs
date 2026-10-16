namespace Winland;

internal sealed partial class MasterLayout
{
    private static readonly (string Word, Side Side)[] Directions =
    [
        ("l", Side.Left),
        ("left", Side.Left),
        ("r", Side.Right),
        ("right", Side.Right),
        ("u", Side.Up),
        ("up", Side.Up),
        ("t", Side.Up),
        ("top", Side.Up),
        ("d", Side.Down),
        ("down", Side.Down),
        ("b", Side.Down),
        ("bottom", Side.Down),
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

    private WindowId? Neighbor(Rectangle origin, Side side, WindowId except, bool floating) =>
        DirectionalNeighbor.Find(_clients, origin, side, except, floating, (WindowId window, out Rectangle box) => TryBox(window, out box, out _));

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
        if (!ResizeArguments.TryParse(arguments, out var x, out var y, out var exact))
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

        if (_floating.Remove(id, out var floating))
        {
            if (floating.Width > 0 && floating.Height > 0)
                _lastFloat[id] = (floating.Width, floating.Height);

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
        var floated = FloatingBounds(id, tileBox);
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

        // A Client tiled again while its floating drag runs is left to the stack.
        if (grab.WasFloating && !_floating.ContainsKey(grab.Id))
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

            if (TryBounds(client.Window, out var bounds))
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

    private Rectangle FloatingBounds(ClientId id, Rectangle tile)
    {
        var width = 640;
        var height = 400;
        if (_lastFloat.TryGetValue(id, out var last) && last.Width >= 5 && last.Height >= 5)
        {
            width = last.Width;
            height = last.Height;
        }

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

    private IReadOnlyList<Placement> MoveFloating(WindowId window, Rectangle box, Side direction)
    {
        var frame = _layout.FloatingWorkArea(_area);
        var moved = direction switch
        {
            Side.Left => box with { X = frame.X },
            Side.Right => box with { X = frame.X + frame.Width - box.Width },
            Side.Up => box with { Y = frame.Y },
            Side.Down => box with { Y = frame.Y + frame.Height - box.Height },
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

    private static bool TryDirection(string text, out Side direction)
    {
        foreach (var (word, parsed) in Directions)
        {
            if (text.Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                direction = parsed;
                return true;
            }
        }

        direction = Side.Left;
        return false;
    }

    private static string First(string arguments)
    {
        var tokens = Tokens(arguments);
        return tokens.Length == 0 ? "" : tokens[0];
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
