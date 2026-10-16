namespace Winland;

internal sealed partial class DwindleLayout : IMouseDragHost
{
    private readonly MouseDrag _drag = new();

    FloatingClients IMouseDragHost.Floating => _floating;

    int IMouseDragHost.ResizeCorner => _options.ResizeCorner;

    public IReadOnlyList<Placement> Drag(WindowId window, PointerDrag drag) => _drag.Step(this, window, drag);

    public IReadOnlyList<Placement> PlaceFloating(WindowId window, Rectangle bounds)
    {
        var id = new ClientId(window.Value);
        return _floating.Place(id, bounds) ? WithFloating(id) : [];
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

        if (_nodes.TryGetValue(id, out var node) && InTree(node))
        {
            bounds = Present(id, node.Box);
            return true;
        }

        return false;
    }

    Rectangle IMouseDragHost.SnapFloating(ClientId self, Rectangle proposed, SnapEdges edges, bool resize) =>
        FloatingSnap.AmongClients(this, self, proposed, edges, resize, _area, _monitor, _options);

    bool IMouseDragHost.LiftTile(ClientId id, out Rectangle box)
    {
        box = default;
        if (!_nodes.TryGetValue(id, out var node) || !InTree(node))
            return false;

        box = Present(id, node.Box);
        var sibling = Detach(node);
        _nodes.Remove(id);
        if (_splitTarget == id)
            _splitTarget = sibling is null ? null : Leaf(sibling.Node).Id;

        MarkFloating(id, floating: true);
        _floating.Set(id, box);
        Refit();
        return true;
    }

    IReadOnlyList<Placement> IMouseDragHost.ResizeTile(ClientId id, int dx, int dy, (bool Left, bool Top) corner)
    {
        if (dx == 0 && dy == 0)
            return [];

        if (!_nodes.TryGetValue(id, out var node) || !InTree(node))
            return [];

        return ResizeTile(node, dx, dy, corner);
    }

    IReadOnlyList<Placement> IMouseDragHost.DropTile(ClientId id, Point pointer)
    {
        _floating.Remove(id);
        MarkFloating(id, floating: false);
        var node = new Node { Id = id };
        _nodes[id] = node;

        var target = LeafContaining(pointer) ?? ClosestLeaf(pointer);
        if (target is null || _root is null)
        {
            node.Box = TilingArea();
            node.Parent = null;
            _root = node;
            _splitTarget = id;
            Refit();
            return Placements();
        }

        Split(target, node, pointer, dropped: true);
        _splitTarget = id;
        return Placements();
    }

    IReadOnlyList<Placement> IMouseDragHost.ShowFloating(ClientId id) => WithFloating(id);

    IReadOnlyList<Placement> IMouseDragHost.EndInPlace() => [];

    private void MarkFloating(ClientId id, bool floating)
    {
        var index = _clients.FindIndex(client => client.Id == id);
        if (index >= 0)
            _clients[index] = _clients[index] with { Floating = floating };
    }

    private IReadOnlyList<Placement> WithFloating(ClientId id)
    {
        var placements = new List<Placement>(Placements());
        if (_floating.TryGet(id, out var box))
            placements.Add(new Placement(id, box));

        return placements;
    }
}
