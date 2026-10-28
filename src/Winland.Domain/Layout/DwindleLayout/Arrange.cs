namespace Winland.Domain;

internal sealed partial class DwindleLayout
{
    // How far apart the tiles on the two sides of a split may stand: the gap between them, their borders and what
    // rounding leaves.
    private const int SplitSlack = 64;

    /// <summary>
    /// Builds the tree again from the rectangle each tile had, as a restart finds them in the state file. Every split
    /// of a dwindle tree cuts its box in two, so the rectangles name the splits again, their direction and their
    /// ratio. Changes nothing unless every tile has a rectangle and the rectangles make such a tree.
    /// </summary>
    public IReadOnlyList<Placement> Arrange(IReadOnlyDictionary<WindowId, Rectangle> tiles)
    {
        if (_root is null || _suspended.Count > 0 || FullscreenClients.Covering(_clients))
            return [];

        var leaves = new List<(Node Node, Rectangle Box)>();
        foreach (var node in _nodes.Values)
        {
            if (!InTree(node))
                continue;

            if (!tiles.TryGetValue(new WindowId(node.Id!.Value.Value), out var box))
                return [];

            leaves.Add((node, box));
        }

        var splits = new Dictionary<Node, int>();
        if (leaves.Count < 2 || Cut(leaves, splits) is not Node root)
            return [];

        _root = root;
        _root.Parent = null;
        _root.Box = TilingArea();
        Ratio(_root, splits);
        Refit();
        return Placements();
    }

    // Each split's ratio puts it where it was, in the box the tree gives the node, from the top down. The children
    // learn their parent here, once the whole cut has held, so a cut that fails leaves the live tree as it was.
    private static void Ratio(Node node, Dictionary<Node, int> splits)
    {
        if (node.First is null || node.Second is null)
            return;

        node.First.Parent = node;
        node.Second.Parent = node;

        var start = node.SplitTop ? node.Box.Y : node.Box.X;
        var size = node.SplitTop ? node.Box.Height : node.Box.Width;
        node.SplitRatio = Math.Clamp(2.0 * (splits[node] - start) / Math.Max(1, size), 0.1, 1.9);
        var first = FirstShare(size, node.SplitRatio);
        if (node.SplitTop)
        {
            node.First.Box = node.Box with { Height = first };
            node.Second.Box = node.Box with { Y = node.Box.Y + first, Height = node.Box.Height - first };
        }
        else
        {
            node.First.Box = node.Box with { Width = first };
            node.Second.Box = node.Box with { X = node.Box.X + first, Width = node.Box.Width - first };
        }

        Ratio(node.First, splits);
        Ratio(node.Second, splits);
    }

    // The tree over these tiles, or null when no line splits them into two groups all the way across. Where each
    // split falls goes in splits.
    private static Node? Cut(List<(Node Node, Rectangle Box)> tiles, Dictionary<Node, int> splits)
    {
        if (tiles.Count == 1)
            return tiles[0].Node;

        foreach (var top in new[] { false, true })
        {
            var ordered = tiles.OrderBy(tile => top ? tile.Box.Y : tile.Box.X).ToList();
            for (var split = 1; split < ordered.Count; split++)
            {
                var first = ordered.Take(split).ToList();
                var second = ordered.Skip(split).ToList();
                var firstEnd = top ? first.Max(tile => tile.Box.Y + tile.Box.Height) : first.Max(tile => tile.Box.X + tile.Box.Width);
                var secondStart = top ? second.Min(tile => tile.Box.Y) : second.Min(tile => tile.Box.X);
                if (firstEnd > secondStart + 1 || secondStart - firstEnd > SplitSlack)
                    continue;

                if (Cut(first, splits) is not Node firstNode || Cut(second, splits) is not Node secondNode)
                    return null;

                // The split falls in the middle of the gap between the two sides.
                var node = new Node { First = firstNode, Second = secondNode, SplitTop = top };
                splits[node] = (int)Math.Round((firstEnd + secondStart) / 2.0);
                return node;
            }
        }

        return null;
    }
}
