namespace Winland;

internal sealed partial class ScrollingLayout
{
    public IReadOnlyList<Placement> Drag(WindowId window, PointerDrag drag)
    {
        return drag.Phase switch
        {
            PointerDragPhase.Begin => BeginDrag(window, drag.Pointer, drag.Resize, drag.KeepRatio),
            PointerDragPhase.Move => MoveDrag(drag.Pointer, drag.Resize),
            PointerDragPhase.End => EndDrag(drag.Pointer),
            _ => [],
        };
    }

    private IReadOnlyList<Placement> BeginDrag(WindowId window, Point pointer, bool resize, bool keepRatio)
    {
        var id = new ClientId(window.Value);
        if (!TryDragBounds(id, out var bounds, out var floating))
        {
            _grab = null;
            return [];
        }

        var (left, top) = BorderPointer.GrabbedCorner(_layoutOptions.ResizeCorner, floating, pointer, bounds);
        _grab = new MouseGrab(id, resize, floating, pointer, bounds, left, top) { KeepRatio = keepRatio };
        return [];
    }

    private IReadOnlyList<Placement> MoveDrag(Point pointer, bool resize)
    {
        if (_grab is not MouseGrab grab || grab.Resize != resize)
            return [];

        // A Client tiled again while its floating drag runs is left to the tape.
        if (grab.WasFloating && !_floating.Contains(grab.Id))
            return [];

        if (resize)
            return grab.WasFloating ? ResizeDraggedFloating(grab, pointer) : ResizeDraggedTiled(grab, pointer);

        if (!grab.WasFloating && !grab.Lifted && !Lift(grab))
            return [];

        return Follow(grab, pointer);
    }

    private IReadOnlyList<Placement> EndDrag(Point pointer)
    {
        _dragging = false;
        if (_grab is not MouseGrab grab)
            return [];

        _grab = null;
        if (grab.Lifted && _floating.Contains(grab.Id))
            return DropTiled(grab.Id, pointer);

        if (grab.WasFloating && _floating.Contains(grab.Id))
            return WithFloating(grab.Id);

        Recalculate();
        return Placements();
    }

    private bool Lift(MouseGrab grab)
    {
        if (!_cells.Remove(grab.Id, out var cell))
            return false;

        var box = cell.Logical.Width > 0 ? cell.Logical : cell.Box;
        RemoveCell(cell);
        SetFloating(grab.Id, true);
        _floating.Set(grab.Id, box);
        grab.Lifted = true;
        grab.Start = box;
        grab.HotX = box.Width / 2;
        grab.HotY = box.Height / 2;
        Recalculate();
        return true;
    }

    private IReadOnlyList<Placement> Follow(MouseGrab grab, Point pointer)
    {
        var next = new Rectangle(pointer.X - grab.HotX, pointer.Y - grab.HotY, grab.Start.Width, grab.Start.Height);
        next = SnapFloating(grab.Id, next, SnapEdges.All, resize: false);
        _floating.Set(grab.Id, next);
        return WithFloating(grab.Id);
    }

    private IReadOnlyList<Placement> ResizeDraggedFloating(MouseGrab grab, Point pointer)
    {
        var resized = BorderPointer.Resize(grab.Start, grab.Grab, pointer, grab.Left, grab.Top, grab.KeepRatio);
        var next = SnapFloating(grab.Id, resized, SnapEdges.FromGrab(grab.Left, grab.Top), resize: true);
        _floating.Set(grab.Id, next);
        return WithFloating(grab.Id);
    }

    public IReadOnlyList<Placement> PlaceFloating(WindowId window, Rectangle bounds)
    {
        var id = new ClientId(window.Value);
        return _floating.Place(id, bounds) ? WithFloating(id) : [];
    }

    private Rectangle SnapFloating(ClientId self, Rectangle proposed, SnapEdges edges, bool resize)
    {
        if (!_layoutOptions.SnapEnabled)
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
        return FloatingSnap.Apply(proposed, edges, resize, others, _area, monitor, _layoutOptions);
    }

    private IReadOnlyList<Placement> ResizeDraggedTiled(MouseGrab grab, Point pointer)
    {
        var dx = pointer.X - grab.Last.X;
        var dy = pointer.Y - grab.Last.Y;
        grab.Last = pointer;
        if (dx == 0 && dy == 0)
            return [];

        if (Minimized(grab.Id) || !_cells.TryGetValue(grab.Id, out var cell))
            return [];

        _dragging = true;
        return ResizeCell(cell, dx, dy, (grab.Left, grab.Top));
    }

    private IReadOnlyList<Placement> DropTiled(ClientId id, Point pointer)
    {
        _floating.Remove(id);
        SetFloating(id, false);
        var window = new WindowId(id.Value);
        var cell = new Cell(id);
        _cells[id] = cell;
        var target = ColumnAt(pointer);
        if (target is null)
        {
            var created = Append(null);
            AddCell(created, cell);
            FitColumn(created);
        }
        else if (OnEdge(target, pointer, out var before))
        {
            var index = _columns.IndexOf(target);
            var created = InsertAt(before ? index : index + 1, null);
            AddCell(created, cell);
            FitColumn(created);
        }
        else
        {
            AddCell(target, cell, IndexForHeight(target, pointer.Y));
            FitColumn(target);
        }

        _focus = window;
        Recalculate();
        return Placements();
    }

    private Column? ColumnAt(Point pointer)
    {
        Column? closest = null;
        var best = double.MaxValue;
        foreach (var column in _columns)
        {
            if (column.Cells.Count == 0)
                continue;

            var box = column.Cells[0].Logical;
            foreach (var cell in column.Cells)
            {
                if (Intersects(cell.Logical, new Rectangle(pointer.X, pointer.Y, 1, 1)))
                    return column;
            }

            var dx = pointer.X < box.X ? box.X - pointer.X : pointer.X >= box.X + box.Width ? pointer.X - (box.X + box.Width) : 0;
            var dy = pointer.Y < box.Y ? box.Y - pointer.Y : pointer.Y >= box.Y + box.Height ? pointer.Y - (box.Y + box.Height) : 0;
            var distance = (dx * (double)dx) + (dy * (double)dy);
            if (distance < best)
            {
                best = distance;
                closest = column;
            }
        }

        return closest;
    }

    private bool OnEdge(Column column, Point pointer, out bool before)
    {
        before = false;
        if (column.Cells.Count == 0)
            return false;

        var box = column.Cells[0].Logical;
        double origin;
        double size;
        double point;
        if (IsHorizontal())
        {
            origin = box.X;
            size = box.Width;
            point = pointer.X;
        }
        else
        {
            origin = box.Y;
            size = box.Height;
            point = pointer.Y;
        }

        if (size <= 0)
            return false;

        before = point <= origin + (size * 0.3);
        return before || point >= origin + (size * 0.7);
    }

    private bool TryDragBounds(ClientId id, out Rectangle bounds, out bool floating)
    {
        bounds = default;
        floating = false;
        var index = _clients.FindIndex(client => client.Id == id);
        if (index < 0 || _clients[index].Minimized)
            return false;

        if (_floating.TryGet(id, out bounds))
        {
            floating = true;
            return true;
        }

        if (_cells.TryGetValue(id, out var cell))
        {
            bounds = cell.Logical.Width > 0 ? cell.Logical : cell.Box;
            return bounds.Width > 0 && bounds.Height > 0;
        }

        return false;
    }

    private IReadOnlyList<Placement> WithFloating(ClientId id)
    {
        var placements = new List<Placement>(Placements());
        if (_floating.TryGet(id, out var box))
            placements.Add(new Placement(id, box));
        return placements;
    }

    private sealed class MouseGrab
    {
        public MouseGrab(ClientId id, bool resize, bool wasFloating, Point grab, Rectangle start, bool left, bool top)
        {
            Id = id;
            Resize = resize;
            WasFloating = wasFloating;
            Grab = grab;
            Last = grab;
            Start = start;
            Left = left;
            Top = top;
            if (wasFloating)
            {
                HotX = grab.X - start.X;
                HotY = grab.Y - start.Y;
            }
        }

        public ClientId Id { get; }

        public WindowId Window => new(Id.Value);

        public bool Resize { get; }

        public bool WasFloating { get; }

        public Point Grab { get; }

        public Point Last { get; set; }

        public Rectangle Start { get; set; }

        public bool Left { get; }

        public bool Top { get; }

        public bool Lifted { get; set; }

        public bool KeepRatio { get; init; }

        public int HotX { get; set; }

        public int HotY { get; set; }
    }
}
