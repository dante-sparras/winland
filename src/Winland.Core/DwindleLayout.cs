namespace Winland.Core;

internal sealed partial class DwindleLayout : Layout
{
    private readonly List<Client> _clients = [];
    private readonly Dictionary<ClientId, Node> _nodes = [];
    private readonly Dictionary<ClientId, Suspension> _suspended = [];
    private readonly FloatingClients _floating = new();
    private readonly Dictionary<ClientId, Rectangle> _natural = [];
    private readonly PseudoTiles _pseudo = new();
    private LayoutOptions _options = LayoutOptions.Unconfigured;
    private PendingSplit _preselect;
    private Node? _root;
    private ClientId? _splitTarget;
    private Rectangle _area;
    private Rectangle _monitor;
    private bool _activeWorkspace = true;

    public IReadOnlyList<Client> Clients => _clients;

    public void Retitle(WindowId window, string title)
    {
        for (var i = 0; i < _clients.Count; i++)
        {
            if (_clients[i].Window == window)
                _clients[i] = _clients[i] with { Title = title };
        }
    }

    public void Reclass(WindowId window, string windowClass)
    {
        for (var i = 0; i < _clients.Count; i++)
        {
            if (_clients[i].Window == window)
                _clients[i] = _clients[i] with { Class = windowClass };
        }
    }

    public void Readdress(WorkspaceId workspace)
    {
        for (var i = 0; i < _clients.Count; i++)
            _clients[i] = _clients[i] with { Workspace = workspace };
    }

    public void NudgeFloating(int dx, int dy) => _floating.Nudge(dx, dy);

    public IReadOnlyList<Placement> Apply(LayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        Refit();
        return Placements();
    }

    public void Fit(Rectangle area)
    {
        _area = area;
        Refit();
    }

    public IReadOnlyList<Placement> SetFullscreen(WindowId window, FullscreenMode mode, FullscreenMode clientMode)
    {
        if (!FullscreenClients.Assign(_clients, window, mode, clientMode))
            return [];

        if (!FullscreenClients.Covering(_clients))
            Refit();

        return Visible();
    }

    public IReadOnlyList<Placement> Current() => Placements();

    public bool TryBounds(WindowId window, out Rectangle bounds)
    {
        var id = new ClientId(window.Value);
        if (_floating.TryGet(id, out bounds))
            return true;

        if (_nodes.TryGetValue(id, out var node))
        {
            bounds = Present(id, node.Box);
            return true;
        }

        if (_suspended.TryGetValue(id, out var suspension))
        {
            bounds = suspension.Node.Box;
            return true;
        }

        bounds = default;
        return false;
    }

    public IReadOnlyList<Placement> Preselect(string direction)
    {
        ArgumentNullException.ThrowIfNull(direction);
        if (direction.Length > 0)
            _preselect = ParseDirection(direction);
        return [];
    }

    public IReadOnlyList<Placement> TogglePseudo(WindowId window)
    {
        var id = new ClientId(window.Value);
        if (!_clients.Exists(client => client.Id == id))
            return [];

        _pseudo.Toggle(id);

        return Placements();
    }

    public IReadOnlyList<Placement> Add(
        WindowId window,
        Rectangle area,
        Point? pointer,
        bool floating,
        Rectangle bounds,
        WorkspaceId workspace,
        string windowClass = "",
        string title = "",
        bool activeWorkspace = true,
        Rectangle monitor = default,
        long processId = 0,
        string initialClass = "",
        string initialTitle = "",
        Size? minSize = null,
        Size? maxSize = null,
        TiledLimits? tiledLimits = null)
    {
        _area = area;
        _monitor = monitor;
        _activeWorkspace = activeWorkspace;
        var id = new ClientId(window.Value);
        if (_nodes.ContainsKey(id) || _suspended.ContainsKey(id) || _floating.Contains(id))
            return [];

        _limits[id] = new SizeLimits(minSize, maxSize) { Tiled = tiledLimits };
        _natural[id] = bounds;
        var placed = new SizeLimits(minSize, maxSize).Clamp(bounds);
        var overflows = !floating && CannotFill(maxSize, pointer);
        if (overflows)
            placed = FloatedFromTile(new SizeLimits(minSize, maxSize), pointer);

        _floating.Remember(id, placed);
        var floats = floating || overflows;
        var client = ClientAdoption.Adopted(id, window, workspace, windowClass, title, floats, processId, initialClass, initialTitle);
        if (floats)
        {
            _clients.Add(client);
            _floating.Set(id, placed);
            return [new Placement(id, placed)];
        }

        var node = new Node { Id = client.Id };
        _clients.Add(client);
        _nodes.Add(client.Id, node);

        var opening = Opening(pointer);
        if (opening is null && _root is null)
        {
            node.Box = TilingArea();
            _root = node;
            Refit();
        }
        else
        {
            Split(opening ?? Leaf(_root!), node, pointer);
        }

        _splitTarget = client.Id;
        return Placements();
    }

    public IReadOnlyList<Placement> Remove(WindowId window)
    {
        var id = new ClientId(window.Value);
        _pseudo.Forget(id);
        _natural.Remove(id);
        _limits.Remove(id);
        _drag.Forget(id);

        if (_floating.Forget(id) || _suspended.Remove(id))
        {
            _clients.RemoveAll(client => client.Id == id);
            return [];
        }

        if (!_nodes.Remove(id, out var node))
            return [];

        _clients.RemoveAll(client => client.Id == id);
        var sibling = Detach(node);
        if (_splitTarget == id)
            _splitTarget = sibling is null ? null : Leaf(sibling.Node).Id;

        Refit();
        return Placements();
    }

    public IReadOnlyList<Placement> Minimize(WindowId window)
    {
        var id = new ClientId(window.Value);
        if (_floating.Contains(id))
        {
            SetMinimized(id, true);
            return [];
        }

        if (!_nodes.Remove(id, out var node))
            return [];

        SetMinimized(id, true);
        var sibling = Detach(node);
        _suspended[id] = new Suspension(node, sibling?.Node, sibling?.Index ?? 0, sibling?.SplitRatio ?? 1, sibling?.SplitTop ?? false);
        if (_splitTarget == id)
            _splitTarget = sibling is null ? null : Leaf(sibling.Node).Id;

        Refit();
        return Placements();
    }

    public IReadOnlyList<Placement> Restore(WindowId window)
    {
        var id = new ClientId(window.Value);
        if (_floating.TryGet(id, out var bounds))
        {
            var index = _clients.FindIndex(client => client.Id == id);
            if (index < 0 || !_clients[index].Minimized)
                return [];

            SetMinimized(id, false);
            return [new Placement(id, bounds)];
        }

        if (!_suspended.Remove(id, out var suspension))
            return [];

        SetMinimized(id, false);
        var node = suspension.Node;
        _nodes[id] = node;

        if (suspension.Sibling is { } sibling && InTree(sibling))
            Wrap(sibling, node, suspension.Index, suspension.SplitRatio, suspension.SplitTop);
        else if (_root is null)
        {
            node.Parent = null;
            node.Box = TilingArea();
            _root = node;
        }
        else
        {
            Split(SplitTarget() ?? Leaf(_root), node, null);
        }

        _splitTarget = id;
        Refit();
        return Placements();
    }

    public bool TryTake(WindowId window, out Taken taken)
    {
        taken = default;
        var id = new ClientId(window.Value);
        var index = _clients.FindIndex(client => client.Id == id);
        if (index < 0)
            return false;

        var client = _clients[index];
        var bounds = _floating.TryGet(id, out var floating)
            ? floating
            : _natural.TryGetValue(id, out var natural) ? natural : default;
        _limits.TryGetValue(id, out var limits);
        taken = SlotHandover.Describe(client, bounds, limits);
        Remove(window);
        return true;
    }

    public IReadOnlyList<Placement> Visible()
    {
        var placements = new List<Placement>(Placements());
        _floating.AddShown(placements, _clients);
        return placements.ToArray();
    }

    public IReadOnlyList<Placement> Placed() => Placements();

    private void SetMinimized(ClientId id, bool minimized)
    {
        var index = _clients.FindIndex(client => client.Id == id);
        _clients[index] = _clients[index] with { Minimized = minimized };
    }

    private Node? Opening(Point? pointer)
    {
        if (!_options.UseActiveForSplits)
        {
            if (!_activeWorkspace)
                return FirstLeaf();

            if (pointer is { } point)
            {
                var hit = LeafContaining(point);
                if (hit is not null)
                    return hit;

                if (OnReserved(point))
                    return ClosestLeaf(point);
            }

            return FirstLeaf();
        }

        return SplitTarget() ?? (_root is null ? null : Leaf(_root));
    }

    private bool OnReserved(Point point)
    {
        if (_monitor.Width <= 0 || _monitor.Height <= 0)
            return false;

        if (point.X < _monitor.X - 1
            || point.Y < _monitor.Y - 1
            || point.X >= _monitor.X + _monitor.Width + 1
            || point.Y >= _monitor.Y + _monitor.Height + 1)
            return false;

        return point.X < _area.X
            || point.Y < _area.Y
            || point.X >= _area.X + _area.Width
            || point.Y >= _area.Y + _area.Height;
    }

    private Placement[] Placements()
    {
        var placements = new List<Placement>();
        foreach (var client in _clients)
        {
            if (client.Minimized || client.Floating)
                continue;

            placements.Add(new Placement(client.Id, Present(client.Id, _nodes[client.Id].Box)));
        }

        return placements.ToArray();
    }

    private Rectangle Present(ClientId id, Rectangle box)
    {
        var area = TilingArea();
        var gaps = _options.GapsIn;
        var left = box.X == area.X ? 0 : gaps.Left;
        var top = box.Y == area.Y ? 0 : gaps.Top;
        var right = box.X + box.Width == area.X + area.Width ? 0 : gaps.Right;
        var bottom = box.Y + box.Height == area.Y + area.Height ? 0 : gaps.Bottom;
        var presented = _options.KeepAspect(Shrink(box, left, top, right, bottom), _area, _nodes.Count);
        _natural.TryGetValue(id, out var natural);
        presented = _pseudo.Frame(id, presented, natural, _options.BorderFor(id));
        return _limits.TryGetValue(id, out var limits) ? limits.ForTiles.Raise(presented, area) : presented;
    }

    private Rectangle TilingArea()
    {
        var gaps = _options.GapsOut;
        var width = Math.Max(0, _area.Width - gaps.Left - gaps.Right);
        var height = Math.Max(0, _area.Height - gaps.Top - gaps.Bottom);
        return new Rectangle(_area.X + gaps.Left, _area.Y + gaps.Top, width, height);
    }

    private void Recalculate(Node node, bool keepDirection = false)
    {
        if (node.First is null || node.Second is null)
            return;

        if (!keepDirection && !_options.PreserveSplit && !_options.SmartSplit && !_options.PreciseMouseMove)
            node.SplitTop = SplitsTop(node.Box);

        if (node.SplitTop)
        {
            var first = FirstShare(node.Box.Height, node.SplitRatio);
            node.First.Box = new Rectangle(node.Box.X, node.Box.Y, node.Box.Width, first);
            node.Second.Box = new Rectangle(node.Box.X, node.Box.Y + first, node.Box.Width, node.Box.Height - first);
        }
        else
        {
            var first = FirstShare(node.Box.Width, node.SplitRatio);
            node.First.Box = new Rectangle(node.Box.X, node.Box.Y, first, node.Box.Height);
            node.Second.Box = new Rectangle(node.Box.X + first, node.Box.Y, node.Box.Width - first, node.Box.Height);
        }

        Recalculate(node.First);
        Recalculate(node.Second);
    }

    private bool SplitsTop(Rectangle box) =>
        box.Height * _options.SplitWidthMultiplier > box.Width;

    // The epsilon keeps a ratio set from a whole pixel delta, such as 1.614 of 500, from truncating to the pixel below.
    private static int FirstShare(int size, double splitRatio) => (int)((size / 2.0 * splitRatio) + 1e-6);

    private static Rectangle Shrink(Rectangle box, int left, int top, int right, int bottom)
    {
        var width = Math.Max(0, box.Width - left - right);
        var height = Math.Max(0, box.Height - top - bottom);
        return new Rectangle(box.X + left, box.Y + top, width, height);
    }

    private static bool Covers(Rectangle box, Point point) =>
        point.X >= box.X
        && point.Y >= box.Y
        && point.X < box.X + box.Width
        && point.Y < box.Y + box.Height;

    private static double DistanceSquared(Rectangle box, Point point)
    {
        var dx = point.X < box.X ? box.X - point.X : point.X >= box.X + box.Width ? point.X - (box.X + box.Width) : 0;
        var dy = point.Y < box.Y ? box.Y - point.Y : point.Y >= box.Y + box.Height ? point.Y - (box.Y + box.Height) : 0;
        return (dx * (double)dx) + (dy * (double)dy);
    }

    private static PendingSplit ParseDirection(string direction) => direction[0] switch
    {
        'l' => PendingSplit.Left,
        'r' => PendingSplit.Right,
        'u' or 't' => PendingSplit.Up,
        'd' or 'b' => PendingSplit.Down,
        _ => PendingSplit.None,
    };

    private static Node Leaf(Node node)
    {
        while (node.Id is null)
            node = node.First!;

        return node;
    }

    private enum PendingSplit
    {
        None,
        Left,
        Right,
        Up,
        Down,
    }

    private sealed class Node
    {
        public Node? Parent { get; set; }

        public Node? First { get; set; }

        public Node? Second { get; set; }

        public ClientId? Id { get; set; }

        public Rectangle Box { get; set; }

        public double SplitRatio { get; set; } = 1;

        public bool SplitTop { get; set; }
    }

    private sealed record Sibling(Node Node, int Index, double SplitRatio, bool SplitTop);

    private sealed record Suspension(Node Node, Node? Sibling, int Index, double SplitRatio, bool SplitTop);
}
