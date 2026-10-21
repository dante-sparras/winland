namespace Winland.Core;

// Mouse moves and resizes in the monocle Layout, as Winland's: a floating Client follows the pointer or resizes
// from the grabbed corner, a tile is lifted out of the stack on its first move and dropped back as the shown tile, and a
// tile does not resize, since every tile fills the area.
internal sealed partial class MonocleLayout : IMouseDragHost, IDragOwner
{
    private readonly MouseDrag _drag = new();

    MouseDrag IDragOwner.Drag => _drag;

    FloatingClients IMouseDragHost.Floating => _floating;

    int IMouseDragHost.ResizeCorner => _options.ResizeCorner;

    public IReadOnlyList<Placement> Drag(WindowId window, PointerDrag drag) => _drag.Step(this, window, drag);

    bool IMouseDragHost.TryDragBounds(ClientId id, out Rectangle bounds, out bool floating)
    {
        bounds = default;
        floating = false;
        var index = IndexOf(id);
        if (index < 0 || _clients[index].Minimized)
            return false;

        if (_floating.TryGet(id, out bounds))
        {
            floating = true;
            return true;
        }

        if (VisibleTiled() != id)
            return false;

        bounds = Tile(id);
        return true;
    }

    Rectangle IMouseDragHost.SnapFloating(ClientId self, Rectangle proposed, SnapEdges edges, bool resize) =>
        FloatingSnap.AmongClients(this, self, proposed, edges, resize, _area, _area, _options);

    bool IMouseDragHost.LiftTile(ClientId id, out Rectangle box)
    {
        box = default;
        if (VisibleTiled() != id)
            return false;

        box = Tile(id);
        if (!DropTiled(id))
            return false;

        SetFloating(id, true);
        _floating.Set(id, box);
        return true;
    }

    IReadOnlyList<Placement> IMouseDragHost.ResizeTile(ClientId id, int dx, int dy, (bool Left, bool Top) corner) => [];

    IReadOnlyList<Placement> IMouseDragHost.DropTile(ClientId id, Point pointer)
    {
        _ = pointer;
        _floating.Remove(id);
        SetFloating(id, false);
        _tiled.Add(id);
        _visible = _tiled.Count - 1;
        _recent.Remove(id);
        _recent.Insert(0, id);
        return Placed();
    }

    IReadOnlyList<Placement> IMouseDragHost.ShowFloating(ClientId id)
    {
        var placements = new List<Placement>(Placed());
        if (_floating.TryGet(id, out var box))
            placements.Add(new Placement(id, box));

        return placements;
    }

    IReadOnlyList<Placement> IMouseDragHost.EndInPlace() => [];
}
