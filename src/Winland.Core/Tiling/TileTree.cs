namespace Winland.Core.Tiling;

/// <summary>A node in one workspace's tile tree.</summary>
internal abstract class TileNode;

/// <summary>One tile. A group is several windows sharing that tile.</summary>
internal sealed class TileLeaf : TileNode
{
    public List<long> Members { get; set; } = [];

    public int Active { get; set; }

    public long ActiveId => Members[Active];
}

/// <summary>A split between two subtrees.</summary>
internal sealed class TileSplit : TileNode
{
    public TileSplit(TileNode first, TileNode second, bool sideBySide)
    {
        First = first;
        Second = second;
        SideBySide = sideBySide;
    }

    public TileNode First { get; set; }

    public TileNode Second { get; set; }

    public bool SideBySide { get; set; }

    public double Ratio { get; set; } = 0.5;
}

/// <summary>Inserts, removes, and rewires the leaves of one workspace.</summary>
internal sealed class TileTree
{
    private readonly Dictionary<long, TileLeaf> _leafByWindow = [];
    private TileNode? _root;

    public TileNode? Root => _root;

    public IReadOnlyCollection<long> Windows => _leafByWindow.Keys;

    public bool Contains(long id) => _leafByWindow.ContainsKey(id);

    /// <summary>True when a tile holds more than one window.</summary>
    public bool Grouped
    {
        get
        {
            foreach (var leaf in _leafByWindow.Values)
            {
                if (leaf.Members.Count > 1)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool TryGet(long id, out TileLeaf leaf) => _leafByWindow.TryGetValue(id, out leaf!);

    /// <summary>
    /// Adds <paramref name="id"/> as its own tile.
    /// When <paramref name="beside"/> is already in the tree, that tile splits; otherwise the new tile splits the root.
    /// </summary>
    public void Insert(long id, long? beside, bool sideBySide)
    {
        var leaf = new TileLeaf();
        leaf.Members.Add(id);
        _leafByWindow[id] = leaf;
        if (_root is null)
        {
            _root = leaf;
        }
        else if (beside is long focused && _leafByWindow.TryGetValue(focused, out var focusedLeaf))
        {
            Replace(focusedLeaf, new TileSplit(focusedLeaf, leaf, sideBySide));
        }
        else
        {
            _root = new TileSplit(_root, leaf, sideBySide);
        }
    }

    /// <summary>Takes <paramref name="root"/> as the whole tree. The tree must be empty.</summary>
    public void Adopt(TileNode root)
    {
        if (_root is not null)
        {
            return;
        }

        _root = root;
        foreach (var leaf in Leaves(root))
        {
            foreach (var id in leaf.Members)
            {
                _leafByWindow[id] = leaf;
            }
        }
    }

    /// <summary>Adds <paramref name="id"/> to the group that holds <paramref name="host"/>.</summary>
    public void Join(long host, long id)
    {
        var hostLeaf = _leafByWindow[host];
        hostLeaf.Members.Add(id);
        hostLeaf.Active = hostLeaf.Members.Count - 1;
        _leafByWindow[id] = hostLeaf;
    }

    /// <summary>Forgets <paramref name="id"/> and promotes its sibling when a split loses a child.</summary>
    public void Remove(long id)
    {
        if (!_leafByWindow.TryGetValue(id, out var leaf))
        {
            return;
        }

        DetachMember(leaf, id);
    }

    private void DetachMember(TileLeaf leaf, long id)
    {
        var index = leaf.Members.IndexOf(id);
        if (index < 0)
        {
            return;
        }

        leaf.Members.RemoveAt(index);
        _leafByWindow.Remove(id);
        if (leaf.Members.Count == 0)
        {
            Detach(leaf);
        }
        else if (index < leaf.Active)
        {
            leaf.Active--;
        }
        else if (leaf.Active >= leaf.Members.Count)
        {
            leaf.Active = leaf.Members.Count - 1;
        }
    }

    /// <summary>Swaps the windows of two tiles.</summary>
    public void Exchange(TileLeaf a, TileLeaf b)
    {
        (a.Members, b.Members) = (b.Members, a.Members);
        (a.Active, b.Active) = (b.Active, a.Active);
        foreach (var id in a.Members)
        {
            _leafByWindow[id] = a;
        }

        foreach (var id in b.Members)
        {
            _leafByWindow[id] = b;
        }
    }

    /// <summary>
    /// Pulls <paramref name="id"/> out of its tile and splits the tile that holds <paramref name="beside"/>.
    /// When <paramref name="asFirst"/> is set, the window takes the left or top half.
    /// </summary>
    public void Reinsert(long id, long beside, bool sideBySide, bool asFirst)
    {
        if (!_leafByWindow.TryGetValue(id, out var leaf)
            || !_leafByWindow.TryGetValue(beside, out var host)
            || ReferenceEquals(leaf, host))
        {
            return;
        }

        DetachMember(leaf, id);
        Split(id, beside, sideBySide, asFirst);
    }

    /// <summary>
    /// Splits the tile that holds <paramref name="beside"/> with <paramref name="id"/>.
    /// <paramref name="id"/> must not already be in the tree.
    /// </summary>
    public void Split(long id, long beside, bool sideBySide, bool asFirst)
    {
        if (_leafByWindow.ContainsKey(id) || !_leafByWindow.TryGetValue(beside, out var host))
        {
            return;
        }

        var born = new TileLeaf();
        born.Members.Add(id);
        _leafByWindow[id] = born;
        Replace(host, asFirst ? new TileSplit(born, host, sideBySide) : new TileSplit(host, born, sideBySide));
    }

    /// <summary>
    /// Moves <paramref name="id"/> into the group that holds <paramref name="host"/>.
    /// A host that is not a group is left alone, unless <paramref name="lone"/> lets the two become one.
    /// </summary>
    /// <param name="id">The window that moves.</param>
    /// <param name="host">A window in the tile it moves into.</param>
    /// <param name="lone">True when a host that is a single window takes it too.</param>
    /// <returns>True when the window joined the group.</returns>
    public bool MoveInto(long id, long host, bool lone = false)
    {
        if (!_leafByWindow.TryGetValue(id, out var from)
            || !_leafByWindow.TryGetValue(host, out var into)
            || ReferenceEquals(from, into)
            || (into.Members.Count < 2 && !lone))
        {
            return false;
        }

        DetachMember(from, id);
        into.Members.Add(id);
        into.Active = into.Members.Count - 1;
        _leafByWindow[id] = into;
        return true;
    }

    /// <summary>
    /// Makes <paramref name="members"/> one group in the tile that holds <paramref name="shown"/>, in the order given.
    /// Each member leaves the tile it was in, and a tile left with none goes.
    /// </summary>
    /// <param name="members">Windows the tree holds, at least two, with <paramref name="shown"/> among them.</param>
    /// <param name="shown">The window the group shows.</param>
    public void Assemble(IReadOnlyList<long> members, long shown)
    {
        foreach (var id in members)
        {
            if (id != shown)
            {
                MoveInto(id, shown, lone: true);
            }
        }

        var leaf = _leafByWindow[shown];
        leaf.Members = [.. members];
        leaf.Active = leaf.Members.IndexOf(shown);
    }

    /// <summary>Pulls <paramref name="id"/> out of its group into a new tile beside that group.</summary>
    public void Leave(long id)
    {
        if (!_leafByWindow.TryGetValue(id, out var leaf) || leaf.Members.Count < 2)
        {
            return;
        }

        leaf.Members.Remove(id);
        if (leaf.Active >= leaf.Members.Count)
        {
            leaf.Active = leaf.Members.Count - 1;
        }

        var born = new TileLeaf();
        born.Members.Add(id);
        _leafByWindow[id] = born;
        Replace(leaf, new TileSplit(leaf, born, sideBySide: true));
    }

    /// <summary>Turns a group into a stack of single-window tiles, keeping the active window in place.</summary>
    public void Disassemble(TileLeaf leaf)
    {
        var keep = leaf.ActiveId;
        var others = leaf.Members.Where(id => id != keep).ToList();
        leaf.Members.Clear();
        leaf.Members.Add(keep);
        leaf.Active = 0;
        TileNode anchor = leaf;
        foreach (var id in others)
        {
            var born = new TileLeaf();
            born.Members.Add(id);
            _leafByWindow[id] = born;
            var split = new TileSplit(anchor, born, sideBySide: false);
            Replace(anchor, split);
            anchor = born;
        }
    }

    public TileSplit? ParentOf(TileNode child)
    {
        if (_root is null)
        {
            return null;
        }

        return FindParent(_root, child);
    }

    public static IEnumerable<TileLeaf> Leaves(TileNode node)
    {
        if (node is TileLeaf leaf)
        {
            yield return leaf;
            yield break;
        }

        if (node is TileSplit split)
        {
            foreach (var item in Leaves(split.First))
            {
                yield return item;
            }

            foreach (var item in Leaves(split.Second))
            {
                yield return item;
            }
        }
    }

    private void Detach(TileLeaf leaf)
    {
        if (ReferenceEquals(_root, leaf))
        {
            _root = null;
            return;
        }

        var parent = ParentOf(leaf);
        if (parent is null || _root is null)
        {
            return;
        }

        var sibling = ReferenceEquals(parent.First, leaf) ? parent.Second : parent.First;
        if (ReferenceEquals(_root, parent))
        {
            _root = sibling;
            return;
        }

        var grand = ParentOf(parent);
        if (grand is null)
        {
            _root = sibling;
            return;
        }

        if (ReferenceEquals(grand.First, parent))
        {
            grand.First = sibling;
        }
        else
        {
            grand.Second = sibling;
        }
    }

    private void Replace(TileNode target, TileNode replacement)
    {
        if (ReferenceEquals(_root, target))
        {
            _root = replacement;
            return;
        }

        var parent = ParentOf(target);
        if (parent is null)
        {
            _root = replacement;
            return;
        }

        if (ReferenceEquals(parent.First, target))
        {
            parent.First = replacement;
        }
        else
        {
            parent.Second = replacement;
        }
    }

    private static TileSplit? FindParent(TileNode node, TileNode child)
    {
        if (node is not TileSplit split)
        {
            return null;
        }

        if (ReferenceEquals(split.First, child) || ReferenceEquals(split.Second, child))
        {
            return split;
        }

        return FindParent(split.First, child) ?? FindParent(split.Second, child);
    }
}
