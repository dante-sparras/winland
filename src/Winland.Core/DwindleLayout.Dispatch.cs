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

        if (_floating.TryGetValue(id, out var floating))
            return MoveFloating(id, floating, direction);

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

        if (_floating.TryGetValue(left, out var leftBox) && _floating.TryGetValue(right, out var rightBox))
        {
            _floating[left] = rightBox;
            _floating[right] = leftBox;
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

        if (_floating.ContainsKey(id))
            return Tile(id, index);

        if (!_nodes.TryGetValue(id, out var node) || !InTree(node))
            return [];

        var tile = node.Box;
        var sibling = Detach(node);
        _nodes.Remove(id);
        if (_splitTarget == id)
            _splitTarget = sibling is null ? null : Leaf(sibling.Node).Id;

        var floated = FloatingBounds(id, tile);
        _floating[id] = floated;
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
            return tokens.Length < 2 ? [] : Preselect(tokens[1]);
        if (Is(tokens[0], DwindleMessages.ToggleSplit))
            return _options.PreserveSplit ? ToggleSplit(active) : [];
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

        if (!TryNode(window, out var node))
            return [];
        if (!MoveNodeToRoot(node, stable))
            return [];

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
        if (tokens.Length < 2 || !double.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
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
        if (_floating.TryGetValue(id, out var floating) && floating.Width > 0 && floating.Height > 0)
            _lastFloat[id] = (floating.Width, floating.Height);

        _floating.Remove(id);
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
        return FitInWorkArea(floated, _options.FloatingWorkArea(_area));
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

    private IReadOnlyList<Placement> MoveFloating(ClientId id, Rectangle box, PendingSplit direction)
    {
        var frame = _options.FloatingWorkArea(_area);
        var moved = direction switch
        {
            PendingSplit.Left => box with { X = frame.X },
            PendingSplit.Right => box with { X = frame.X + frame.Width - box.Width },
            PendingSplit.Up => box with { Y = frame.Y },
            PendingSplit.Down => box with { Y = frame.Y + frame.Height - box.Height },
            _ => box,
        };

        if (moved == box)
            return [];

        _floating[id] = moved;
        return [new Placement(id, moved)];
    }

    private IReadOnlyList<Placement> ResizeFloating(ClientId id, Rectangle box, int x, int y, bool exact)
    {
        var deltaX = exact ? x - box.Width : x;
        var deltaY = exact ? y - box.Height : y;
        var resized = new Rectangle(box.X - (deltaX / 2), box.Y - (deltaY / 2), box.Width + deltaX, box.Height + deltaY);
        if (resized.Width < 1 || resized.Height < 1)
            return [];

        _floating[id] = resized;
        return [new Placement(id, resized)];
    }

    private WindowId? Neighbor(Rectangle origin, PendingSplit direction, WindowId except, bool floating)
    {
        WindowId? best = null;
        var bestOverlap = 1.0;
        foreach (var client in _clients)
        {
            if (client.Window == except || client.Minimized || client.Floating != floating)
                continue;
            if (!TryGeometry(client.Window, out var box, out _))
                continue;
            if (!Overlap(origin, box, direction, out var overlap) || overlap <= bestOverlap)
                continue;

            bestOverlap = overlap;
            best = client.Window;
        }

        return best;
    }

    private static bool Overlap(Rectangle origin, Rectangle other, PendingSplit direction, out double overlap)
    {
        var adjacent = direction switch
        {
            PendingSplit.Left => Adjacent(origin.X, origin.X + origin.Width, other.X, other.X + other.Width),
            PendingSplit.Right => Adjacent(other.X, other.X + other.Width, origin.X, origin.X + origin.Width),
            PendingSplit.Up => Adjacent(origin.Y, origin.Y + origin.Height, other.Y, other.Y + other.Height),
            PendingSplit.Down => Adjacent(other.Y, other.Y + other.Height, origin.Y, origin.Y + origin.Height),
            _ => false,
        };

        overlap = adjacent
            ? direction is PendingSplit.Left or PendingSplit.Right
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

    private bool TryGeometry(WindowId window, out Rectangle box, out bool floating)
    {
        var id = new ClientId(window.Value);
        if (_floating.TryGetValue(id, out box))
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

    private static string[] Tokens(string arguments) =>
        arguments.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
}
