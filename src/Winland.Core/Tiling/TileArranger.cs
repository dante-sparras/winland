namespace Winland.Core.Tiling;

/// <summary>
/// Turns a tile tree into pixel placements. Every tile stays inside the work area and is at least
/// as large as the minimum its window keeps, so no window paints on a neighbor.
/// A tree that holds every minimum is laid out as it is. Otherwise the fewest splits change direction,
/// unless a grid gives the smallest tile a longer side. When a grid cannot hold every minimum either,
/// the windows that do not fit share the last cell, which shows one of them, the way a Group does.
/// </summary>
internal static class TileArranger
{
    public static IReadOnlyList<Placement> Arrange(
        TileTree tree,
        LayoutKind layout,
        bool spacingEnabled,
        int spacing,
        long? focused,
        long? fullscreen,
        IReadOnlySet<long> floated,
        IReadOnlySet<long> fullWidth,
        IReadOnlyDictionary<long, WindowMinimum> minimums,
        IReadOnlyDictionary<long, WindowMinimum> pseudos,
        TileRect area,
        TileRect cover,
        IReadOnlyList<long> recent,
        int strip = 0,
        List<long>? shared = null,
        bool squareTile = false)
    {
        shared?.Clear();
        if (tree.Root is null)
        {
            return [];
        }

        var result = new List<Placement>();
        if (layout == LayoutKind.Scrolling)
        {
            ArrangeScrolling(tree, spacingEnabled, spacing, focused, floated, fullWidth, minimums, pseudos, area, result);
        }
        else
        {
            var plan = new Plan(tree, spacingEnabled ? spacing : 0, focused, floated, fullWidth, minimums, pseudos, area, recent, strip, shared);
            ArrangeDwindle(plan, result);
        }

        if (squareTile)
        {
            SquareTheOne(tree, floated, fullWidth, minimums, area, result);
        }

        ListWideLast(tree, floated, fullWidth, result);

        if (fullscreen is long coverId && tree.Contains(coverId))
        {
            // A floated window is left out of the tiles above. It still has to cover the monitor, or the bar yields to nothing.
            var found = false;
            for (var i = 0; i < result.Count; i++)
            {
                if (result[i].Id == coverId)
                {
                    result[i] = new Placement(coverId, cover.X, cover.Y, cover.Width, cover.Height, true, true);
                    found = true;
                }
            }

            if (!found)
            {
                result.Add(new Placement(coverId, cover.X, cover.Y, cover.Width, cover.Height, true, true));
            }
        }

        return result;
    }

    /// <summary>
    /// Draws the one tiled window as a square as tall as <paramref name="area"/>, centered. A window that will not shrink
    /// to the square gets the width it accepts, never more than the area. A workspace with two or more tiled windows, and
    /// a full-width or natural-size window, is left as placed.
    /// </summary>
    private static void SquareTheOne(
        TileTree tree,
        IReadOnlySet<long> floated,
        IReadOnlySet<long> fullWidth,
        IReadOnlyDictionary<long, WindowMinimum> minimums,
        TileRect area,
        List<Placement> result)
    {
        long? only = null;
        foreach (var leaf in TileTree.Leaves(tree.Root!))
        {
            foreach (var id in leaf.Members)
            {
                if (floated.Contains(id))
                {
                    continue;
                }

                if (only is not null)
                {
                    return;
                }

                only = id;
            }
        }

        if (only is not long id1 || fullWidth.Contains(id1))
        {
            return;
        }

        var index = result.FindIndex(item => item.Id == id1);
        if (index < 0 || result[index].Width != area.Width || result[index].Height != area.Height)
        {
            // Placed as something other than the full tile, as a natural-size window is.
            return;
        }

        var accepted = minimums.TryGetValue(id1, out var minimum) ? minimum.Width : 1;
        var width = Math.Min(area.Width, Math.Max(area.Height, accepted));
        var placed = result[index];
        result[index] = placed with { X = area.X + ((area.Width - width) / 2), Y = area.Y, Width = width, Height = area.Height };
    }

    /// <summary>
    /// Lists a full-width tile after the others. It is drawn over the tiles it covers,
    /// and the last tile at a point is the one <see cref="TileHit.At"/> finds there.
    /// </summary>
    private static void ListWideLast(TileTree tree, IReadOnlySet<long> floated, IReadOnlySet<long> fullWidth, List<Placement> result)
    {
        if (fullWidth.Count == 0)
        {
            return;
        }

        HashSet<long>? wide = null;
        foreach (var leaf in TileTree.Leaves(tree.Root!))
        {
            if (leaf.Members.Any(id => fullWidth.Contains(id) && !floated.Contains(id)))
            {
                (wide ??= []).UnionWith(leaf.Members);
            }
        }

        if (wide is null)
        {
            return;
        }

        var over = result.FindAll(item => wide.Contains(item.Id));
        result.RemoveAll(item => wide.Contains(item.Id));
        result.AddRange(over);
    }

    private static void ArrangeScrolling(
        TileTree tree,
        bool spacingEnabled,
        int spacing,
        long? focused,
        IReadOnlySet<long> floated,
        IReadOnlySet<long> fullWidth,
        IReadOnlyDictionary<long, WindowMinimum> minimums,
        IReadOnlyDictionary<long, WindowMinimum> pseudos,
        TileRect area,
        List<Placement> result)
    {
        var columns = TileTree.Leaves(tree.Root!).Where(leaf => leaf.Members.Any(id => !floated.Contains(id))).ToList();
        if (columns.Count == 0)
        {
            return;
        }

        var gap = spacingEnabled ? spacing : 0;
        var widest = 1;
        foreach (var column in columns)
        {
            widest = Math.Max(widest, LeafMinimum(column, floated, minimums).Width);
        }

        var focusIndex = 0;
        if (focused is long focusedId)
        {
            var index = columns.FindIndex(leaf => leaf.Members.Contains(focusedId));
            if (index >= 0)
            {
                focusIndex = index;
            }
        }

        var columnWidth = columns.Count == 1
            ? area.Width
            : Math.Max(widest, area.Width * 49 / 100);
        var origin = columns.Count == 1 ? area.X : area.X - (focusIndex * (columnWidth + gap));
        for (var i = 0; i < columns.Count; i++)
        {
            var rect = new TileRect(origin + (i * (columnWidth + gap)), area.Y, columnWidth, area.Height);
            PlaceLeaf(tree, columns[i], floated, fullWidth, pseudos, rect, area, result);
        }
    }

    private static void ArrangeDwindle(Plan plan, List<Placement> result)
    {
        var root = plan.Tree.Root!;
        var shapes = Shapes(root, plan);
        if (shapes is null)
        {
            return;
        }

        var chosen = -1;
        for (var i = 0; i < shapes.Count; i++)
        {
            if (chosen < 0 || shapes[i].Cost < shapes[chosen].Cost)
            {
                chosen = i;
            }
        }

        if (chosen < 0)
        {
            ArrangeGrid(plan, result, plan.Shared);
            return;
        }

        var dwindle = new List<Placement>();
        PlaceShape(root, chosen, plan.Area, plan, dwindle);
        if (shapes[chosen].Cost == 0)
        {
            result.AddRange(dwindle);
            return;
        }

        // Every tile of this grid is visible when it wins, so no window shares a cell in it.
        var grid = new List<Placement>();
        ArrangeGrid(plan, grid, null);
        var gridWins = grid.Count(item => item.Visible) == dwindle.Count(item => item.Visible)
            && SmallestSide(grid) > SmallestSide(dwindle);
        result.AddRange(gridWins ? grid : dwindle);
    }

    private static int SmallestSide(List<Placement> placed) =>
        placed.Where(item => item.Visible).Select(item => Math.Min(item.Width, item.Height)).DefaultIfEmpty(0).Min();

    /// <summary>
    /// Every way <paramref name="node"/> can be laid out inside the work area, keeping the cheapest
    /// for each size. Flipping a split costs one for each tile under it, so a deep flip is preferred.
    /// Null when the node holds no tiled window.
    /// </summary>
    private static List<Shape>? Shapes(TileNode node, Plan plan)
    {
        if (plan.Shapes.TryGetValue(node, out var known))
        {
            return known;
        }

        List<Shape>? result = null;
        switch (node)
        {
            case TileLeaf leaf when Tiled(leaf, plan.Floated):
                var minimum = plan.Minimum(leaf);
                result = [new Shape(minimum.Width, minimum.Height, 0, -1, -1, false)];
                break;
            case TileSplit split:
                var first = Shapes(split.First, plan);
                var second = Shapes(split.Second, plan);
                if (first is null || second is null)
                {
                    result = first ?? second;
                    break;
                }

                var flipCost = TileTree.Leaves(split).Count(leaf => Tiled(leaf, plan.Floated));
                result = [];
                for (var i = 0; i < first.Count; i++)
                {
                    for (var j = 0; j < second.Count; j++)
                    {
                        Combine(result, first[i], second[j], i, j, split.SideBySide, 0, plan);
                        Combine(result, first[i], second[j], i, j, !split.SideBySide, flipCost, plan);
                    }
                }

                break;
        }

        plan.Shapes[node] = result;
        return result;
    }

    private static void Combine(List<Shape> front, Shape a, Shape b, int i, int j, bool sideBySide, int flipCost, Plan plan)
    {
        var width = sideBySide ? a.Width + b.Width + plan.Gap : Math.Max(a.Width, b.Width);
        var height = sideBySide ? Math.Max(a.Height, b.Height) : a.Height + b.Height + plan.Gap;
        if (width > plan.Area.Width || height > plan.Area.Height)
        {
            return;
        }

        var shape = new Shape(width, height, a.Cost + b.Cost + flipCost, i, j, sideBySide);
        foreach (var kept in front)
        {
            if (kept.Width <= shape.Width && kept.Height <= shape.Height && kept.Cost <= shape.Cost)
            {
                return;
            }
        }

        front.RemoveAll(kept => shape.Width <= kept.Width && shape.Height <= kept.Height && shape.Cost <= kept.Cost);
        front.Add(shape);
    }

    private static void PlaceShape(TileNode node, int index, TileRect area, Plan plan, List<Placement> result)
    {
        switch (node)
        {
            case TileLeaf leaf:
                PlaceLeaf(plan.Tree, leaf, plan.Floated, plan.FullWidth, plan.Pseudos, area, plan.Area, result);
                break;
            case TileSplit split:
                var first = plan.Shapes[split.First];
                var second = plan.Shapes[split.Second];
                if (first is null)
                {
                    PlaceShape(split.Second, index, area, plan, result);
                    break;
                }

                if (second is null)
                {
                    PlaceShape(split.First, index, area, plan, result);
                    break;
                }

                var shape = plan.Shapes[split]![index];
                var firstNeed = first[shape.First];
                var secondNeed = second[shape.Second];
                if (shape.SideBySide)
                {
                    var usable = Math.Max(0, area.Width - plan.Gap);
                    var firstWidth = Share(usable, split.Ratio, firstNeed.Width, secondNeed.Width);
                    var secondWidth = Math.Max(0, usable - firstWidth);
                    PlaceShape(split.First, shape.First, area with { Width = firstWidth }, plan, result);
                    PlaceShape(split.Second, shape.Second, area with { X = area.X + firstWidth + plan.Gap, Width = secondWidth }, plan, result);
                }
                else
                {
                    var usable = Math.Max(0, area.Height - plan.Gap);
                    var firstHeight = Share(usable, split.Ratio, firstNeed.Height, secondNeed.Height);
                    var secondHeight = Math.Max(0, usable - firstHeight);
                    PlaceShape(split.First, shape.First, area with { Height = firstHeight }, plan, result);
                    PlaceShape(split.Second, shape.Second, area with { Y = area.Y + firstHeight + plan.Gap, Height = secondHeight }, plan, result);
                }

                break;
        }
    }

    /// <summary>
    /// Lays the tiles out in columns, in tree order. Starting from one cell per tile, the last cell
    /// takes one more tile each time the minimums still do not fit. The shared cell shows the tile used last of those in it,
    /// so it keeps showing that one while the keyboard is on another tile.
    /// <paramref name="shared"/> is given the windows of that cell, in tree order, when more than one tile is in it.
    /// </summary>
    private static void ArrangeGrid(Plan plan, List<Placement> result, List<long>? shared)
    {
        var leaves = TileTree.Leaves(plan.Tree.Root!).Where(leaf => Tiled(leaf, plan.Floated)).ToList();
        for (var cells = leaves.Count; cells >= 1; cells--)
        {
            var needs = new List<(int Width, int Height)>(cells);
            for (var i = 0; i < cells - 1; i++)
            {
                needs.Add(plan.Minimum(leaves[i]));
            }

            var lastWidth = 1;
            var lastHeight = 1;
            for (var i = cells - 1; i < leaves.Count; i++)
            {
                var minimum = plan.Minimum(leaves[i]);
                lastWidth = Math.Max(lastWidth, minimum.Width);
                lastHeight = Math.Max(lastHeight, minimum.Height);
            }

            // A shared cell keeps a strip free along its top, and the windows under it keep their size.
            if (cells < leaves.Count)
            {
                lastHeight = Math.Min(lastHeight + plan.Strip, Math.Max(1, plan.Area.Height));
            }

            needs.Add((lastWidth, lastHeight));
            var rects = Grid(needs, plan.Area, plan.Gap);
            if (rects is null)
            {
                continue;
            }

            var shown = UsedLast(leaves, cells - 1, plan);
            for (var i = 0; i < leaves.Count; i++)
            {
                var rect = rects[Math.Min(i, cells - 1)];
                if (i < cells - 1 || i == shown)
                {
                    PlaceLeaf(plan.Tree, leaves[i], plan.Floated, plan.FullWidth, plan.Pseudos, rect, plan.Area, result);
                    continue;
                }

                foreach (var id in leaves[i].Members)
                {
                    // A floated member is not a tile, as in PlaceLeaf. Hidden here, it would leave the glass with the shared tile.
                    if (plan.Floated.Contains(id))
                    {
                        continue;
                    }

                    result.Add(new Placement(id, rect.X, rect.Y, rect.Width, rect.Height, false));
                }
            }

            if (shared is not null && cells < leaves.Count)
            {
                for (var i = cells - 1; i < leaves.Count; i++)
                {
                    foreach (var id in leaves[i].Members)
                    {
                        if (!plan.Floated.Contains(id))
                        {
                            shared.Add(id);
                        }
                    }
                }
            }

            return;
        }
    }

    /// <summary>The tile from <paramref name="first"/> on that holds the focused window, or else the window used last. The first of them when none was used.</summary>
    private static int UsedLast(List<TileLeaf> leaves, int first, Plan plan)
    {
        if (plan.Focused is long focused && leaves.FindIndex(first, leaf => leaf.Members.Contains(focused)) is >= 0 and var held)
        {
            return held;
        }

        foreach (var id in plan.Recent)
        {
            if (!plan.Floated.Contains(id) && leaves.FindIndex(first, leaf => leaf.Members.Contains(id)) is >= 0 and var used)
            {
                return used;
            }
        }

        return first;
    }

    /// <summary>
    /// The column count whose smallest cell side is largest, with every cell at least its need.
    /// Null when no column count fits.
    /// </summary>
    private static List<TileRect>? Grid(List<(int Width, int Height)> needs, TileRect area, int gap)
    {
        List<TileRect>? best = null;
        var bestScore = -1;
        var count = needs.Count;
        for (var columns = 1; columns <= count; columns++)
        {
            var spans = new List<(int Start, int Rows)>(columns);
            var start = 0;
            for (var column = 0; column < columns; column++)
            {
                var rows = (count / columns) + (column < count % columns ? 1 : 0);
                spans.Add((start, rows));
                start += rows;
            }

            var widthNeeds = spans.Select(span => needs.Skip(span.Start).Take(span.Rows).Max(need => need.Width)).ToList();
            var usableWidth = area.Width - (gap * (columns - 1));
            if (widthNeeds.Sum() > usableWidth)
            {
                continue;
            }

            var widths = Distribute(usableWidth, widthNeeds);
            var rects = new List<TileRect>(count);
            var fits = true;
            var score = int.MaxValue;
            var x = area.X;
            for (var column = 0; column < columns && fits; column++)
            {
                var (first, rows) = spans[column];
                var heightNeeds = needs.Skip(first).Take(rows).Select(need => need.Height).ToList();
                var usableHeight = area.Height - (gap * (rows - 1));
                if (heightNeeds.Sum() > usableHeight)
                {
                    fits = false;
                    break;
                }

                var heights = Distribute(usableHeight, heightNeeds);
                var y = area.Y;
                foreach (var height in heights)
                {
                    rects.Add(new TileRect(x, y, widths[column], height));
                    score = Math.Min(score, Math.Min(widths[column], height));
                    y += height + gap;
                }

                x += widths[column] + gap;
            }

            if (fits && score > bestScore)
            {
                best = rects;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>Splits <paramref name="total"/> as evenly as the needs allow. The sizes add up to the total.</summary>
    private static int[] Distribute(int total, List<int> needs)
    {
        var count = needs.Count;
        var pinned = new bool[count];
        while (true)
        {
            var free = 0;
            var remaining = total;
            for (var i = 0; i < count; i++)
            {
                if (pinned[i])
                {
                    remaining -= needs[i];
                }
                else
                {
                    free++;
                }
            }

            var share = free == 0 ? 0 : remaining / free;
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                if (!pinned[i] && needs[i] > share)
                {
                    pinned[i] = true;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        var sizes = new int[count];
        var left = total;
        var open = 0;
        for (var i = 0; i < count; i++)
        {
            if (pinned[i])
            {
                sizes[i] = needs[i];
                left -= needs[i];
            }
            else
            {
                open++;
            }
        }

        var extra = open == 0 ? 0 : left % open;
        for (var i = 0; i < count; i++)
        {
            if (pinned[i])
            {
                continue;
            }

            sizes[i] = (left / open) + (extra > 0 ? 1 : 0);
            extra--;
        }

        return sizes;
    }

    private static void PlaceLeaf(
        TileTree tree,
        TileLeaf leaf,
        IReadOnlySet<long> floated,
        IReadOnlySet<long> fullWidth,
        IReadOnlyDictionary<long, WindowMinimum> pseudos,
        TileRect area,
        TileRect workspace,
        List<Placement> result)
    {
        var visibleMembers = leaf.Members.Where(id => !floated.Contains(id)).ToList();
        if (visibleMembers.Count == 0)
        {
            return;
        }

        // Full width is the workspace's width at the full tile height. The work area is clear of the bar, so the bar stays up.
        // The window shown is kept above the tiles it covers, so one that opens or takes the keyboard is not drawn over it.
        var rect = area;
        var wide = visibleMembers.Any(id => fullWidth.Contains(id)) && tree.Root is not null;
        if (wide)
        {
            rect = workspace;
        }

        var active = visibleMembers.Contains(leaf.ActiveId) ? leaf.ActiveId : visibleMembers[0];
        if (pseudos.TryGetValue(active, out var natural) && natural.Width > 0 && natural.Height > 0)
        {
            var width = natural.Width;
            var height = natural.Height;
            if (width > rect.Width || height > rect.Height)
            {
                var scale = Math.Min(rect.Width / (double)width, rect.Height / (double)height);
                width = Math.Max(1, (int)(width * scale));
                height = Math.Max(1, (int)(height * scale));
            }

            rect = new TileRect(rect.X + ((rect.Width - width) / 2), rect.Y + ((rect.Height - height) / 2), width, height);
        }

        foreach (var id in leaf.Members)
        {
            if (floated.Contains(id))
            {
                continue;
            }

            result.Add(new Placement(id, rect.X, rect.Y, rect.Width, rect.Height, id == active, Above: wide && id == active));
        }
    }

    private static bool Tiled(TileLeaf leaf, IReadOnlySet<long> floated) => leaf.Members.Any(id => !floated.Contains(id));

    private static (int Width, int Height) LeafMinimum(
        TileLeaf leaf,
        IReadOnlySet<long> floated,
        IReadOnlyDictionary<long, WindowMinimum> minimums)
    {
        var width = 1;
        var height = 1;
        foreach (var id in leaf.Members)
        {
            if (floated.Contains(id) || !minimums.TryGetValue(id, out var minimum))
            {
                continue;
            }

            width = Math.Max(width, minimum.Width);
            height = Math.Max(height, minimum.Height);
        }

        return (width, height);
    }

    private static int Share(int usable, double ratio, int firstMin, int secondMin)
    {
        if (usable <= 1)
        {
            return Math.Max(0, usable);
        }

        var preferred = (int)(usable * ratio);
        if (firstMin + secondMin > usable)
        {
            return Math.Clamp(preferred, 1, usable - 1);
        }

        return Math.Clamp(preferred, Math.Max(1, firstMin), usable - Math.Max(1, secondMin));
    }

    /// <summary>One way to lay out a node: its smallest size, what it costs, and the child shapes it uses.</summary>
    private readonly record struct Shape(int Width, int Height, int Cost, int First, int Second, bool SideBySide);

    private sealed class Plan(
        TileTree tree,
        int gap,
        long? focused,
        IReadOnlySet<long> floated,
        IReadOnlySet<long> fullWidth,
        IReadOnlyDictionary<long, WindowMinimum> minimums,
        IReadOnlyDictionary<long, WindowMinimum> pseudos,
        TileRect area,
        IReadOnlyList<long> recent,
        int strip,
        List<long>? shared)
    {
        public TileTree Tree { get; } = tree;

        /// <summary>The height of the strip a shared tile keeps free, in pixels.</summary>
        public int Strip { get; } = strip;

        /// <summary>The windows in the order they last had the focus, the latest first.</summary>
        public IReadOnlyList<long> Recent { get; } = recent;

        /// <summary>Filled with the windows that share the last tile. Null when nobody asked.</summary>
        public List<long>? Shared { get; } = shared;

        public int Gap { get; } = gap;

        public long? Focused { get; } = focused;

        public IReadOnlySet<long> Floated { get; } = floated;

        public IReadOnlySet<long> FullWidth { get; } = fullWidth;

        public IReadOnlyDictionary<long, WindowMinimum> Pseudos { get; } = pseudos;

        public TileRect Area { get; } = area;

        public Dictionary<TileNode, List<Shape>?> Shapes { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>The leaf's minimum, never larger than the work area.</summary>
        public (int Width, int Height) Minimum(TileLeaf leaf)
        {
            var minimum = LeafMinimum(leaf, Floated, minimums);
            return (Math.Clamp(minimum.Width, 1, Math.Max(1, Area.Width)), Math.Clamp(minimum.Height, 1, Math.Max(1, Area.Height)));
        }
    }
}
