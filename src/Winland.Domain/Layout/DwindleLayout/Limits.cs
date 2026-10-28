namespace Winland.Domain;

internal sealed partial class DwindleLayout
{

    public IReadOnlyList<Placement> UpdateLimits(WindowId window, Size? minSize, Size? maxSize, TiledLimits? tiledLimits = null)
    {
        var id = new ClientId(window.Value);
        var index = IndexOf(id);
        if (index < 0)
            return [];

        var limits = new SizeLimits(minSize, maxSize) { Tiled = tiledLimits };
        _limits[id] = limits;
        if (_floating.Contains(id))
            return _floating.Clamp(id, new SizeLimits(minSize, maxSize));

        if (!_nodes.TryGetValue(id, out var node) || !InTree(node))
            return [];

        if (Short(maxSize, node))
            return FloatShort(id, index, node, minSize, maxSize);

        Refit();
        return Placements();
    }

    private static bool Short(Size? max, Node node) =>
        max is { } limit && (limit.Width < node.Box.Width || limit.Height < node.Box.Height);

    // A Client tiled again, by a toggle or a drop, floats instead when its maximum cannot fill the tile it got.
    private IReadOnlyList<Placement> FloatIfShort(ClientId id, Node node)
    {
        if (_limits.TryGetValue(id, out var limits) && Short(limits.Max, node))
            return FloatShort(id, IndexOf(id), node, limits.Min, limits.Max);

        return Placements();
    }

    private IReadOnlyList<Placement> FloatShort(ClientId id, int index, Node node, Size? min, Size? max)
    {
        var presented = Present(id, node.Box);
        var sibling = Detach(node);
        _nodes.Remove(id);
        if (_splitTarget == id)
            _splitTarget = sibling is null ? null : Leaf(sibling.Node).Id;

        var floated = new SizeLimits(min, max).Clamp(presented);
        _floating.Set(id, floated);
        _clients[index] = _clients[index] with { Floating = true };
        Refit();
        var placements = new List<Placement>(Placements()) { new(id, floated) };
        return placements;
    }

    private bool CannotFill(Size? max)
    {
        if (max is not { } limit)
            return false;

        var tile = PredictedTile();
        return limit.Width < tile.Width || limit.Height < tile.Height;
    }

    // A window whose maximum cannot fill the tile it would open in floats instead. It would
    // have been sized for that tile, half the tile it splits or the whole Monitor, so it floats at that size kept
    // inside its limits, in the middle of the work area.
    private Rectangle FloatedFromTile(SizeLimits limits)
    {
        var tile = PredictedTile();
        if (Opening() is not null)
        {
            tile = tile.Height > tile.Width
                ? tile with { Height = tile.Height / 2 }
                : tile with { Width = tile.Width / 2 };
        }

        return FloatedLimits.Centered(limits, tile, _area);
    }

    private Rectangle PredictedTile()
    {
        var opening = Opening();
        if (opening is not null)
            return opening.Box;

        if (_monitor.Width > 0 && _monitor.Height > 0)
            return new Rectangle(0, 0, _monitor.Width, _monitor.Height);

        return _area;
    }

    private void RefitLimits()
    {
        if (_root is null)
            return;

        HonorMinimums(_root);
        HonorMinimums(_root);
    }

    // Where the minimums along a split fit in the parent, the split gives each window
    // its minimum and the neighbour takes whatever space remains. Where they do not,
    // the side that needs more takes all but the other side's floor: no tile grows
    // past its parent or the Monitor, and a window below its minimum grows over its
    // neighbours when it is placed (SizeLimits.Raise).
    private void HonorMinimums(Node node)
    {
        if (node.First is null || node.Second is null)
            return;

        var horizontal = !node.SplitTop;
        var available = horizontal ? node.Box.Width : node.Box.Height;
        // Neither side gives up more than the smallest split ratio, 0.1, would leave it.
        var floor = FirstShare(available, 0.1);
        var firstNeed = Math.Max(Need(node.First, horizontal), floor);
        var secondNeed = Math.Max(Need(node.Second, horizontal), floor);
        var firstHave = horizontal ? node.First.Box.Width : node.First.Box.Height;
        var secondHave = horizontal ? node.Second.Box.Width : node.Second.Box.Height;
        int firstSpan;
        int secondSpan;
        var ideal = FirstShare(available, node.SplitRatio);
        if (firstNeed + secondNeed > available)
        {
            // The side that needs more takes all it can and leaves the other its floor. Equal needs keep the ratio.
            if (firstNeed > secondNeed)
                firstSpan = Math.Min(firstNeed, available - floor);
            else if (secondNeed > firstNeed)
                firstSpan = available - Math.Min(secondNeed, available - floor);
            else
                firstSpan = ideal;
        }
        else if (ideal < firstNeed)
            firstSpan = firstNeed;
        else if (available - ideal < secondNeed)
            firstSpan = available - secondNeed;
        else
            firstSpan = ideal;

        secondSpan = available - firstSpan;

        var crossHave = horizontal ? node.First.Box.Height : node.First.Box.Width;
        var crossNow = horizontal ? node.Box.Height : node.Box.Width;
        // A split that already fits keeps its boxes, but a minimum deeper down can still need room.
        var settled = firstSpan == firstHave && secondSpan == secondHave && crossHave == crossNow;
        if (!settled)
            Resplit(node, horizontal, firstSpan, secondSpan);

        HonorMinimums(node.First);
        HonorMinimums(node.Second);
    }

    private void Resplit(Node node, bool horizontal, int firstSpan, int secondSpan)
    {
        if (horizontal)
        {
            node.First!.Box = new Rectangle(node.Box.X, node.Box.Y, firstSpan, node.Box.Height);
            node.Second!.Box = new Rectangle(node.Box.X + firstSpan, node.Box.Y, secondSpan, node.Box.Height);
        }
        else
        {
            node.First!.Box = new Rectangle(node.Box.X, node.Box.Y, node.Box.Width, firstSpan);
            node.Second!.Box = new Rectangle(node.Box.X, node.Box.Y + firstSpan, node.Box.Width, secondSpan);
        }

        Recalculate(node.First, keepDirection: true);
        Recalculate(node.Second, keepDirection: true);
    }

    private int Need(Node node, bool horizontal)
    {
        if (node.Id is { } id)
        {
            if (!_limits.TryGetValue(id, out var limits) || limits.ForTiles.Min is not { } min)
                return 0;

            var want = horizontal ? min.Width : min.Height;
            var chrome = Chrome(id, node.Box);
            return AtLeast(0, want, horizontal ? chrome.Horizontal : chrome.Vertical);
        }

        if (node.First is null || node.Second is null)
            return 0;

        var first = Need(node.First, horizontal);
        var second = Need(node.Second, horizontal);
        var along = !node.SplitTop == horizontal;
        var sum = (long)first + second;
        if (sum > int.MaxValue)
            sum = int.MaxValue;

        return along ? (int)sum : Math.Max(first, second);
    }

    private (int Horizontal, int Vertical) Chrome(ClientId id, Rectangle box)
    {
        var area = TilingArea();
        var gaps = _options.GapsIn;
        var left = box.X == area.X ? 0 : gaps.Left;
        var top = box.Y == area.Y ? 0 : gaps.Top;
        var right = box.X + box.Width == area.X + area.Width ? 0 : gaps.Right;
        var bottom = box.Y + box.Height == area.Y + area.Height ? 0 : gaps.Bottom;
        var border = _options.BorderFor(id);
        return (left + right + (border * 2), top + bottom + (border * 2));
    }

    private static int AtLeast(int current, int want, int chrome)
    {
        if (want < 1)
            return current;

        var need = (long)want + chrome;
        if (need > int.MaxValue)
            need = int.MaxValue;

        return (int)Math.Max(current, need);
    }
}
