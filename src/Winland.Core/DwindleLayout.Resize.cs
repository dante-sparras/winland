namespace Winland;

internal sealed partial class DwindleLayout
{
    public IReadOnlyList<Placement> ResizeActive(WindowId window, string arguments)
    {
        if (!ResizeArguments.TryParse(arguments, out var x, out var y, out var exact))
            return [];

        var id = new ClientId(window.Value);
        if (Minimized(id))
            return [];

        if (_floating.Contains(id))
            return _floating.Resize(id, x, y, exact);

        if (!_nodes.TryGetValue(id, out var node) || !InTree(node) || node.Parent is null)
            return [];

        // exact names the window's size, which is the tile less its gaps and border.
        var shown = Present(id, node.Box);
        return ResizeTile(node, exact ? x - shown.Width : x, exact ? y - shown.Height : y, corner: null);
    }

    /// <summary>
    /// Moves the split edges of a tile by a pixel delta. A mouse resize names the grabbed corner;
    /// a dispatcher resize names none. With smart_resizing the corner picks which edges move.
    /// </summary>
    private IReadOnlyList<Placement> ResizeTile(Node node, int deltaX, int deltaY, (bool Left, bool Top)? corner)
    {
        if (node.Parent is null)
            return [];

        var box = node.Box;
        var area = TilingArea();
        var edges = new TileEdges(
            Sticks(box.X, area.X),
            Sticks(box.Y, area.Y),
            Sticks(box.X + box.Width, area.X + area.Width),
            Sticks(box.Y + box.Height, area.Y + area.Height));
        if (edges.Left && edges.Right)
            deltaX = 0;
        if (edges.Top && edges.Bottom)
            deltaY = 0;

        if (_options.SmartResizing)
            SmartResize(node, deltaX, deltaY, corner, edges);
        else
            ResizeByParents(node, deltaX, deltaY);

        Refit();
        return Placements();
    }

    private static void ResizeByParents(Node node, int deltaX, int deltaY)
    {
        var parent = node.Parent!;
        var sideBySide = !parent.SplitTop;
        var other = parent.Parent;
        while (other is not null && other.SplitTop == !sideBySide)
            other = other.Parent;

        if (other is null)
        {
            if (sideBySide)
                Grow(parent, deltaX, horizontal: true);
            else
                Grow(parent, deltaY, horizontal: false);
            return;
        }

        var side = sideBySide ? parent : other;
        var top = sideBySide ? other : parent;
        Grow(side, deltaX, horizontal: true);
        Grow(top, deltaY, horizontal: false);
    }

    // The outer split on each axis is the one whose divider is the grabbed edge. The inner split
    // below it is the tile's other edge on that axis, which is kept where it was.
    private void SmartResize(Node node, int deltaX, int deltaY, (bool Left, bool Top)? corner, TileEdges edges)
    {
        var none = corner is null;
        var left = corner is { Left: true } || edges.Right;
        var top = corner is { Top: true } || edges.Bottom;
        var right = corner is { Left: false } || edges.Left;
        var bottom = corner is { Top: false } || edges.Top;

        Node? verticalOuter = null;
        Node? verticalInner = null;
        Node? horizontalOuter = null;
        Node? horizontalInner = null;
        for (var current = node; current.Parent is { } parent; current = parent)
        {
            var first = ReferenceEquals(parent.First, current);
            if (verticalOuter is null && parent.SplitTop && (none || (top && !first) || (bottom && first)))
                verticalOuter = current;
            else if (verticalOuter is null && verticalInner is null && parent.SplitTop)
                verticalInner = current;
            else if (horizontalOuter is null && !parent.SplitTop && (none || (left && !first) || (right && first)))
                horizontalOuter = current;
            else if (horizontalOuter is null && horizontalInner is null && !parent.SplitTop)
                horizontalInner = current;

            if (verticalOuter is not null && horizontalOuter is not null)
                break;
        }

        if (horizontalOuter is not null)
            MoveEdge(horizontalOuter, horizontalInner, deltaX, horizontal: true);
        if (verticalOuter is not null)
            MoveEdge(verticalOuter, verticalInner, deltaY, horizontal: false);
    }

    private void MoveEdge(Node outer, Node? inner, int delta, bool horizontal)
    {
        if (delta == 0)
            return;

        var outerParent = outer.Parent!;
        var original = inner is null ? 0 : Extent(inner.Box, horizontal);
        Grow(outerParent, delta, horizontal);
        Recalculate(outerParent);
        if (inner?.Parent is not { } innerParent)
            return;

        var size = Extent(innerParent.Box, horizontal);
        if (size == 0)
            return;

        var ratio = ReferenceEquals(innerParent.First, inner)
            ? (original - delta) * 2.0 / size
            : 2 - ((original + delta) * 2.0 / size);
        innerParent.SplitRatio = Math.Clamp(ratio, 0.1, 1.9);
        Recalculate(innerParent);
    }

    private static int Extent(Rectangle box, bool horizontal) => horizontal ? box.Width : box.Height;

    private static void Grow(Node node, int delta, bool horizontal)
    {
        var size = Extent(node.Box, horizontal);
        if (delta == 0 || size == 0)
            return;

        node.SplitRatio = Math.Clamp(node.SplitRatio + (delta * 2.0 / size), 0.1, 1.9);
    }

    private static bool Sticks(int edge, int other) => Math.Abs(edge - other) <= 1;

    /// <summary>Which sides of a tile lie on the tiling area's edge.</summary>
    private readonly record struct TileEdges(bool Left, bool Top, bool Right, bool Bottom);
}
