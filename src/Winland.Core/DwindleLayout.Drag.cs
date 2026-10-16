namespace Winland;

internal sealed partial class DwindleLayout
{
    private MouseGrab? _grab;

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

        var (left, top) = BorderPointer.GrabbedCorner(_options.ResizeCorner, floating, pointer, bounds);
        _grab = new MouseGrab(id, resize, floating, pointer, bounds, left, top) { KeepRatio = keepRatio };
        return [];
    }

    private IReadOnlyList<Placement> MoveDrag(Point pointer, bool resize)
    {
        if (_grab is not MouseGrab grab || grab.Resize != resize)
            return [];

        if (resize)
            return grab.WasFloating ? ResizeFloating(grab, pointer) : ResizeTiled(grab, pointer);

        if (!grab.WasFloating && !grab.Lifted && !Lift(grab, pointer))
            return [];

        return Follow(grab, pointer);
    }

    private IReadOnlyList<Placement> EndDrag(Point pointer)
    {
        if (_grab is not MouseGrab grab)
            return [];

        _grab = null;
        if (grab.Lifted && _floating.ContainsKey(grab.Id))
            return DropTiled(grab.Id, pointer);

        if (grab.WasFloating && _floating.ContainsKey(grab.Id))
            return WithFloating(grab.Id);

        return [];
    }

    private bool Lift(MouseGrab grab, Point pointer)
    {
        if (!_nodes.TryGetValue(grab.Id, out var node) || !InTree(node))
            return false;

        var box = Present(grab.Id, node.Box);
        var sibling = Detach(node);
        _nodes.Remove(grab.Id);
        if (_splitTarget == grab.Id)
            _splitTarget = sibling is null ? null : Leaf(sibling.Node).Id;

        MarkFloating(grab.Id, floating: true);
        _floating[grab.Id] = box;
        grab.Lifted = true;
        grab.Start = box;
        grab.HotX = box.Width / 2;
        grab.HotY = box.Height / 2;
        Refit();
        return true;
    }

    private IReadOnlyList<Placement> Follow(MouseGrab grab, Point pointer)
    {
        var next = new Rectangle(pointer.X - grab.HotX, pointer.Y - grab.HotY, grab.Start.Width, grab.Start.Height);
        next = SnapFloating(grab.Id, next, SnapEdges.All, resize: false);
        _floating[grab.Id] = next;
        return WithFloating(grab.Id);
    }

    private IReadOnlyList<Placement> ResizeFloating(MouseGrab grab, Point pointer)
    {
        var resized = BorderPointer.Resize(grab.Start, grab.Grab, pointer, grab.Left, grab.Top, grab.KeepRatio);
        var next = SnapFloating(grab.Id, resized, SnapEdges.FromGrab(grab.Left, grab.Top), resize: true);
        _floating[grab.Id] = next;
        return WithFloating(grab.Id);
    }

    public IReadOnlyList<Placement> PlaceFloating(WindowId window, Rectangle bounds)
    {
        var id = new ClientId(window.Value);
        if (!_floating.ContainsKey(id) || bounds.Width < 1 || bounds.Height < 1)
            return [];

        _floating[id] = bounds;
        return WithFloating(id);
    }

    private Rectangle SnapFloating(ClientId self, Rectangle proposed, SnapEdges edges, bool resize)
    {
        if (!_options.SnapEnabled)
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
        return FloatingSnap.Apply(proposed, edges, resize, others, _area, monitor, _options);
    }

    private IReadOnlyList<Placement> ResizeTiled(MouseGrab grab, Point pointer)
    {
        var dx = pointer.X - grab.Last.X;
        var dy = pointer.Y - grab.Last.Y;
        grab.Last = pointer;
        if (dx == 0 && dy == 0)
            return [];

        if (!_nodes.TryGetValue(grab.Id, out var node) || !InTree(node))
            return [];

        return ResizeTile(node, dx, dy, (grab.Left, grab.Top));
    }

    private IReadOnlyList<Placement> DropTiled(ClientId id, Point pointer)
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

    private bool TryDragBounds(ClientId id, out Rectangle bounds, out bool floating)
    {
        bounds = default;
        floating = false;
        var index = _clients.FindIndex(client => client.Id == id);
        if (index < 0 || _clients[index].Minimized)
            return false;

        if (_floating.TryGetValue(id, out bounds))
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

    private void MarkFloating(ClientId id, bool floating)
    {
        var index = _clients.FindIndex(client => client.Id == id);
        if (index >= 0)
            _clients[index] = _clients[index] with { Floating = floating };
    }

    private IReadOnlyList<Placement> WithFloating(ClientId id)
    {
        var placements = new List<Placement>(Placements());
        if (_floating.TryGetValue(id, out var box))
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
