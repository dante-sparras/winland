namespace Winland;

internal sealed partial class DwindleLayout
{
    private readonly Dictionary<ClientId, ClientLimits> _limits = [];

    public IReadOnlyList<Placement> UpdateLimits(WindowId window, Size? minSize, Size? maxSize)
    {
        var id = new ClientId(window.Value);
        var index = _clients.FindIndex(client => client.Id == id);
        if (index < 0)
            return [];

        _limits[id] = new ClientLimits(minSize, maxSize);
        if (_floating.TryGetValue(id, out var floating))
        {
            var clamped = ClampToLimits(floating, minSize, maxSize);
            if (clamped == floating)
                return [];

            _floating[id] = clamped;
            return [new Placement(id, clamped)];
        }

        if (!_nodes.TryGetValue(id, out var node) || !InTree(node))
            return [];

        if (maxSize is { } limit && (limit.Width < node.Box.Width || limit.Height < node.Box.Height))
            return FloatShort(id, index, node, minSize, maxSize);

        Refit();
        return Placements();
    }

    private IReadOnlyList<Placement> FloatShort(ClientId id, int index, Node node, Size? min, Size? max)
    {
        var presented = Present(id, node.Box);
        var sibling = Detach(node);
        _nodes.Remove(id);
        if (_splitTarget == id)
            _splitTarget = sibling is null ? null : Leaf(sibling.Node).Id;

        var floated = ClampToLimits(presented, min, max);
        _floating[id] = floated;
        _clients[index] = _clients[index] with { Floating = true };
        Refit();
        var placements = new List<Placement>(Placements()) { new(id, floated) };
        return placements;
    }

    private bool CannotFill(Size? max, Point? pointer)
    {
        if (max is not { } limit)
            return false;

        var tile = PredictedTile(pointer);
        return limit.Width < tile.Width || limit.Height < tile.Height;
    }

    private Rectangle PredictedTile(Point? pointer)
    {
        var opening = Opening(pointer);
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

    // A tile's presented size is never below the window's minimum. The split gives
    // that minimum to the window and the neighbour takes whatever space remains.
    // When the minimums along a split sum to more than the parent, neither window
    // is shrunk: the later window extends past the parent, and the root extends
    // past the monitor. The windows do not overlap.
    private void HonorMinimums(Node node)
    {
        if (node.First is null || node.Second is null)
        {
            if (node.Parent is null && node.Id is not null)
                GrowLeaf(node);
            return;
        }

        var horizontal = !node.SplitTop;
        var crossNeed = Math.Max(Need(node.First, !horizontal), Need(node.Second, !horizontal));
        if (horizontal)
        {
            if (crossNeed > node.Box.Height)
                node.Box = new Rectangle(node.Box.X, node.Box.Y, node.Box.Width, crossNeed);
        }
        else if (crossNeed > node.Box.Width)
        {
            node.Box = new Rectangle(node.Box.X, node.Box.Y, crossNeed, node.Box.Height);
        }

        var available = horizontal ? node.Box.Width : node.Box.Height;
        var firstNeed = Need(node.First, horizontal);
        var secondNeed = Need(node.Second, horizontal);
        var firstHave = horizontal ? node.First.Box.Width : node.First.Box.Height;
        var secondHave = horizontal ? node.Second.Box.Width : node.Second.Box.Height;
        int firstSpan;
        int secondSpan;
        if (firstNeed + secondNeed > available)
        {
            firstSpan = firstNeed;
            secondSpan = secondNeed;
        }
        else
        {
            var ideal = FirstShare(available, node.SplitRatio);
            if (ideal < firstNeed)
                firstSpan = firstNeed;
            else if (available - ideal < secondNeed)
                firstSpan = available - secondNeed;
            else
                firstSpan = ideal;

            secondSpan = available - firstSpan;
        }

        var crossHave = horizontal ? node.First.Box.Height : node.First.Box.Width;
        var crossNow = horizontal ? node.Box.Height : node.Box.Width;
        if (firstSpan == firstHave && secondSpan == secondHave && crossHave == crossNow && firstSpan + secondSpan <= available)
            return;

        if (firstSpan + secondSpan > available)
        {
            if (horizontal)
                node.Box = new Rectangle(node.Box.X, node.Box.Y, firstSpan + secondSpan, node.Box.Height);
            else
                node.Box = new Rectangle(node.Box.X, node.Box.Y, node.Box.Width, firstSpan + secondSpan);
        }

        if (horizontal)
        {
            node.First.Box = new Rectangle(node.Box.X, node.Box.Y, firstSpan, node.Box.Height);
            node.Second.Box = new Rectangle(node.Box.X + firstSpan, node.Box.Y, secondSpan, node.Box.Height);
        }
        else
        {
            node.First.Box = new Rectangle(node.Box.X, node.Box.Y, node.Box.Width, firstSpan);
            node.Second.Box = new Rectangle(node.Box.X, node.Box.Y + firstSpan, node.Box.Width, secondSpan);
        }

        Recalculate(node.First, keepDirection: true);
        Recalculate(node.Second, keepDirection: true);
        HonorMinimums(node.First);
        HonorMinimums(node.Second);
    }

    private void GrowLeaf(Node node)
    {
        if (node.Id is not { } id || !_limits.TryGetValue(id, out var limits) || limits.Min is not { } min)
            return;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var chrome = Chrome(node.Box);
            var width = AtLeast(node.Box.Width, min.Width, chrome.Horizontal);
            var height = AtLeast(node.Box.Height, min.Height, chrome.Vertical);
            if (width == node.Box.Width && height == node.Box.Height)
                return;

            node.Box = new Rectangle(node.Box.X, node.Box.Y, width, height);
        }
    }

    private int Need(Node node, bool horizontal)
    {
        if (node.Id is { } id)
        {
            if (!_limits.TryGetValue(id, out var limits) || limits.Min is not { } min)
                return 0;

            var want = horizontal ? min.Width : min.Height;
            var chrome = Chrome(node.Box);
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

    private (int Horizontal, int Vertical) Chrome(Rectangle box)
    {
        var area = TilingArea();
        var gaps = _options.GapsIn;
        var left = box.X == area.X ? 0 : gaps.Left;
        var top = box.Y == area.Y ? 0 : gaps.Top;
        var right = box.X + box.Width == area.X + area.Width ? 0 : gaps.Right;
        var bottom = box.Y + box.Height == area.Y + area.Height ? 0 : gaps.Bottom;
        var border = _options.BorderSize;
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

    private static Rectangle ClampToLimits(Rectangle bounds, Size? min, Size? max)
    {
        var width = bounds.Width;
        var height = bounds.Height;
        if (max is { } hi)
        {
            if (hi.Width < int.MaxValue && width > hi.Width)
                width = hi.Width;
            if (hi.Height < int.MaxValue && height > hi.Height)
                height = hi.Height;
        }

        if (min is { } lo)
        {
            if (lo.Width > 0 && width < lo.Width)
                width = lo.Width;
            if (lo.Height > 0 && height < lo.Height)
                height = lo.Height;
        }

        if (width == bounds.Width && height == bounds.Height)
            return bounds;

        return new Rectangle(bounds.X, bounds.Y, width, height);
    }

    private readonly record struct ClientLimits(Size? Min, Size? Max);
}
