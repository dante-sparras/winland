namespace Winland.Domain;

internal sealed partial class ScrollingLayout : IMouseDragHost, IDragOwner
{
    private readonly MouseDrag _drag = new();

    MouseDrag IDragOwner.Drag => _drag;

    FloatingClients IMouseDragHost.Floating => _floating;

    int IMouseDragHost.ResizeCorner => _layoutOptions.ResizeCorner;

    public IReadOnlyList<Placement> Drag(WindowId window, PointerDrag drag)
    {
        if (drag.Phase == PointerDragPhase.End)
            _dragging = false;

        return _drag.Step(this, window, drag);
    }

    public IReadOnlyList<Placement> PlaceFloating(WindowId window, Rectangle bounds)
    {
        var id = new ClientId(window.Value);
        return _floating.Place(id, bounds, _layoutOptions.FloatingOnScreen(_area)) ? WithFloating(id) : [];
    }

    Rectangle IMouseDragHost.SnapFloating(ClientId self, Rectangle proposed, SnapEdges edges, bool resize) =>
        FloatingSnap.AmongClients(this, self, proposed, edges, resize, _area, _monitor, _layoutOptions);

    bool IMouseDragHost.LiftTile(ClientId id, out Rectangle box)
    {
        box = default;
        if (!_cells.Remove(id, out var cell))
            return false;

        box = cell.Logical.Width > 0 ? cell.Logical : cell.Box;
        RemoveCell(cell);
        SetFloating(id, true);
        _floating.Set(id, box);
        Recalculate();
        return true;
    }

    IReadOnlyList<Placement> IMouseDragHost.ResizeTile(ClientId id, int dx, int dy, (bool Left, bool Top) corner)
    {
        if (dx == 0 && dy == 0)
            return [];

        if (Minimized(id) || !_cells.TryGetValue(id, out var cell))
            return [];

        _dragging = true;
        return ResizeCell(cell, dx, dy, corner);
    }

    IReadOnlyList<Placement> IMouseDragHost.ShowFloating(ClientId id) => WithFloating(id);

    IReadOnlyList<Placement> IMouseDragHost.EndInPlace()
    {
        Recalculate();
        return Placements();
    }

    IReadOnlyList<Placement> IMouseDragHost.DropTile(ClientId id, Point pointer)
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

    bool IMouseDragHost.TryDragBounds(ClientId id, out Rectangle bounds, out bool floating)
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
}
