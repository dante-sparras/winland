namespace Winland;

internal sealed partial class DwindleLayout : Layout
{
    private readonly List<Client> _clients = [];
    private readonly Dictionary<ClientId, Node> _nodes = [];
    private readonly Dictionary<ClientId, Suspension> _suspended = [];
    private readonly Dictionary<ClientId, Rectangle> _floating = [];
    private readonly Dictionary<ClientId, Rectangle> _natural = [];
    private readonly Dictionary<ClientId, (int Width, int Height)> _lastFloat = [];
    private readonly HashSet<ClientId> _pseudo = [];
    private LayoutOptions _options = LayoutOptions.Unconfigured;
    private PendingSplit _preselect;
    private Node? _root;
    private ClientId? _splitTarget;
    private Rectangle _area;
    private Rectangle _monitor;
    private bool _activeWorkspace = true;

    public IReadOnlyList<Client> Clients => _clients;

    public void NudgeFloating(int dx, int dy) => FloatingShift.By(_floating, dx, dy);

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
        if (_floating.TryGetValue(id, out bounds))
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

        if (!_pseudo.Add(id))
            _pseudo.Remove(id);

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
        Size? maxSize = null)
    {
        _area = area;
        _monitor = monitor;
        _activeWorkspace = activeWorkspace;
        var id = new ClientId(window.Value);
        if (_nodes.ContainsKey(id) || _suspended.ContainsKey(id) || _floating.ContainsKey(id))
            return [];

        _limits[id] = new ClientLimits(minSize, maxSize);
        _natural[id] = bounds;
        var placed = ClampToLimits(bounds, minSize, maxSize);
        if (placed.Width > 0 && placed.Height > 0)
            _lastFloat[id] = (placed.Width, placed.Height);
        var floats = floating || CannotFill(maxSize, pointer);
        var client = Adopted(id, window, workspace, windowClass, title, floats, processId, initialClass, initialTitle);
        if (floats)
        {
            _clients.Add(client);
            _floating[id] = placed;
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
        _pseudo.Remove(id);
        _natural.Remove(id);
        _lastFloat.Remove(id);
        _limits.Remove(id);
        if (_floating.Remove(id) || _suspended.Remove(id))
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
        if (_floating.ContainsKey(id))
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
        if (_floating.TryGetValue(id, out var bounds))
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
        var bounds = _floating.TryGetValue(id, out var floating)
            ? floating
            : _natural.TryGetValue(id, out var natural) ? natural : default;
        _limits.TryGetValue(id, out var limits);
        taken = new Taken(
            client.Floating,
            client.Minimized,
            bounds,
            client.Class,
            client.Title,
            client.ProcessId,
            client.InitialClass,
            client.InitialTitle,
            limits.Min,
            limits.Max);
        Remove(window);
        return true;
    }

    public IReadOnlyList<Placement> Visible()
    {
        var placements = new List<Placement>(Placements());
        foreach (var client in _clients)
        {
            if (!client.Floating || client.Minimized)
                continue;

            if (_floating.TryGetValue(client.Id, out var bounds))
                placements.Add(new Placement(client.Id, bounds));
        }

        return placements.ToArray();
    }

    public IReadOnlyList<Placement> Placed() => Placements();

    private static Client Adopted(
        ClientId id,
        WindowId window,
        WorkspaceId workspace,
        string windowClass,
        string title,
        bool floating,
        long processId,
        string initialClass,
        string initialTitle) =>
        new(
            id,
            window,
            Floating: floating,
            Workspace: workspace,
            Class: windowClass,
            Title: title,
            ProcessId: processId,
            InitialClass: initialClass.Length == 0 ? windowClass : initialClass,
            InitialTitle: initialTitle.Length == 0 ? title : initialTitle);

    private void Split(Node opening, Node node, Point? pointer, bool focal = false)
    {
        var parent = new Node
        {
            Box = opening.Box,
            Parent = opening.Parent,
            SplitRatio = _options.DefaultSplitRatio,
            SplitTop = SplitsTop(opening.Box),
        };

        var direction = TakePreselect();
        if (direction != PendingSplit.None)
            AssignDirection(parent, opening, node, direction);
        else if (_options.SmartSplit && pointer is { } point)
            AssignSmart(parent, opening, node, point);
        else if (!focal && _options.ForceSplit == 1)
        {
            parent.First = node;
            parent.Second = opening;
        }
        else if (!focal && _options.ForceSplit == 2)
        {
            parent.First = opening;
            parent.Second = node;
        }
        else
            AssignByPointer(parent, opening, node, pointer);

        if (_options.SplitBias == 1 && ReferenceEquals(parent.First, node))
            parent.SplitRatio = 2 - parent.SplitRatio;

        Replace(opening, parent);
        opening.Parent = parent;
        node.Parent = parent;
        Refit();
    }

    private PendingSplit TakePreselect()
    {
        var direction = _preselect;
        if (direction != PendingSplit.None && !_options.PermanentDirectionOverride)
            _preselect = PendingSplit.None;
        return direction;
    }

    private static void AssignDirection(Node parent, Node opening, Node node, PendingSplit direction)
    {
        parent.SplitTop = direction is PendingSplit.Up or PendingSplit.Down;
        if (direction is PendingSplit.Up or PendingSplit.Left)
        {
            parent.First = node;
            parent.Second = opening;
        }
        else
        {
            parent.First = opening;
            parent.Second = node;
        }
    }

    private void AssignSmart(Node parent, Node opening, Node node, Point point)
    {
        var centerX = parent.Box.X + (parent.Box.Width / 2.0);
        var centerY = parent.Box.Y + (parent.Box.Height / 2.0);
        var dx = point.X - centerX;
        var dy = point.Y - centerY;
        var proportions = parent.Box.Width == 0
            ? double.PositiveInfinity
            : parent.Box.Height / (double)parent.Box.Width;
        var slope = dx == 0 ? double.PositiveInfinity : dy / dx;

        if (Math.Abs(slope) < proportions)
        {
            parent.SplitTop = false;
            if (dx > 0)
            {
                parent.First = opening;
                parent.Second = node;
            }
            else
            {
                parent.First = node;
                parent.Second = opening;
            }
        }
        else
        {
            parent.SplitTop = true;
            if (dy > 0)
            {
                parent.First = opening;
                parent.Second = node;
            }
            else
            {
                parent.First = node;
                parent.Second = opening;
            }
        }
    }

    private void AssignByPointer(Node parent, Node opening, Node node, Point? pointer)
    {
        var sideBySide = parent.Box.Width > parent.Box.Height * _options.SplitWidthMultiplier;
        var inFirst = pointer is { } point && (sideBySide
            ? point.X < parent.Box.X + (parent.Box.Width / 2.0)
            : point.Y < parent.Box.Y + (parent.Box.Height / 2.0));
        if (inFirst)
        {
            parent.First = node;
            parent.Second = opening;
        }
        else
        {
            parent.First = opening;
            parent.Second = node;
        }
    }

    private void Wrap(Node sibling, Node node, int index, double splitRatio, bool splitTop)
    {
        var parent = new Node
        {
            Parent = sibling.Parent,
            SplitRatio = splitRatio,
            SplitTop = splitTop,
            Box = sibling.Box,
        };

        if (index == 0)
        {
            parent.First = node;
            parent.Second = sibling;
        }
        else
        {
            parent.First = sibling;
            parent.Second = node;
        }

        Replace(sibling, parent);
        sibling.Parent = parent;
        node.Parent = parent;
    }

    private Sibling? Detach(Node node)
    {
        var parent = node.Parent;
        node.Parent = null;
        if (parent is null)
        {
            _root = null;
            return null;
        }

        var index = ReferenceEquals(parent.First, node) ? 0 : 1;
        var sibling = index == 0 ? parent.Second! : parent.First!;
        sibling.Parent = parent.Parent;
        Replace(parent, sibling);
        return new Sibling(sibling, index, parent.SplitRatio, parent.SplitTop);
    }

    private void Replace(Node current, Node replacement)
    {
        if (current.Parent is { } grandparent)
        {
            if (ReferenceEquals(grandparent.First, current))
                grandparent.First = replacement;
            else
                grandparent.Second = replacement;

            replacement.Parent = grandparent;
            return;
        }

        _root = replacement;
        replacement.Parent = null;
    }

    private void Refit()
    {
        if (_root is null || FullscreenClients.Covering(_clients))
            return;

        _root.Box = TilingArea();
        Recalculate(_root);
        RefitLimits();
    }

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

    private Node? SplitTarget() =>
        _splitTarget is { } id && _nodes.TryGetValue(id, out var node) ? node : null;

    private Node? LeafContaining(Point point) => _root is null ? null : FindLeaf(_root, point);

    private Node? FindLeaf(Node node, Point point)
    {
        if (node.Id is not null)
            return Covers(node.Box, point) ? node : null;

        if (node.First is not null && Covers(node.First.Box, point))
            return FindLeaf(node.First, point);

        if (node.Second is not null && Covers(node.Second.Box, point))
            return FindLeaf(node.Second, point);

        return null;
    }

    private Node? ClosestLeaf(Point point)
    {
        Node? best = null;
        var bestDistance = double.MaxValue;
        foreach (var client in _clients)
        {
            if (!_nodes.TryGetValue(client.Id, out var node) || !InTree(node))
                continue;

            var distance = DistanceSquared(node.Box, point);
            if (distance < bestDistance)
            {
                best = node;
                bestDistance = distance;
            }
        }

        return best;
    }

    private Node? FirstLeaf()
    {
        foreach (var client in _clients)
        {
            if (_nodes.TryGetValue(client.Id, out var node) && InTree(node))
                return node;
        }

        return null;
    }

    private bool InTree(Node target) => _root is not null && Contains(_root, target);

    private static bool Contains(Node node, Node target)
    {
        if (ReferenceEquals(node, target))
            return true;

        return (node.First is not null && Contains(node.First, target))
            || (node.Second is not null && Contains(node.Second, target));
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
        var presented = Shrink(box, left, top, right, bottom);
        if (_pseudo.Contains(id))
            presented = Center(presented, PseudoSize(id));

        var border = _options.BorderSize;
        return Shrink(presented, border, border, border, border);
    }

    private (int Width, int Height) PseudoSize(ClientId id)
    {
        if (_natural.TryGetValue(id, out var bounds) && bounds.Width > 0 && bounds.Height > 0)
            return (bounds.Width, bounds.Height);

        return (1280, 720);
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

        if (!keepDirection && !_options.PreserveSplit && !_options.SmartSplit)
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

    private static int FirstShare(int size, double splitRatio) => (int)(size / 2.0 * splitRatio);

    private static Rectangle Shrink(Rectangle box, int left, int top, int right, int bottom)
    {
        var width = Math.Max(0, box.Width - left - right);
        var height = Math.Max(0, box.Height - top - bottom);
        return new Rectangle(box.X + left, box.Y + top, width, height);
    }

    private static Rectangle Center(Rectangle tile, (int Width, int Height) size)
    {
        var width = (double)size.Width;
        var height = (double)size.Height;
        if (width > tile.Width || height > tile.Height)
        {
            var scale = 1.0;
            if (width > tile.Width)
                scale = tile.Width / width;
            if (height * scale > tile.Height)
                scale = tile.Height / height;

            width = Math.Round(width * scale, MidpointRounding.AwayFromZero);
            height = Math.Round(height * scale, MidpointRounding.AwayFromZero);
        }

        var placedWidth = Math.Max(0, (int)width);
        var placedHeight = Math.Max(0, (int)height);
        return new Rectangle(
            tile.X + ((tile.Width - placedWidth) / 2),
            tile.Y + ((tile.Height - placedHeight) / 2),
            placedWidth,
            placedHeight);
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
