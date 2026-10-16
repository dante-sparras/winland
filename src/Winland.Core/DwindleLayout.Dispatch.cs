using System.Globalization;
using static Winland.OrdinalText;

namespace Winland;

internal sealed partial class DwindleLayout
{
    private static readonly (string Word, PendingSplit Direction)[] Directions =
    [
        ("l", PendingSplit.Left),
        ("left", PendingSplit.Left),
        ("r", PendingSplit.Right),
        ("right", PendingSplit.Right),
        ("u", PendingSplit.Up),
        ("up", PendingSplit.Up),
        ("t", PendingSplit.Up),
        ("top", PendingSplit.Up),
        ("d", PendingSplit.Down),
        ("down", PendingSplit.Down),
        ("b", PendingSplit.Down),
        ("bottom", PendingSplit.Down),
    ];

    public WindowId? MoveFocus(WindowId active, string arguments)
    {
        if (!TryDirection(First(arguments), out var direction))
            return null;
        if (!TryGeometry(active, out var origin, out var floating))
            return null;

        return Neighbor(origin, direction, active, floating)
            ?? Neighbor(origin, direction, active, !floating);
    }

    private WindowId? Neighbor(Rectangle origin, PendingSplit direction, WindowId except, bool floating) =>
        ToSide(direction) is Side side
            ? DirectionalNeighbor.Find(_clients, origin, side, except, floating, (WindowId window, out Rectangle box) => TryGeometry(window, out box, out _))
            : null;

    private static Side? ToSide(PendingSplit direction) => direction switch
    {
        PendingSplit.Left => Side.Left,
        PendingSplit.Right => Side.Right,
        PendingSplit.Up => Side.Up,
        PendingSplit.Down => Side.Down,
        _ => null,
    };

    public void NoteFocus(WindowId window)
    {
        var id = new ClientId(window.Value);
        if (_nodes.TryGetValue(id, out var node) && InTree(node))
            _splitTarget = id;
    }

    public IReadOnlyList<Placement> MoveWindow(WindowId window, string arguments)
    {
        if (!TryDirection(First(arguments), out var direction))
            return [];

        var id = new ClientId(window.Value);
        if (Minimized(id))
            return [];

        if (_floating.Contains(id))
            return MoveFloating(id, direction);

        if (!_nodes.TryGetValue(id, out var node) || node.Parent is null || !InTree(node))
            return [];

        var focal = Focal(node.Box, direction);
        var towardSibling = TowardLeafSibling(node, direction);
        Detach(node);
        Refit();

        var opening = LeafContaining(focal) ?? ClosestLeaf(focal);
        if (opening is null || _root is null)
        {
            node.Parent = null;
            node.Box = TilingArea();
            _root = node;
            _splitTarget = id;
            Refit();
            return Placements();
        }

        if (towardSibling)
            _preselect = direction;

        Split(opening, node, focal, focal: true);
        _splitTarget = id;
        return Placements();
    }

    public IReadOnlyList<Placement> Swap(WindowId first, WindowId second)
    {
        if (first == second)
            return [];

        var left = new ClientId(first.Value);
        var right = new ClientId(second.Value);
        if (Minimized(left) || Minimized(right))
            return [];

        if (_nodes.TryGetValue(left, out var leftNode)
            && _nodes.TryGetValue(right, out var rightNode)
            && InTree(leftNode)
            && InTree(rightNode))
        {
            leftNode.Id = right;
            rightNode.Id = left;
            _nodes[left] = rightNode;
            _nodes[right] = leftNode;
            Refit();
            return Placements();
        }

        if (_floating.TryGet(left, out var leftBox) && _floating.TryGet(right, out var rightBox))
        {
            _floating.Set(left, rightBox);
            _floating.Set(right, leftBox);
            return [new Placement(left, rightBox), new Placement(right, leftBox)];
        }

        return [];
    }

    public IReadOnlyList<Placement> ToggleFloating(WindowId window)
    {
        var id = new ClientId(window.Value);
        var index = _clients.FindIndex(client => client.Id == id);
        if (index < 0 || _clients[index].Minimized)
            return [];

        if (_floating.Contains(id))
            return Tile(id, index);

        if (!_nodes.TryGetValue(id, out var node) || !InTree(node))
            return [];

        var tile = node.Box;
        var sibling = Detach(node);
        _nodes.Remove(id);
        if (_splitTarget == id)
            _splitTarget = sibling is null ? null : Leaf(sibling.Node).Id;

        var floated = _floating.Around(id, tile, _options.FloatingWorkArea(_area));
        _floating.Set(id, floated);
        _clients[index] = _clients[index] with { Floating = true };
        Refit();

        var placements = new List<Placement>(Placements()) { new(id, floated) };
        return placements;
    }

    public string? LayoutMessageError { get; private set; }

    public IReadOnlyList<Placement> LayoutMessage(WindowId? active, string arguments)
    {
        LayoutMessageError = null;
        var tokens = Tokens(arguments);
        if (tokens.Length == 0)
        {
            LayoutMessageError = "error: bad layoutmsg";
            return [];
        }

        if (Is(tokens[0], "preselect"))
        {
            if (tokens.Length >= 2)
                return Preselect(tokens[1]);

            LayoutMessageError = "error: no direction for preselect";
            return [];
        }
        if (Is(tokens[0], DwindleMessages.ToggleSplit))
            return _options.PreserveSplit || _options.SmartSplit || _options.PreciseMouseMove ? ToggleSplit(active) : [];
        if (Is(tokens[0], DwindleMessages.SwapSplit))
            return SwapSplit(active);
        if (Is(tokens[0], DwindleMessages.RotateSplit))
            return RotateSplit(active, tokens);
        if (Is(tokens[0], "movetoroot"))
            return MoveToRoot(active, tokens);
        if (Is(tokens[0], DwindleMessages.SplitRatio))
            return SplitRatio(active, tokens);

        LayoutMessageError = "error: bad layoutmsg '" + tokens[0] + "'";
        return [];
    }

    private IReadOnlyList<Placement> ToggleSplit(WindowId? active)
    {
        if (!TryNode(active, out var node) || node.Parent is null)
            return [];

        node.Parent.SplitTop = !node.Parent.SplitTop;
        Refit();
        return Placements();
    }

    private IReadOnlyList<Placement> SwapSplit(WindowId? active)
    {
        if (!TryNode(active, out var node) || node.Parent is null)
            return [];

        (node.Parent.First, node.Parent.Second) = (node.Parent.Second, node.Parent.First);
        Refit();
        return Placements();
    }

    private IReadOnlyList<Placement> RotateSplit(WindowId? active, string[] tokens)
    {
        var angle = 90;
        if (tokens.Length >= 2 && !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out angle))
        {
            LayoutMessageError = "error: bad layoutmsg '" + DwindleMessages.RotateSplit + "'";
            return [];
        }
        if (!TryNode(active, out var node) || node.Parent is null)
            return [];

        var steps = ((angle / 90 % 4) + 4) % 4;
        var parent = node.Parent;
        var swap = false;
        switch (steps)
        {
            case 1:
                if (parent.SplitTop)
                    swap = true;
                parent.SplitTop = !parent.SplitTop;
                break;
            case 2:
                swap = true;
                break;
            case 3:
                if (!parent.SplitTop)
                    swap = true;
                parent.SplitTop = !parent.SplitTop;
                break;
        }

        if (swap)
            (parent.First, parent.Second) = (parent.Second, parent.First);

        Refit();
        return Placements();
    }

    private IReadOnlyList<Placement> MoveToRoot(WindowId? active, string[] tokens)
    {
        var window = active;
        if (tokens.Length >= 2)
        {
            var selected = WindowSelector.Find(tokens[1], active, _clients);
            if (selected is WindowId found)
                window = found;
        }

        var stable = tokens.Length < 3 || !Is(tokens[2], "unstable");

        if (!TryNode(window, out var node) || !MoveNodeToRoot(node, stable))
        {
            LayoutMessageError = "error: can't movetoroot in the current workspace";
            return [];
        }

        return Placements();
    }

    private bool MoveNodeToRoot(Node node, bool stable)
    {
        var parent = node.Parent;
        if (parent?.Parent is null || parent.First is null || parent.Second is null)
            return false;

        var nodeIndex = ReferenceEquals(parent.First, node) ? 0 : 1;
        var ancestor = node;
        var root = parent;
        while (root.Parent is not null)
        {
            ancestor = root;
            root = root.Parent;
        }

        if (root.First is null || root.Second is null)
            return false;

        var ancestorIsFirst = ReferenceEquals(root.First, ancestor);
        var swap = ancestorIsFirst ? root.Second : root.First;
        if (nodeIndex == 0)
            parent.First = swap;
        else
            parent.Second = swap;

        if (ancestorIsFirst)
            root.Second = node;
        else
            root.First = node;

        swap.Parent = parent;
        node.Parent = root;

        if (stable)
            (root.First, root.Second) = (root.Second, root.First);

        Refit();
        return true;
    }

    private IReadOnlyList<Placement> SplitRatio(WindowId? active, string[] tokens)
    {
        if (tokens.Length < 2 || !double.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var delta) || !double.IsFinite(delta))
        {
            LayoutMessageError = "error: bad layoutmsg '" + DwindleMessages.SplitRatio + "'";
            return [];
        }

        var exact = tokens.Length >= 3 && tokens[2].StartsWith("exact", StringComparison.OrdinalIgnoreCase);
        if (!TryNode(active, out var node) || node.Parent is null)
            return [];

        var ratio = exact ? delta : node.Parent.SplitRatio + delta;
        node.Parent.SplitRatio = Math.Clamp(ratio, 0.1, 1.9);
        Refit();
        return Placements();
    }

    private IReadOnlyList<Placement> Tile(ClientId id, int index)
    {
        _floating.Sink(id);
        _clients[index] = _clients[index] with { Floating = false };
        var node = new Node { Id = id };
        _nodes[id] = node;

        if (_root is null)
        {
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

    private IReadOnlyList<Placement> MoveFloating(ClientId id, PendingSplit direction) =>
        ToSide(direction) is Side side ? _floating.MoveToEdge(id, side, _options.FloatingWorkArea(_area)) : [];

    private bool TryGeometry(WindowId window, out Rectangle box, out bool floating)
    {
        var id = new ClientId(window.Value);
        if (_floating.TryGet(id, out box))
        {
            floating = true;
            return true;
        }

        floating = false;
        if (_nodes.TryGetValue(id, out var node) && InTree(node))
        {
            box = node.Box;
            return true;
        }

        box = default;
        return false;
    }

    private bool TryNode(WindowId? window, out Node node)
    {
        node = null!;
        if (window is not WindowId id)
            return false;
        if (!_nodes.TryGetValue(new ClientId(id.Value), out var found) || !InTree(found))
            return false;

        node = found;
        return true;
    }

    private bool Minimized(ClientId id)
    {
        var index = _clients.FindIndex(client => client.Id == id);
        return index >= 0 && _clients[index].Minimized;
    }

    private static bool TowardLeafSibling(Node node, PendingSplit direction)
    {
        var parent = node.Parent;
        if (parent is null)
            return false;

        var first = ReferenceEquals(parent.First, node);
        var sibling = first ? parent.Second : parent.First;
        if (sibling?.Id is null)
            return false;

        return direction switch
        {
            PendingSplit.Up => parent.SplitTop && !first,
            PendingSplit.Down => parent.SplitTop && first,
            PendingSplit.Left => !parent.SplitTop && !first,
            PendingSplit.Right => !parent.SplitTop && first,
            _ => false,
        };
    }

    private static Point Focal(Rectangle box, PendingSplit direction) => direction switch
    {
        PendingSplit.Left => new Point(box.X - 1, box.Y + (box.Height / 2)),
        PendingSplit.Right => new Point(box.X + box.Width + 1, box.Y + (box.Height / 2)),
        PendingSplit.Up => new Point(box.X + (box.Width / 2), box.Y - 1),
        PendingSplit.Down => new Point(box.X + (box.Width / 2), box.Y + box.Height + 1),
        _ => new Point(box.X, box.Y),
    };

    private static bool TryDirection(string text, out PendingSplit direction)
    {
        foreach (var (word, parsed) in Directions)
        {
            if (text.Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                direction = parsed;
                return true;
            }
        }

        direction = PendingSplit.None;
        return false;
    }

    private static string First(string arguments)
    {
        var tokens = Tokens(arguments);
        return tokens.Length == 0 ? "" : tokens[0];
    }

    private static string[] Tokens(string arguments) =>
        arguments.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
}
