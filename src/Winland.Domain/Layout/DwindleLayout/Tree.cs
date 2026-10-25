namespace Winland.Domain;

internal sealed partial class DwindleLayout
{
    private void Split(Node opening, Node node, Point? pointer)
    {
        var parent = new Node
        {
            Box = opening.Box,
            Parent = opening.Parent,
            SplitRatio = 1,
            SplitTop = SplitsTop(opening.Box),
        };

        var direction = TakePreselect();
        if (direction != PendingSplit.None)
            AssignDirection(parent, opening, node, direction);
        else
            AssignByPointer(parent, opening, node, pointer);

        Replace(opening, parent);
        opening.Parent = parent;
        node.Parent = parent;
        Refit();
    }

    private PendingSplit TakePreselect()
    {
        var direction = _preselect;
        if (direction != PendingSplit.None)
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

    private static void AssignByPointer(Node parent, Node opening, Node node, Point? pointer)
    {
        var sideBySide = parent.Box.Width > parent.Box.Height;
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

    private Node? SplitTarget() =>
        _splitTarget is { } id && _nodes.TryGetValue(id, out var node) ? node : null;

    private Node? LeafContaining(Point point) => _root is null ? null : FindLeaf(_root, point);

    private static Node? FindLeaf(Node node, Point point)
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

    private bool InTree(Node target) => _root is not null && Contains(_root, target);

    private static bool Contains(Node node, Node target)
    {
        if (ReferenceEquals(node, target))
            return true;

        return (node.First is not null && Contains(node.First, target))
            || (node.Second is not null && Contains(node.Second, target));
    }
}
