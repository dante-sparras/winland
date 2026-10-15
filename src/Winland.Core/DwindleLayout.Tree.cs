namespace Winland;

internal sealed partial class DwindleLayout
{
    // A drop from a mouse drag follows the pointer even under force_split, and with precise_mouse_move it picks the side as smart_split does.
    private void Split(Node opening, Node node, Point? pointer, bool focal = false, bool dropped = false)
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
        else if ((_options.SmartSplit || (_options.PreciseMouseMove && dropped)) && pointer is { } point)
            AssignSmart(parent, opening, node, point);
        else if (!focal && !dropped && _options.ForceSplit == 1)
        {
            parent.First = node;
            parent.Second = opening;
        }
        else if (!focal && !dropped && _options.ForceSplit == 2)
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
}
