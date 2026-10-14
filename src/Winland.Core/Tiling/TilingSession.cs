namespace Winland.Core.Tiling;

/// <summary>
/// Tiles the windows of one workspace. Spacing is off until <see cref="ToggleSpacing"/> is called.
/// Floated windows are left out of the arrangement. One window may be fullscreen.
/// </summary>
public sealed class TilingSession
{
    private readonly TileTree _tree = new();
    private readonly Dictionary<long, WindowMinimum> _minimums = [];
    private readonly Dictionary<long, WindowMinimum> _reported = [];
    private readonly Dictionary<long, WindowMinimum> _kept = [];
    private readonly Dictionary<long, TileRect> _askedIn = [];
    private readonly HashSet<long> _floated = [];
    private readonly List<long> _recent = [];
    private readonly Dictionary<long, WindowMinimum> _pseudo = [];
    private long? _focused;
    private long? _fullscreen;
    private readonly HashSet<long> _fullWidth = [];
    private readonly HashSet<long> _tucked = [];
    private readonly Dictionary<long, int> _unanswered = [];
    private readonly Dictionary<long, int> _savedWidth = [];

    /// <summary>How many places in a row ask a window that does not answer its minimum. After that it is asked again when the area changes, or when it keeps a larger size than its tile.</summary>
    public const int MinimumTries = 3;

    // The tiles last computed, handed out again until something they were computed from changes.
    private int _tilesVersion = -1;
    private TileRect _tilesArea;
    private TileRect _tilesCover;
    private IReadOnlyList<Placement> _tilesMemo = [];

    // The windows that share the last tile in those tiles, in tree order. Empty while every tile fits.
    // Worked out with the tiles and never kept past them, so a window has a tile of its own again as soon as there is room.
    private readonly List<long> _shared = [];

    // The tiles last handed out with each group's strip taken off, and the tiles they were made from.
    private IReadOnlyList<Placement>? _strippedFrom;
    private IReadOnlyList<Placement> _strippedMemo = [];

    /// <summary>
    /// Changes whenever something a placement is computed from changes: the tree, the focus, a minimum, a toggle, the spacing, or the group strip.
    /// The same value twice means <see cref="Arrange(TileRect, TileRect)"/> answers the same tiles for the same rectangles.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>True when a placement hid a window of this workspace and the Host has not listed it since.</summary>
    public bool AnyTucked => _tucked.Count > 0;

    /// <summary>
    /// True when a tile may have a strip to draw: a tile holds more than one window, Super+G is armed,
    /// or the windows that do not fit share the last tile in the area the tiles were last asked for.
    /// </summary>
    public bool HasStrips => GroupBar > 0 && _tree.Windows.Count > 0 && (GroupingArmed || _tree.Grouped || Shares);

    /// <summary>True when the last tile is shared in the area the tiles were last computed for. A session never arranged shares none.</summary>
    private bool Shares
    {
        get
        {
            if (_tilesVersion < 0)
            {
                return false;
            }

            if (_tilesVersion != Version)
            {
                Tiles(_tilesArea, _tilesCover);
            }

            return _shared.Count > 1;
        }
    }

    private void Touch() => Version++;

    /// <summary>Current layout.</summary>
    public LayoutKind Layout { get; private set; } = LayoutKind.Dwindle;

    /// <summary>Whether gutters are drawn between tiles.</summary>
    public bool SpacingEnabled { get; private set; }

    /// <summary>Gutter size in pixels, used only while <see cref="SpacingEnabled"/> is true.</summary>
    public int Spacing { get; private set; } = 8;

    /// <summary>Pixels kept free along the top of a group's tile for the strip that shows its members. Zero keeps none.</summary>
    public int GroupBar { get; private set; }

    /// <summary>Sets <see cref="GroupBar"/>. Values below zero become zero.</summary>
    /// <param name="pixels">The strip's height in pixels.</param>
    public void SetGroupBar(int pixels)
    {
        GroupBar = Math.Max(0, pixels);
        Touch();
    }

    /// <summary>True after Super+G until the group is disassembled or the workspace has no windows.</summary>
    public bool GroupingArmed { get; private set; }

    /// <summary>The focused window, if any.</summary>
    public long? Focused => _focused;

    /// <summary>The window <see cref="ToggleFullscreen"/> placed over the screen, if one is.</summary>
    public long? Fullscreen => _fullscreen;

    /// <summary>Windows the session knows about.</summary>
    public IReadOnlyCollection<long> Windows => _tree.Windows;

    /// <summary>True when <paramref name="id"/> is floated out of its tile.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>True after float was toggled on and not toggled off.</returns>
    public bool Floated(long id) => _floated.Contains(id);

    /// <summary>True when <paramref name="id"/> takes the full width of its workspace, at the full tile height.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>True after full width was toggled on and not toggled off.</returns>
    public bool Wide(long id) => _fullWidth.Contains(id);

    /// <summary>True when <paramref name="id"/> is drawn at its pseudotile size.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>True after pseudotile was toggled on and not toggled off.</returns>
    public bool Pseudo(long id) => _pseudo.ContainsKey(id);

    /// <summary>True when <paramref name="id"/> shares its tile with another window.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>True when the leaf holds more than one member.</returns>
    public bool Grouped(long id) => _tree.TryGet(id, out var leaf) && leaf.Members.Count > 1;

    /// <summary>True when a placement hid <paramref name="id"/> behind another window, as an inactive group member is, and the Host has not listed it since.</summary>
    /// <param name="id">Window identity.</param>
    /// <returns>True from the placement that hid the window until <see cref="NoteListed"/>.</returns>
    public bool Tucked(long id) => _tucked.Contains(id);

    /// <summary>
    /// The Host lists <paramref name="id"/>, so it is on the glass. A window shown again after it was hidden
    /// is listed a moment later, and it stays <see cref="Tucked"/> until then.
    /// </summary>
    /// <param name="id">Window identity.</param>
    public void NoteListed(long id) => _tucked.Remove(id);

    /// <summary>
    /// Records which windows <paramref name="placed"/> hides. The Host stops listing a hidden window,
    /// and without this it would be taken for a closed one.
    /// </summary>
    /// <param name="placed">The placements going onto the glass.</param>
    public void NotePlaced(IReadOnlyList<Placement> placed)
    {
        ArgumentNullException.ThrowIfNull(placed);
        foreach (var item in placed)
        {
            if (!item.Visible && _tree.Contains(item.Id))
            {
                _tucked.Add(item.Id);
            }
        }
    }

    /// <summary>Sets the gutter size. Values below zero become zero.</summary>
    public void SetSpacing(int pixels)
    {
        Spacing = Math.Max(0, pixels);
        Touch();
    }

    /// <summary>Adds a window. While grouping is armed, it joins the focused window's group.</summary>
    public void Add(long id) => Add(id, new TileRect(0, 0, 1, 1));

    /// <summary>
    /// Adds a window into <paramref name="area"/>. The focused tile splits along its longer side,
    /// the same way Hyprland dwindle does: beside when it is wider, below when it is taller.
    /// </summary>
    public void Add(long id, TileRect area)
    {
        if (_tree.Contains(id))
        {
            _focused = id;
            Remember(id);
            Touch();
            return;
        }

        if (GroupingArmed && _focused is long host && _tree.Contains(host))
        {
            _tree.Join(host, id);
            _focused = id;
            Remember(id);
            Touch();
            return;
        }

        if (_focused is long focused && _tree.Contains(focused))
        {
            _tree.Insert(id, focused, SplitBeside(focused, area));
        }
        else
        {
            _tree.Insert(id, null, area.Width >= area.Height);
        }

        _focused = id;
        Remember(id);
        Touch();
    }

    /// <summary>
    /// The tile <paramref name="id"/> would take if it were added now (<see cref="Add(long, TileRect)"/>). Nothing changes:
    /// the tree, the focus, and the tiles last computed are as they were. A window is put there before its program shows it.
    /// </summary>
    /// <param name="id">A window the session does not hold.</param>
    /// <param name="area">The rectangle the tiles share.</param>
    /// <param name="cover">The monitor the fullscreen window covers.</param>
    /// <returns>
    /// Null when the session holds the window, when it would join a group or stand beside a strip, and when its tile would not be on the glass.
    /// </returns>
    public TileRect? Preview(long id, TileRect area, TileRect cover)
    {
        if (_tree.Contains(id) || HasStrips || _fullscreen is not null)
        {
            return null;
        }

        var focused = _focused;
        var beside = focused is long tile && _tree.Contains(tile) ? focused : null;
        var sideBySide = beside is long host ? SplitBeside(host, area) : area.Width >= area.Height;
        var memo = (_tilesMemo, _tilesVersion, _tilesArea, _tilesCover);
        _tree.Insert(id, beside, sideBySide);
        _focused = id;
        Remember(id);
        try
        {
            foreach (var placed in Compute(area, cover))
            {
                if (placed.Id == id)
                {
                    return placed.Visible && placed.Width > 0 && placed.Height > 0
                        ? new TileRect(placed.X, placed.Y, placed.Width, placed.Height)
                        : null;
                }
            }

            return null;
        }
        finally
        {
            _tree.Remove(id);
            _recent.Remove(id);
            _focused = focused;
            (_tilesMemo, _tilesVersion, _tilesArea, _tilesCover) = memo;
        }
    }

    /// <summary>
    /// Takes the layout the windows already show: each window gets the tile it covers in <paramref name="seen"/>.
    /// A desktop that starts uses this, so the windows stay where the last desktop left them.
    /// A session that already has windows is left alone.
    /// </summary>
    /// <param name="seen">The rectangle each window covers, the window used last first. Each window once.</param>
    /// <param name="focused">The window with the keyboard. One that is not in <paramref name="seen"/> focuses the first window.</param>
    /// <returns>True when the session took the layout.</returns>
    public bool Recall(IReadOnlyList<Placement> seen, long? focused = null)
    {
        ArgumentNullException.ThrowIfNull(seen);
        if (_tree.Windows.Count > 0 || seen.Count == 0)
        {
            return false;
        }

        _tree.Adopt(TileRecall.Read(seen));
        for (var i = seen.Count - 1; i >= 0; i--)
        {
            Remember(seen[i].Id);
        }

        _focused = focused is long id && _tree.Contains(id) ? id : seen[0].Id;
        Remember(_focused.Value);
        Touch();
        return true;
    }

    private bool SplitBeside(long focused, TileRect area)
    {
        var placed = Tiles(area).FirstOrDefault(item => item.Id == focused && item.Visible);
        if (placed.Id != focused || (placed.Width == 0 && placed.Height == 0))
        {
            return area.Width >= area.Height;
        }

        return placed.Width >= placed.Height;
    }

    /// <summary>
    /// Forgets a window and promotes its sibling when a split loses a child.
    /// Focus moves to the visible tile whose center is nearest the closed window.
    /// </summary>
    public void Remove(long id, TileRect area)
    {
        if (!_tree.Contains(id))
        {
            return;
        }

        long? next = null;
        if (_focused == id)
        {
            var placed = Tiles(area);
            var origin = placed.FirstOrDefault(item => item.Id == id);
            next = ClosestOther(placed, origin, id);
        }

        long? mate = null;
        if (_tree.TryGet(id, out var leaf))
        {
            foreach (var member in leaf.Members)
            {
                if (member != id)
                {
                    mate = member;
                    break;
                }
            }
        }

        _tree.Remove(id);

        // What its group shows now. The rest of the group was hidden behind it, so the nearest visible tile is never one of them,
        // and the keyboard and a fullscreen tile both stay with the group.
        long? shown = mate is long other && _tree.TryGet(other, out var group) ? group.ActiveId : null;
        if (_focused == id && shown is not null)
        {
            next = shown;
        }

        if (_fullscreen == id)
        {
            _fullscreen = shown;
        }

        _minimums.Remove(id);
        _reported.Remove(id);
        _kept.Remove(id);
        _askedIn.Remove(id);
        _floated.Remove(id);
        _fullWidth.Remove(id);
        _pseudo.Remove(id);
        _recent.Remove(id);
        _tucked.Remove(id);
        _unanswered.Remove(id);
        _savedWidth.Remove(id);
        if (_fullscreen == id)
        {
            _fullscreen = null;
        }

        if (_focused == id)
        {
            _focused = next;
            if (next is long kept)
            {
                Remember(kept);
            }
        }

        if (_tree.Windows.Count == 0)
        {
            GroupingArmed = false;
        }

        Touch();
    }

    /// <summary>Focuses a window the session already contains.</summary>
    public void Focus(long id)
    {
        if (!_tree.TryGet(id, out var leaf))
        {
            return;
        }

        // Asked for the window in front before every chord, and for a window that just opened.
        // The same window again moves no tile, and a new version would place them all once more.
        var index = leaf.Members.IndexOf(id);
        var full = _fullscreen;
        var same = _focused == id && leaf.Active == index;
        _focused = id;
        leaf.Active = index;
        CarryFullscreen(leaf);
        Remember(id);
        if (!same || _fullscreen != full)
        {
            Touch();
        }
    }

    // Fullscreen belongs to the tile. Left on the member that was shown, it kept that window over the screen
    // while the keyboard went to the one behind it.
    private void CarryFullscreen(TileLeaf leaf)
    {
        if (_fullscreen is long full && full != leaf.ActiveId && leaf.Members.Contains(full))
        {
            _fullscreen = leaf.ActiveId;
        }
    }

    /// <summary>Moves focus to the nearest visible tile in <paramref name="direction"/>.</summary>
    public long? FocusDirection(Direction direction, TileRect area)
    {
        var placed = Tiles(area).Where(item => item.Visible).ToList();
        if (_focused is not long focused)
        {
            return null;
        }

        var origin = placed.FirstOrDefault(item => item.Id == focused);
        if (origin.Id != focused)
        {
            return null;
        }

        var best = TileNeighbors.Nearest(placed, origin, focused, direction, _recent);
        if (best.Id == 0 && !placed.Any(item => item.Id == best.Id && item.Id != focused))
        {
            return _focused;
        }

        if (best.Width == 0 && best.Height == 0)
        {
            return _focused;
        }

        Focus(best.Id);
        return _focused;
    }

    /// <summary>Swaps the focused window with the neighbor in <paramref name="direction"/>.</summary>
    public void Swap(Direction direction, TileRect area)
    {
        if (_focused is not long focused)
        {
            return;
        }

        var placed = Tiles(area).Where(item => item.Visible).ToList();
        var origin = placed.FirstOrDefault(item => item.Id == focused);
        var neighbor = TileNeighbors.Nearest(placed, origin, focused, direction, _recent);
        if (neighbor.Id == 0 || !_tree.TryGet(focused, out var a) || !_tree.TryGet(neighbor.Id, out var b) || ReferenceEquals(a, b))
        {
            return;
        }

        _tree.Exchange(a, b);
        Touch();
    }

    /// <summary>Toggles whether the focused window is tiled.</summary>
    public void ToggleFloat()
    {
        if (_focused is not long id)
        {
            return;
        }

        if (!_floated.Add(id))
        {
            _floated.Remove(id);
        }

        Touch();
    }

    /// <summary>Toggles fullscreen for the focused window.</summary>
    public void ToggleFullscreen()
    {
        if (_focused is not long id)
        {
            return;
        }

        _fullscreen = _fullscreen == id ? null : id;
        Touch();
    }

    /// <summary>
    /// Places <paramref name="id"/> over the screen, or back on its tile, whichever window is focused.
    /// A window its own app made fullscreen is answered with this.
    /// </summary>
    /// <param name="id">A window the session contains.</param>
    /// <param name="covers">True covers the screen. False tiles the window again when it is the fullscreen one.</param>
    public void SetFullscreen(long id, bool covers)
    {
        if (!_tree.Contains(id))
        {
            return;
        }

        var next = covers ? id : _fullscreen == id ? null : _fullscreen;
        if (next == _fullscreen)
        {
            return;
        }

        _fullscreen = next;
        Touch();
    }

    /// <summary>
    /// Toggles full-width for the focused window: it fills the area the tiles share, whatever split it is in.
    /// That area is clear of the bar, so the bar stays up. The other tiles keep their places under it.
    /// </summary>
    public void ToggleFullWidth()
    {
        if (_focused is not long id)
        {
            return;
        }

        if (!_fullWidth.Add(id))
        {
            _fullWidth.Remove(id);
        }

        Touch();
    }

    /// <summary>Switches between dwindle and scrolling.</summary>
    public void ToggleLayout()
    {
        Layout = Layout == LayoutKind.Dwindle ? LayoutKind.Scrolling : LayoutKind.Dwindle;
        Touch();
    }

    /// <summary>Sets the layout, as a desktop that starts does with the one the windows carry before it builds any tile (ADR 0050).</summary>
    /// <param name="layout">The layout to take.</param>
    public void SetLayout(LayoutKind layout)
    {
        if (Layout == layout)
        {
            return;
        }

        Layout = layout;
        Touch();
    }

    /// <summary>Each group in tree order: a tile that holds more than one window.</summary>
    /// <returns>The members of each group in the order its strip lists them, and the one it shows. Empty while no window shares a tile.</returns>
    public IReadOnlyList<TileGroup> Groups()
    {
        if (_tree.Root is not { } root || !_tree.Grouped)
        {
            return [];
        }

        var groups = new List<TileGroup>();
        foreach (var leaf in TileTree.Leaves(root))
        {
            if (leaf.Members.Count > 1)
            {
                groups.Add(new TileGroup([.. leaf.Members], leaf.ActiveId));
            }
        }

        return groups;
    }

    /// <summary>
    /// Makes <paramref name="members"/> one group, in that order, showing <paramref name="shown"/>. A desktop that starts does this
    /// with the group the windows carry, after the tiles were read from their rectangles (ADR 0050). The group takes the tile of the shown window.
    /// A member the session does not hold is left out. Fewer than two that it holds make no group.
    /// </summary>
    /// <param name="members">The windows in the order the group lists them.</param>
    /// <param name="shown">The member the group shows. One that is not among the held members shows the first.</param>
    /// <returns>True when a group was made.</returns>
    public bool Regroup(IReadOnlyList<long> members, long shown)
    {
        ArgumentNullException.ThrowIfNull(members);
        var held = members.Where(id => _tree.Contains(id)).Distinct().ToList();
        if (held.Count < 2)
        {
            return false;
        }

        var host = held.Contains(shown) ? shown : held[0];
        _tree.Assemble(held, host);
        if (_focused is long focused && held.Contains(focused))
        {
            _focused = host;
            Remember(host);
        }

        Touch();
        return true;
    }

    /// <summary>Turns gutters on or off. The default is off.</summary>
    public void ToggleSpacing()
    {
        SpacingEnabled = !SpacingEnabled;
        Touch();
    }

    /// <summary>True while a Workspace that holds exactly one tiled window draws it square, as tall as the area. Off until <see cref="ToggleSquareTile"/>.</summary>
    public bool SquareTile { get; private set; }

    /// <summary>Turns Square tile on or off. The default is off.</summary>
    public void ToggleSquareTile()
    {
        SquareTile = !SquareTile;
        Touch();
    }

    /// <summary>Arms grouping, or disassembles the focused group when already armed.</summary>
    public void ToggleGroup()
    {
        Touch();
        if (!GroupingArmed)
        {
            GroupingArmed = true;
            return;
        }

        GroupingArmed = false;
        if (_focused is long id && _tree.TryGet(id, out var leaf) && leaf.Members.Count > 1)
        {
            _tree.Disassemble(leaf);
        }
    }

    /// <summary>Pulls the focused window out of its group into its own tile.</summary>
    public void LeaveGroup()
    {
        if (_focused is long id)
        {
            _tree.Leave(id);
            Touch();
        }
    }

    /// <summary>
    /// Moves an edge of the focused window by <paramref name="dx"/> and <paramref name="dy"/> pixels.
    /// A negative <paramref name="dx"/> expands the left edge. A positive <paramref name="dx"/> shrinks it.
    /// A negative <paramref name="dy"/> shrinks from the top. A positive <paramref name="dy"/> expands the bottom.
    /// A pseudotile changes its stored size instead of a split.
    /// </summary>
    public void Resize(int dx, int dy, TileRect area)
    {
        if ((dx == 0 && dy == 0) || _focused is not long id || !_tree.TryGet(id, out var leaf))
        {
            return;
        }

        if (_pseudo.TryGetValue(id, out var natural))
        {
            var width = natural.Width;
            var height = natural.Height;
            if (dx < 0)
            {
                width += -dx;
            }
            else if (dx > 0)
            {
                width = Math.Max(1, width - dx);
            }

            if (dy > 0)
            {
                height += dy;
            }
            else if (dy < 0)
            {
                height = Math.Max(1, height + dy);
            }

            _pseudo[id] = new WindowMinimum(id, width, height);
            Touch();
            return;
        }

        var tiled = Tiles(area).Where(item => item.Visible).ToList();
        if (tiled.Count < 2)
        {
            return;
        }

        var rect = tiled.FirstOrDefault(item => item.Id == id);
        if (rect.Id != id)
        {
            return;
        }

        if (dx != 0)
        {
            Nudge(leaf, rect, area, horizontal: true, delta: dx, tiled);
        }

        if (dy != 0)
        {
            var again = Tiles(area).Where(item => item.Visible).ToList();
            var vertical = again.FirstOrDefault(item => item.Id == id);
            if (vertical.Id == id)
            {
                Nudge(leaf, vertical, area, horizontal: false, delta: dy, again);
            }
        }
    }

    /// <summary>
    /// Remembers the width the focused window is drawn at now, for <see cref="RestoreWidth"/>. Each window has one saved width,
    /// kept until the window leaves this workspace.
    /// </summary>
    /// <param name="area">The work area the tiles are drawn in.</param>
    public void SaveWidth(TileRect area)
    {
        if (_focused is long id && DrawnWidth(id, area) is int width)
        {
            _savedWidth[id] = width;
        }
    }

    /// <summary>
    /// Resizes the focused window back to the width <see cref="SaveWidth"/> remembered for it, as far as <see cref="Resize"/> can move its edge.
    /// A window with no saved width is left alone.
    /// </summary>
    /// <param name="area">The work area the tiles are drawn in.</param>
    public void RestoreWidth(TileRect area)
    {
        if (_focused is long id && _savedWidth.TryGetValue(id, out var saved) && DrawnWidth(id, area) is int width)
        {
            // A positive x shrinks the window, and a negative one widens it.
            Resize(width - saved, 0, area);
        }
    }

    private int? DrawnWidth(long id, TileRect area)
    {
        foreach (var item in Tiles(area))
        {
            if (item.Id == id && item.Visible && item.Width > 0)
            {
                return item.Width;
            }
        }

        return null;
    }

    /// <summary>Toggles pseudotile for the focused window. The stored size starts as the tile it has now.</summary>
    public void TogglePseudo(TileRect area)
    {
        if (_focused is not long id || !_tree.Contains(id))
        {
            return;
        }

        if (!_pseudo.Remove(id))
        {
            var placed = Tiles(area).FirstOrDefault(item => item.Id == id && item.Visible);
            var width = placed.Id == id && placed.Width > 0 ? placed.Width : Math.Max(1, area.Width / 2);
            var height = placed.Id == id && placed.Height > 0 ? placed.Height : Math.Max(1, area.Height / 2);
            _pseudo[id] = new WindowMinimum(id, width, height);
        }

        Touch();
    }

    /// <summary>
    /// Shows another member of the focused group. When the focused window shares the last tile of <paramref name="area"/>
    /// with the windows that do not fit, shows another of those. Any other window is left alone.
    /// </summary>
    /// <param name="delta">How many windows forward, or back when negative. The cycle wraps.</param>
    /// <param name="area">The rectangle the tiles share.</param>
    public void CycleGroup(int delta, TileRect area)
    {
        if (delta == 0 || _focused is not long id || !_tree.TryGet(id, out var leaf))
        {
            return;
        }

        if (SharedWith(id, area) is { } shared)
        {
            var at = shared.IndexOf(id);
            Show(shared[((at + delta) % shared.Count + shared.Count) % shared.Count]);
            return;
        }

        if (leaf.Members.Count < 2)
        {
            return;
        }

        var count = leaf.Members.Count;
        var index = leaf.Members.IndexOf(id);
        Show(leaf.Members[((index + delta) % count + count) % count]);
    }

    /// <summary>Shows the 1-based member of the focused group.</summary>
    /// <param name="index">The member's place, from 1.</param>
    /// <param name="area">The rectangle the tiles share.</param>
    public void ShowGroupMember(int index, TileRect area)
    {
        if (_focused is not long id || !_tree.TryGet(id, out var leaf))
        {
            return;
        }

        // The strip of a shared last tile numbers its tabs, and a group in it has no strip of its own.
        var members = SharedWith(id, area) ?? leaf.Members;
        if (index >= 1 && index <= members.Count)
        {
            Show(members[index - 1]);
        }
    }

    /// <summary>Pulls the focused window into the group in <paramref name="direction"/>. A neighbor that is not a group stays put, unless grouping is armed.</summary>
    public void JoinNeighbor(Direction direction, TileRect area)
    {
        if (_focused is not long id)
        {
            return;
        }

        var placed = Tiles(area).Where(item => item.Visible).ToList();
        var origin = placed.FirstOrDefault(item => item.Id == id);
        if (origin.Id != id)
        {
            return;
        }

        var neighbor = TileNeighbors.Nearest(placed, origin, id, direction, _recent);
        if (neighbor.Id == 0 || neighbor.Id == id)
        {
            return;
        }

        // Super+G armed a group, so a neighbor that is still one window takes this one and the two are the group.
        if (_tree.MoveInto(id, neighbor.Id, lone: GroupingArmed))
        {
            Remember(id);
            Touch();
        }
    }

    /// <summary>
    /// Pulls <paramref name="id"/> out and splits the tile under <paramref name="x"/>, <paramref name="y"/>.
    /// The child slot follows the pointer's half of that tile. Dropping on itself puts the window back.
    /// </summary>
    public void Drop(long id, TileRect area, int x, int y)
    {
        if (!_tree.TryGet(id, out var from))
        {
            return;
        }

        var placed = Tiles(area);
        if (TileHit.At(placed, x, y) is not long hit || !_tree.TryGet(hit, out var hitLeaf) || ReferenceEquals(from, hitLeaf))
        {
            return;
        }

        _tree.Remove(id);
        Touch();
        var opened = Tiles(area);
        if (TileHit.At(opened, x, y) is not long host)
        {
            _tree.Insert(id, null, area.Width >= area.Height);
            _focused = id;
            Remember(id);
            Touch();
            return;
        }

        var tile = opened.First(item => item.Id == host && item.Visible);
        var sideBySide = tile.Width >= tile.Height;
        var asFirst = sideBySide ? x < tile.X + (tile.Width / 2) : y < tile.Y + (tile.Height / 2);
        _tree.Split(id, host, sideBySide, asFirst);
        _focused = id;
        Remember(id);
        Touch();
    }

    /// <summary>
    /// Flips the split on screen between side by side and stacked.
    /// The stored axis can already be the other way when a minimum would not fit, so the flip follows the tiles
    /// in <paramref name="area"/>. A size a window kept from an earlier tile is forgotten, so that old size cannot hold the flip back.
    /// </summary>
    /// <param name="area">The rectangle the tiles share.</param>
    public void ToggleSplit(TileRect area)
    {
        if (_focused is not long id || !_tree.TryGet(id, out var leaf))
        {
            return;
        }

        var split = _tree.ParentOf(leaf);
        if (split is null)
        {
            return;
        }

        split.SideBySide = ShownSideBySide(split, area) is bool side ? !side : !split.SideBySide;
        Touch();
        foreach (var tile in TileTree.Leaves(split))
        {
            foreach (var member in tile.Members)
            {
                if (_kept.Remove(member))
                {
                    Refresh(member);
                }
            }
        }
    }

    /// <summary>
    /// True when <paramref name="split"/> is drawn with its children beside each other.
    /// Null when the tiles do not show one axis.
    /// </summary>
    private bool? ShownSideBySide(TileSplit split, TileRect area)
    {
        var placed = Tiles(area);
        var first = Bounds(split.First, placed);
        var second = Bounds(split.Second, placed);
        if (first.Width <= 0 || first.Height <= 0 || second.Width <= 0 || second.Height <= 0)
        {
            return null;
        }

        var beside = first.X + first.Width <= second.X + 1 || second.X + second.Width <= first.X + 1;
        var stacked = first.Y + first.Height <= second.Y + 1 || second.Y + second.Height <= first.Y + 1;
        return beside == stacked ? null : beside;
    }

    /// <summary>
    /// Records the size <paramref name="id"/> says it can shrink to. Each report replaces the last one.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <param name="width">Smallest width the window accepts.</param>
    /// <param name="height">Smallest height the window accepts.</param>
    /// <returns>True when the minimum changed and the tiles should be computed again.</returns>
    public bool ReportMinimum(long id, int width, int height)
    {
        if (!_tree.Contains(id))
        {
            return false;
        }

        _reported[id] = new WindowMinimum(id, Math.Max(1, width), Math.Max(1, height));
        return Refresh(id);
    }

    /// <summary>
    /// Records that <paramref name="id"/> stayed larger than its tile. A size at or below zero means
    /// that side fit, so only the side that overflowed raises the minimum. The record only grows.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <param name="width">Width the window kept, or zero when its width fit the tile.</param>
    /// <param name="height">Height the window kept, or zero when its height fit the tile.</param>
    /// <returns>True when the minimum changed and the tiles should be computed again.</returns>
    public bool NoteMinimum(long id, int width, int height)
    {
        if (!_tree.Contains(id))
        {
            return false;
        }

        var kept = _kept.TryGetValue(id, out var current) ? current : new WindowMinimum(id, 1, 1);
        _kept[id] = new WindowMinimum(id, Math.Max(kept.Width, width), Math.Max(kept.Height, height));
        return Refresh(id);
    }

    /// <summary>
    /// Windows whose minimum is not known for <paramref name="area"/>: one that joined, one that was last asked in another area,
    /// and one that kept a larger size than its tile since it was asked. Asking crosses into the window's process, so the rest are not asked again.
    /// </summary>
    /// <param name="area">The rectangle the tiles share.</param>
    /// <param name="skip">Windows not to ask, such as the fullscreen one.</param>
    /// <returns>The windows to ask.</returns>
    internal List<long> MinimumsToAsk(TileRect area, IReadOnlySet<long>? skip)
    {
        var due = new List<long>();
        foreach (var id in _tree.Windows)
        {
            if (id == _fullscreen || skip?.Contains(id) == true)
            {
                continue;
            }

            if (!_askedIn.TryGetValue(id, out var asked) || asked != area)
            {
                due.Add(id);
            }
        }

        return due;
    }

    /// <summary>Records that <paramref name="id"/> answered its minimum for <paramref name="area"/>.</summary>
    internal void NoteAsked(long id, TileRect area)
    {
        _unanswered.Remove(id);
        if (_tree.Contains(id))
        {
            _askedIn[id] = area;
        }
    }

    /// <summary>
    /// Records that <paramref name="id"/> was asked its minimum for <paramref name="area"/> and did not answer.
    /// After <see cref="MinimumTries"/> in a row it counts as asked, so a window that never answers does not hold up every place.
    /// </summary>
    internal void NoteUnanswered(long id, TileRect area)
    {
        if (!_tree.Contains(id))
        {
            return;
        }

        var tries = _unanswered.GetValueOrDefault(id) + 1;
        if (tries < MinimumTries)
        {
            _unanswered[id] = tries;
            return;
        }

        _unanswered.Remove(id);
        _askedIn[id] = area;
    }

    /// <summary>Asks <paramref name="id"/> again on the next place, after it kept a larger size than its tile.</summary>
    internal void AskAgain(long id) => _askedIn.Remove(id);

    /// <summary>True when a place in <paramref name="area"/> would ask a window its minimum: <see cref="MinimumsToAsk"/> without the list.</summary>
    /// <param name="area">The rectangle the tiles share.</param>
    /// <returns>False when every window but the fullscreen one was asked in this area and has not been marked to ask again.</returns>
    internal bool AsksMinimum(TileRect area)
    {
        foreach (var id in _tree.Windows)
        {
            if (id != _fullscreen && (!_askedIn.TryGetValue(id, out var asked) || asked != area))
            {
                return true;
            }
        }

        return false;
    }

    private bool Refresh(long id)
    {
        var reported = _reported.TryGetValue(id, out var said) ? said : new WindowMinimum(id, 1, 1);
        var kept = _kept.TryGetValue(id, out var held) ? held : new WindowMinimum(id, 1, 1);
        var next = new WindowMinimum(id, Math.Max(reported.Width, kept.Width), Math.Max(reported.Height, kept.Height));
        if (_minimums.TryGetValue(id, out var current) && current == next)
        {
            return false;
        }

        _minimums[id] = next;
        Touch();
        return true;
    }

    /// <summary>Computes a placement for every window inside <paramref name="area"/>.</summary>
    public IReadOnlyList<Placement> Arrange(TileRect area) => Arrange(area, area);

    /// <summary>
    /// Computes a placement for every window inside <paramref name="area"/>.
    /// The fullscreen window covers <paramref name="cover"/>, whether the fullscreen chord or its own app made it so.
    /// </summary>
    /// <param name="area">The rectangle the tiles share.</param>
    /// <param name="cover">The monitor the fullscreen window covers, including the strip the bar reserves.</param>
    public IReadOnlyList<Placement> Arrange(TileRect area, TileRect cover) => WithStrips(Tiles(area, cover), null);

    /// <summary>
    /// The strip of each group in <paramref name="area"/>: the rectangle <see cref="GroupBar"/> keeps free above the group's window,
    /// the group's windows in order, and which one is shown. A window Super+G armed has a strip of one.
    /// A group whose shown window is fullscreen has none.
    /// The tile that the windows which do not fit share has a strip too, with a tab for each window in it, a group's members side by side.
    /// </summary>
    /// <param name="area">The rectangle the tiles share.</param>
    /// <param name="cover">The monitor the fullscreen window covers.</param>
    /// <returns>One strip per group and one for a shared last tile, or none when <see cref="GroupBar"/> is zero.</returns>
    public IReadOnlyList<GroupStrip> Strips(TileRect area, TileRect cover)
    {
        if (!HasStrips)
        {
            return [];
        }

        var strips = new List<GroupStrip>();
        WithStrips(Tiles(area, cover), strips);
        return strips;
    }

    /// <summary>The tiles without a group's strip taken out. What the session measures neighbors, splits, and drops with.</summary>
    private IReadOnlyList<Placement> Tiles(TileRect area) => Tiles(area, area);

    private IReadOnlyList<Placement> Tiles(TileRect area, TileRect cover)
    {
        // Fullscreen belongs to the tile, so it is on whichever member the tile shows. A window that joined the group,
        // or was dropped into it, is shown without passing through a cycle.
        if (_fullscreen is long full && _tree.TryGet(full, out var leaf) && leaf.ActiveId != full)
        {
            _fullscreen = leaf.ActiveId;
            Touch();
        }

        // Asked two to four times for one arrange or chord: by the fit, by the group strips, and by the chord itself.
        // Nothing that is handed this list writes to it. Only the fullscreen window is placed on the cover.
        if (_tilesVersion != Version || _tilesArea != area || (_fullscreen is not null && _tilesCover != cover))
        {
            _tilesMemo = Compute(area, cover);
            _tilesVersion = Version;
            _tilesArea = area;
            _tilesCover = cover;
        }

        return _tilesMemo;
    }

    private IReadOnlyList<Placement> Compute(TileRect area, TileRect cover)
    {
        return TileArranger.Arrange(
            _tree,
            Layout,
            SpacingEnabled,
            Spacing,
            _focused,
            _fullscreen,
            _floated,
            _fullWidth,
            _minimums,
            _pseudo,
            area,
            cover,
            _recent,
            GroupBar,
            _shared,
            SquareTile);
    }

    /// <summary>Moves each group's windows down by <see cref="GroupBar"/>, and names the strip that leaves when <paramref name="strips"/> is given.</summary>
    private IReadOnlyList<Placement> WithStrips(IReadOnlyList<Placement> tiles, List<GroupStrip>? strips)
    {
        if (GroupBar <= 0)
        {
            return tiles;
        }

        // Everything a strip depends on computes the tiles again, so the same tiles lose the same strips.
        // A new list on every ask is never the one last placed, and a workspace with a group was then placed on every arrange.
        if (strips is null && ReferenceEquals(tiles, _strippedFrom))
        {
            return _strippedMemo;
        }

        List<Placement>? moved = null;
        for (var i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            if (tile.Raised || tile.Height <= GroupBar * 2 || !_tree.TryGet(tile.Id, out var leaf))
            {
                continue;
            }

            // A group in the shared tile is under that tile's strip, and has none of its own.
            var sharing = _shared.Count > 1 && _shared.Contains(tile.Id);
            if (!sharing && !HasStrip(leaf))
            {
                continue;
            }

            moved ??= [.. tiles];
            moved[i] = tile with { Y = tile.Y + GroupBar, Height = tile.Height - GroupBar };
            if (strips is null || !tile.Visible)
            {
                continue;
            }

            var members = sharing ? _shared.ToArray() : leaf.Members.Where(id => !_floated.Contains(id)).ToArray();
            strips.Add(new GroupStrip(new TileRect(tile.X, tile.Y, tile.Width, GroupBar), members, Math.Max(0, Array.IndexOf(members, tile.Id))));
        }

        var stripped = moved ?? tiles;
        if (strips is null)
        {
            _strippedFrom = tiles;
            _strippedMemo = stripped;
        }

        return stripped;
    }

    /// <summary>The windows that share the last tile of <paramref name="area"/> with <paramref name="id"/>, or null when it has a tile of its own.</summary>
    private List<long>? SharedWith(long id, TileRect area)
    {
        Tiles(area);
        return _shared.Count > 1 && _shared.Contains(id) ? _shared : null;
    }

    /// <summary>Shows <paramref name="id"/> in its tile and focuses it, as a step of a cycle does.</summary>
    private void Show(long id)
    {
        if (!_tree.TryGet(id, out var leaf))
        {
            return;
        }

        leaf.Active = leaf.Members.IndexOf(id);
        _focused = id;
        CarryFullscreen(leaf);

        // Fullscreen belongs to the shared tile as it does to a group's. Both callers computed the tiles just before.
        if (_fullscreen is long full && full != id && _shared.Contains(full) && _shared.Contains(id))
        {
            _fullscreen = id;
        }

        Remember(id);
        Touch();
    }

    private bool HasStrip(TileLeaf leaf) =>
        leaf.Members.Count > 1 || (GroupingArmed && _focused is long focused && leaf.Members.Contains(focused));

    private void Remember(long id)
    {
        _recent.Remove(id);
        _recent.Insert(0, id);
    }

    private static long? ClosestOther(IReadOnlyList<Placement> placed, Placement origin, long id)
    {
        long? best = null;
        var bestDistance = long.MaxValue;
        var originX = origin.Id == id ? origin.X + (origin.Width / 2) : 0;
        var originY = origin.Id == id ? origin.Y + (origin.Height / 2) : 0;
        foreach (var item in placed)
        {
            if (!item.Visible || item.Id == id)
            {
                continue;
            }

            var dx = (item.X + (item.Width / 2)) - originX;
            var dy = (item.Y + (item.Height / 2)) - originY;
            var distance = ((long)dx * dx) + ((long)dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = item.Id;
            }
        }

        return best;
    }

    private void Nudge(TileLeaf leaf, Placement rect, TileRect area, bool horizontal, int delta, IReadOnlyList<Placement> placed)
    {
        var spanStart = horizontal ? rect.X : rect.Y;
        var span = horizontal ? rect.Width : rect.Height;
        var areaStart = horizontal ? area.X : area.Y;
        var areaSpan = horizontal ? area.Width : area.Height;
        if (spanStart <= areaStart + 2 && spanStart + span >= areaStart + areaSpan - 2)
        {
            return;
        }

        var shrink = horizontal ? delta > 0 : delta < 0;
        var pixels = Math.Abs(delta);
        var startIsScreen = spanStart <= areaStart + 2;
        var endIsScreen = spanStart + span >= areaStart + areaSpan - 2;
        bool moveStart;
        if (horizontal)
        {
            moveStart = !startIsScreen;
        }
        else if (shrink)
        {
            moveStart = !startIsScreen;
        }
        else
        {
            moveStart = endIsScreen;
        }

        if ((moveStart && startIsScreen) || (!moveStart && endIsScreen))
        {
            return;
        }

        var (boundary, inner) = SplitsForEdge(leaf, horizontal, moveStart);
        if (boundary is null)
        {
            return;
        }

        var firstBox = Bounds(boundary.First, placed);
        var secondBox = Bounds(boundary.Second, placed);
        if (firstBox.Width <= 0 || secondBox.Width <= 0)
        {
            return;
        }

        var gap = SpacingEnabled ? Spacing : 0;
        var parentSpan = horizontal
            ? (secondBox.X + secondBox.Width) - firstBox.X
            : (secondBox.Y + secondBox.Height) - firstBox.Y;
        var usable = parentSpan - gap;
        if (usable <= 1)
        {
            return;
        }

        var firstSpan = horizontal ? firstBox.Width : firstBox.Height;
        var boundaryDelta = moveStart
            ? (horizontal ? (shrink ? pixels : -pixels) : (shrink ? pixels : -pixels))
            : (horizontal ? (shrink ? -pixels : pixels) : (shrink ? -pixels : pixels));
        // The arranger rounds down, so each ratio aims at the middle of its pixel. Aimed at the pixel's edge,
        // 505 of 1920 came back as 504, and the neighbor that keeps its size moved by that pixel.
        var nextRatio = Math.Clamp((firstSpan + boundaryDelta + 0.5) / usable, 0.05, 0.95);
        var actualFirst = (int)(usable * nextRatio);
        var actualDelta = actualFirst - firstSpan;
        boundary.Ratio = nextRatio;
        Touch();
        if (inner is null)
        {
            return;
        }

        var innerBox = Bounds(inner, placed);
        var innerSpan = horizontal ? innerBox.Width : innerBox.Height;
        if (innerSpan <= 0)
        {
            return;
        }

        var growth = InSecond(boundary, inner) ? -actualDelta : actualDelta;
        var newUsable = innerSpan + growth - gap;
        if (newUsable <= 1)
        {
            return;
        }

        var leafFirst = !InSecond(inner, leaf);
        var sibling = Bounds(leafFirst ? inner.Second : inner.First, placed);
        var siblingSpan = horizontal ? sibling.Width : sibling.Height;
        var innerRatio = leafFirst
            ? (newUsable - siblingSpan + 0.5) / newUsable
            : (siblingSpan + 0.5) / newUsable;
        inner.Ratio = Math.Clamp(innerRatio, 0.05, 0.95);
        Touch();
    }

    private (TileSplit? Boundary, TileSplit? Inner) SplitsForEdge(TileLeaf leaf, bool sideBySide, bool startEdge)
    {
        TileSplit? boundary = null;
        TileSplit? inner = null;
        TileNode node = leaf;
        for (var parent = _tree.ParentOf(node); parent is not null; parent = _tree.ParentOf(node))
        {
            if (parent.SideBySide == sideBySide)
            {
                if (InSecond(parent, leaf) == startEdge)
                {
                    boundary = parent;
                    break;
                }

                inner ??= parent;
            }

            node = parent;
        }

        return (boundary, inner);
    }

    private bool InSecond(TileSplit split, TileNode node)
    {
        TileNode current = node;
        while (true)
        {
            var parent = _tree.ParentOf(current);
            if (parent is null)
            {
                return false;
            }

            if (ReferenceEquals(parent, split))
            {
                return ReferenceEquals(parent.Second, current);
            }

            current = parent;
        }
    }

    private static TileRect Bounds(TileNode node, IReadOnlyList<Placement> placed)
    {
        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;
        var any = false;
        foreach (var leaf in TileTree.Leaves(node))
        {
            foreach (var id in leaf.Members)
            {
                var item = placed.FirstOrDefault(placement => placement.Id == id && placement.Visible);
                if (item.Id != id || !item.Visible)
                {
                    continue;
                }

                any = true;
                left = Math.Min(left, item.X);
                top = Math.Min(top, item.Y);
                right = Math.Max(right, item.X + item.Width);
                bottom = Math.Max(bottom, item.Y + item.Height);
            }
        }

        return any ? new TileRect(left, top, right - left, bottom - top) : default;
    }
}
